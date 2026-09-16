using System.Security.Claims;

using JulOS.Application.Catalog;
using JulOS.Contracts.Catalog;
using JulOS.Server.Authentication;
using JulOS.Server.Authorization;
using JulOS.Server.Operations;
using JulOS.Server.Errors;

using Microsoft.AspNetCore.Antiforgery;

namespace JulOS.Server.Catalog;

/// <summary>
/// Maps the catalog-source administration and publisher-key trust contract from
/// <c>docs/APPLICATION_CATALOG.md</c>.
/// </summary>
/// <remarks>
/// The two surfaces are guarded by two permissions on purpose. <c>catalog.sources.manage</c>
/// says where this installation looks for application definitions;
/// <c>catalog.trust.manage</c> says whose signature is enough to install from, which is the
/// larger decision and is held separately.
/// </remarks>
internal static class CatalogSourceEndpoints
{
    internal static IEndpointRouteBuilder MapJulOsCatalogSources(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var sources = endpoints.MapGroup("/api/v1/catalog/sources").WithTags("Catalog");

        sources.MapGet(string.Empty, ListAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.CatalogSourcesManage);
        sources.MapPost(string.Empty, AddAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.CatalogSourcesManage)
            .RequireJulOsAntiforgery();
        sources.MapGet("/{catalogSourceId:guid}", ReadAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.CatalogSourcesManage);
        sources.MapPut("/{catalogSourceId:guid}", UpdateAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.CatalogSourcesManage)
            .RequireJulOsAntiforgery();
        sources.MapDelete("/{catalogSourceId:guid}", RemoveAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.CatalogSourcesManage)
            .RequireJulOsAntiforgery();
        sources.MapPost("/{catalogSourceId:guid}/refresh", RefreshAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.CatalogSourcesManage)
            .RequireJulOsAntiforgery();

        // Key metadata is fingerprints and validity, not configuration, so reading it is
        // the catalog read permission. Deciding about it is not.
        sources.MapGet("/{catalogSourceId:guid}/publisher-keys", ListPublisherKeysAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.CatalogRead);

        // The cached catalog is content rather than configuration, so reading it is the
        // catalog read permission and not the one that decides where to look.
        var apps = endpoints.MapGroup("/api/v1/catalog/apps").WithTags("Catalog");

        apps.MapGet(string.Empty, ListApplicationsAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.CatalogRead);
        apps.MapGet("/{catalogSourceId:guid}/{appId}", ReadApplicationAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.CatalogRead);

        var keys = endpoints.MapGroup("/api/v1/catalog/publisher-keys").WithTags("Catalog");

        keys.MapGet("/{catalogPublisherKeyId:guid}", ReadPublisherKeyAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.CatalogRead);
        keys.MapPut("/{catalogPublisherKeyId:guid}/administrator-trust", SetPublisherKeyTrustAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.CatalogTrustManage)
            .RequireJulOsAntiforgery();

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        bool? includeRemoved,
        ICatalogSourceService catalog,
        CancellationToken cancellationToken)
    {
        var sources = await catalog
            .ListAsync(includeRemoved ?? false, cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(sources);
    }

    private static async Task<IResult> ReadAsync(
        Guid catalogSourceId,
        ICatalogSourceService catalog,
        CancellationToken cancellationToken)
    {
        var source = await catalog.ReadAsync(catalogSourceId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(source);
    }

    private static async Task<IResult> AddAsync(
        HttpContext context,
        AddCatalogSourceRequest request,
        IAntiforgery antiforgery,
        ICatalogSourceService catalog,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);

        var source = await catalog
            .AddAsync(
                request,
                CurrentUserId(context.User),
                CorrelationId.Get(context),
                context.Connection.RemoteIpAddress?.ToString(),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created(
            $"/api/v1/catalog/sources/{source.CatalogSourceId:D}",
            source);
    }

    private static async Task<IResult> UpdateAsync(
        HttpContext context,
        Guid catalogSourceId,
        UpdateCatalogSourceRequest request,
        IAntiforgery antiforgery,
        ICatalogSourceService catalog,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);

        var source = await catalog
            .UpdateAsync(
                catalogSourceId,
                request,
                CurrentUserId(context.User),
                CorrelationId.Get(context),
                context.Connection.RemoteIpAddress?.ToString(),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(source);
    }

    private static async Task<IResult> RemoveAsync(
        HttpContext context,
        Guid catalogSourceId,
        int expectedRevision,
        IAntiforgery antiforgery,
        ICatalogSourceService catalog,
        CancellationToken cancellationToken)
    {
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);

        // The tombstone is returned rather than 204: removing a source changes what the
        // caller is looking at instead of making it disappear, and the response says so.
        var source = await catalog
            .RemoveAsync(
                catalogSourceId,
                expectedRevision,
                CurrentUserId(context.User),
                CorrelationId.Get(context),
                context.Connection.RemoteIpAddress?.ToString(),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(source);
    }

    private static async Task<IResult> RefreshAsync(
        HttpContext context,
        Guid catalogSourceId,
        IAntiforgery antiforgery,
        ICatalogRefreshService refresh,
        CancellationToken cancellationToken)
    {
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);

        // The refresh reads a whole remote catalog, so the request returns the durable
        // operation and the caller follows that instead of holding a connection open.
        var operation = await refresh
            .RequestAsync(
                catalogSourceId,
                CurrentUserId(context.User),
                CorrelationId.Get(context),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Accepted(
            $"/api/v1/operations/{operation.OperationId:D}",
            OperationEndpoints.ToResponse(operation));
    }

    private static async Task<IResult> ListApplicationsAsync(
        Guid? catalogSourceId,
        ICatalogApplicationService applications,
        CancellationToken cancellationToken)
    {
        var apps = await applications.ListAsync(catalogSourceId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(apps);
    }

    private static async Task<IResult> ReadApplicationAsync(
        Guid catalogSourceId,
        string appId,
        string? version,
        ICatalogApplicationService applications,
        CancellationToken cancellationToken)
    {
        var application = await applications
            .ReadAsync(catalogSourceId, appId, version, cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(application);
    }

    private static async Task<IResult> ListPublisherKeysAsync(
        Guid catalogSourceId,
        ICatalogSourceService catalog,
        CancellationToken cancellationToken)
    {
        var keys = await catalog
            .ListPublisherKeysAsync(catalogSourceId, cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(keys);
    }

    private static async Task<IResult> ReadPublisherKeyAsync(
        Guid catalogPublisherKeyId,
        ICatalogSourceService catalog,
        CancellationToken cancellationToken)
    {
        var key = await catalog
            .ReadPublisherKeyAsync(catalogPublisherKeyId, cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(key);
    }

    private static async Task<IResult> SetPublisherKeyTrustAsync(
        HttpContext context,
        Guid catalogPublisherKeyId,
        SetPublisherKeyTrustRequest request,
        IAntiforgery antiforgery,
        ICatalogSourceService catalog,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);

        var key = await catalog
            .SetPublisherKeyTrustAsync(
                catalogPublisherKeyId,
                request,
                CurrentUserId(context.User),
                CorrelationId.Get(context),
                context.Connection.RemoteIpAddress?.ToString(),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(key);
    }

    private static Guid CurrentUserId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
}

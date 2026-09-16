using JulOS.Application.Packages;
using JulOS.Contracts.Packages;
using JulOS.Server.Authentication;
using JulOS.Server.Authorization;
using JulOS.Server.SafeMode;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace JulOS.Server.Packages;

/// <summary>Confirms installing one official package.</summary>
/// <param name="AcknowledgementDigest">The digest its preview produced, when one is required.</param>
internal sealed record InstallOfficialPackageRequest(string? AcknowledgementDigest);

internal sealed class InstallPackageForm
{
    public required IFormFile Artifact { get; init; }

    /// <summary>The publisher signature, absent for an unsigned artifact.</summary>
    public IFormFile? Signature { get; init; }

    public string? ExpectedDigest { get; init; }

    /// <summary>Claimed publisher identity, absent for an unsigned artifact.</summary>
    public string? PublisherId { get; init; }

    /// <summary>Claimed publisher key identity, absent for an unsigned artifact.</summary>
    public string? PublisherKeyId { get; init; }

    /// <summary>
    /// Base64 SubjectPublicKeyInfo for a publisher this installation has no configured key
    /// for. It can only ever produce an unknown-signed result.
    /// </summary>
    public string? PublisherPublicKeySpki { get; init; }

    /// <summary>The acknowledgement digest the preview produced, when one is required.</summary>
    public string? AcknowledgementDigest { get; init; }

    public required string OperationKey { get; init; }
}

internal static class PackageEndpoints
{
    internal static IEndpointRouteBuilder MapJulOsPackages(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup("/api/v1/packages").WithTags("Packages");

        group.MapGet("/", ListAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.PackageRead);
        group.MapGet("/catalog", ListCatalogAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.PackageRead);
        group.MapPost("/catalog/{packageId}/previews", PreviewOfficialAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.PackageManage)
            .RequireJulOsAntiforgery();
        group.MapPost("/catalog/{packageId}/install", InstallOfficialAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.PackageManage)
            .RequireJulOsAntiforgery();
        group.MapPost("/previews", PreviewAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.PackageManage)
            .RequireJulOsAntiforgery()
            .DisableAntiforgery();
        group.MapPost("/install", InstallAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.PackageManage)
            .RequireJulOsAntiforgery()
            .DisableAntiforgery();
        group.MapPut("/{packageId}/configuration", ConfigureAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.PackageManage)
            .RequireJulOsAntiforgery();
        group.MapPost("/{packageId}/enable", EnableAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.PackageManage)
            .RequireJulOsAntiforgery();
        group.MapPost("/{packageId}/disable", DisableAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.PackageManage)
            .RequireJulOsAntiforgery();
        group.MapDelete("/{packageId}", RemoveAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.PackageManage)
            .RequireJulOsAntiforgery();
        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        IPackageManagementService service,
        CancellationToken cancellationToken)
    {
        var packages = await service.ListAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(packages.Select(ToResponse).ToArray());
    }

    private static async Task<IResult> ListCatalogAsync(
        IOfficialPackageStoreService store,
        CancellationToken cancellationToken)
    {
        var packages = await store.ListAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(packages.Select(item => new OfficialPackageStoreResponse(
            item.Package.PackageId,
            item.Package.Version,
            item.Package.DisplayNameEn,
            item.Package.DisplayNameDe,
            item.Package.DescriptionEn,
            item.Package.DescriptionDe,
            item.Installation?.Version,
            item.Installation?.State,
            item.Installation?.Revision,
            item.Installation is not null
                && !string.Equals(item.Installation.Version, item.Package.Version, StringComparison.Ordinal))).ToArray());
    }

    private static async Task<IResult> PreviewOfficialAsync(
        HttpContext context,
        string packageId,
        IAntiforgery antiforgery,
        IOfficialPackageStoreService store,
        CancellationToken cancellationToken)
    {
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);
        try
        {
            return TypedResults.Ok(
                await store.PreviewAsync(packageId, cancellationToken).ConfigureAwait(false));
        }
        catch (PackageManagementException exception)
        {
            return Failure(exception);
        }
    }

    private static async Task<IResult> InstallOfficialAsync(
        HttpContext context,
        string packageId,
        InstallOfficialPackageRequest? request,
        IAntiforgery antiforgery,
        IOfficialPackageStoreService store,
        SafeModeState safeMode,
        CancellationToken cancellationToken)
    {
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);
        if (safeMode.Enabled)
        {
            return Results.Conflict(new
            {
                code = "package.safe_mode",
                detail = "Optional packages cannot be installed or enabled while JulOS is in safe mode.",
            });
        }

        try
        {
            return TypedResults.Ok(ToResponse(await store
                .InstallOrUpdateAsync(packageId, request?.AcknowledgementDigest, cancellationToken)
                .ConfigureAwait(false)));
        }
        catch (PackageManagementException exception)
        {
            return Failure(exception);
        }
    }

    private static async Task<IResult> InstallAsync(
        HttpContext context,
        [FromForm] InstallPackageForm form,
        IAntiforgery antiforgery,
        IPackageManagementService service,
        CancellationToken cancellationToken)
    {
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);
        var input = await ReadInputAsync(form, cancellationToken).ConfigureAwait(false);
        if (input is null)
        {
            return Results.BadRequest(new { code = "package.upload_invalid", detail = "Package upload is invalid." });
        }

        await using var artifact = input.Value.Artifact;
        try
        {
            var package = await service.InstallAsync(input.Value.Input, cancellationToken).ConfigureAwait(false);
            return TypedResults.Created($"/api/v1/packages/{package.PackageId}", ToResponse(package));
        }
        catch (PackageManagementException exception)
        {
            return Failure(exception);
        }
    }

    /// <summary>Reports what installing the uploaded artifact would mean, changing nothing.</summary>
    private static async Task<IResult> PreviewAsync(
        HttpContext context,
        [FromForm] InstallPackageForm form,
        IAntiforgery antiforgery,
        IPackageManagementService service,
        CancellationToken cancellationToken)
    {
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);
        var input = await ReadInputAsync(form, cancellationToken).ConfigureAwait(false);
        if (input is null)
        {
            return Results.BadRequest(new { code = "package.upload_invalid", detail = "Package upload is invalid." });
        }

        await using var artifact = input.Value.Artifact;
        try
        {
            return TypedResults.Ok(await service
                .PreviewAsync(input.Value.Input, cancellationToken)
                .ConfigureAwait(false));
        }
        catch (PackageManagementException exception)
        {
            return Failure(exception);
        }
    }

    /// <summary>Reads one upload into the shared install input, or reports it unusable.</summary>
    /// <remarks>
    /// Preview and install take the identical upload on purpose: the acknowledgement is a
    /// digest over what was uploaded, so the two calls have to be able to see the same bytes.
    /// </remarks>
    private static async Task<(Stream Artifact, PackageInstallInput Input)?> ReadInputAsync(
        InstallPackageForm form,
        CancellationToken cancellationToken)
    {
        if (form.Artifact.Length <= 0 || form.Signature?.Length is <= 0 or > 4096)
        {
            return null;
        }

        var signature = Array.Empty<byte>();
        if (form.Signature is not null)
        {
            await using var stream = form.Signature.OpenReadStream();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            signature = buffer.ToArray();
        }

        var artifact = form.Artifact.OpenReadStream();
        return (artifact, new PackageInstallInput(
            artifact,
            signature,
            form.ExpectedDigest,
            form.PublisherId ?? string.Empty,
            form.PublisherKeyId ?? string.Empty,
            form.OperationKey,
            form.PublisherPublicKeySpki,
            form.AcknowledgementDigest));
    }

    private static async Task<IResult> ConfigureAsync(
        HttpContext context,
        string packageId,
        ConfigurePackageRequest request,
        IAntiforgery antiforgery,
        IPackageManagementService service,
        CancellationToken cancellationToken)
    {
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);
        try
        {
            return TypedResults.Ok(ToResponse(await service.ConfigureAsync(
                packageId,
                new PackageConfigurationInput(request.Values, request.Revision),
                cancellationToken).ConfigureAwait(false)));
        }
        catch (PackageManagementException exception)
        {
            return Failure(exception);
        }
    }

    private static async Task<IResult> EnableAsync(
        HttpContext context,
        string packageId,
        PackageRevisionRequest request,
        IAntiforgery antiforgery,
        IPackageManagementService service,
        SafeModeState safeMode,
        CancellationToken cancellationToken)
    {
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);
        if (safeMode.Enabled)
        {
            return Results.Conflict(new
            {
                code = "package.safe_mode",
                detail = "Optional packages cannot be enabled while JulOS is in safe mode.",
            });
        }

        try
        {
            return TypedResults.Ok(ToResponse(await service.EnableAsync(
                packageId,
                request.Revision,
                cancellationToken).ConfigureAwait(false)));
        }
        catch (PackageManagementException exception)
        {
            return Failure(exception);
        }
    }

    private static async Task<IResult> DisableAsync(
        HttpContext context,
        string packageId,
        PackageRevisionRequest request,
        IAntiforgery antiforgery,
        IPackageManagementService service,
        CancellationToken cancellationToken)
    {
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);
        try
        {
            return TypedResults.Ok(ToResponse(await service.DisableAsync(
                packageId,
                request.Revision,
                cancellationToken).ConfigureAwait(false)));
        }
        catch (PackageManagementException exception)
        {
            return Failure(exception);
        }
    }

    private static async Task<IResult> RemoveAsync(
        HttpContext context,
        string packageId,
        [FromBody] RemovePackageRequest request,
        IAntiforgery antiforgery,
        IPackageManagementService service,
        CancellationToken cancellationToken)
    {
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);
        try
        {
            return TypedResults.Ok(ToResponse(await service.RemoveAsync(
                packageId,
                new PackageRemovalInput(request.Revision, request.DeletePackageData),
                cancellationToken).ConfigureAwait(false)));
        }
        catch (PackageManagementException exception)
        {
            return Failure(exception);
        }
    }

    private static PackageInstallationResponse ToResponse(PackageInstallationSnapshot package) => new(
        package.InstallationId,
        package.PackageId,
        package.Version,
        package.State,
        package.Revision,
        package.FaultCode,
        package.FaultDetail,
        package.FaultedAtUtc,
        package.ConfigurationRequired,
        package.WorkerHealthy,
        package.ArtifactDigest);

    private static IResult Failure(PackageManagementException exception)
    {
        var status = exception.Code switch
        {
            "package.not_found" or "package.catalog_not_found" => StatusCodes.Status404NotFound,
            "package.already_installed" => StatusCodes.Status409Conflict,
            // The same upload is accepted once its preview has been confirmed, so this is a
            // state conflict rather than a malformed request.
            "package.acknowledgement_required" => StatusCodes.Status409Conflict,
            "package.configuration_invalid" => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status400BadRequest,
        };
        return Results.Json(new { code = exception.Code, detail = exception.Message }, statusCode: status);
    }
}

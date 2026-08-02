using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

using JulOS.Application.Secrets;
using JulOS.Contracts.Secrets;
using JulOS.Server.Authentication;
using JulOS.Server.Authorization;
using JulOS.Server.Errors;

using Microsoft.AspNetCore.Antiforgery;

namespace JulOS.Server.Secrets;

/// <summary>Maps opaque secret-reference metadata without ever returning a stored value.</summary>
internal static class SecretReferenceEndpoints
{
    internal static IEndpointRouteBuilder MapJulOsSecretReferences(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/api/v1/secret-references").WithTags("Secret references");

        group.MapPost(string.Empty, CreateAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.SecretManage)
            .RequireJulOsAntiforgery();
        group.MapGet("/{secretReferenceId:guid}", ReadAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.SecretRead);
        group.MapPost("/{secretReferenceId:guid}/rotation", RotateAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.SecretManage)
            .RequireJulOsAntiforgery();
        group.MapDelete("/{secretReferenceId:guid}", DeleteAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.SecretManage)
            .RequireJulOsAntiforgery();

        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        HttpContext context,
        CreateSecretReferenceRequest request,
        IAntiforgery antiforgery,
        ISecretReferenceService secrets,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);
        var bytes = Encoding.UTF8.GetBytes(request.SecretValue ?? string.Empty);

        try
        {
            var secret = await secrets.CreateAsync(
                new CreateSecretReferenceCommand(
                    CurrentUserId(context.User),
                    ParseScopeType(request.OwningScopeType),
                    request.OwningScopeId,
                    request.Purpose,
                    bytes,
                    CorrelationId.Get(context),
                    context.Connection.RemoteIpAddress?.ToString()),
                cancellationToken).ConfigureAwait(false);

            return TypedResults.Created(
                $"/api/v1/secret-references/{secret.SecretReferenceId:D}",
                ToResponse(secret));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static async Task<IResult> ReadAsync(
        Guid secretReferenceId,
        ISecretReferenceService secrets,
        CancellationToken cancellationToken)
    {
        var secret = await secrets.ReadAsync(secretReferenceId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(ToResponse(secret));
    }

    private static async Task<IResult> RotateAsync(
        HttpContext context,
        Guid secretReferenceId,
        RotateSecretReferenceRequest request,
        IAntiforgery antiforgery,
        ISecretReferenceService secrets,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);
        var bytes = Encoding.UTF8.GetBytes(request.SecretValue ?? string.Empty);

        try
        {
            var secret = await secrets.RotateAsync(
                new RotateSecretReferenceCommand(
                    secretReferenceId,
                    CurrentUserId(context.User),
                    bytes,
                    request.Revision,
                    CorrelationId.Get(context),
                    context.Connection.RemoteIpAddress?.ToString()),
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(ToResponse(secret));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static async Task<IResult> DeleteAsync(
        HttpContext context,
        Guid secretReferenceId,
        int revision,
        IAntiforgery antiforgery,
        ISecretReferenceService secrets,
        CancellationToken cancellationToken)
    {
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);
        _ = await secrets.DeleteAsync(
            new DeleteSecretReferenceCommand(
                secretReferenceId,
                CurrentUserId(context.User),
                revision,
                CorrelationId.Get(context),
                context.Connection.RemoteIpAddress?.ToString()),
            cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static SecretReferenceResponse ToResponse(SecretReferenceSnapshot secret) => new(
        secret.SecretReferenceId,
        ScopeName(secret.OwningScopeType),
        secret.OwningScopeId,
        secret.Purpose,
        secret.StorageProvider,
        secret.IsPresent,
        secret.CreatedAtUtc,
        secret.RotatedAtUtc,
        secret.DeletedAtUtc,
        secret.Revision);

    private static SecretOwningScopeType ParseScopeType(string value) => value switch
    {
        SecretReferenceScopeTypes.System => SecretOwningScopeType.System,
        SecretReferenceScopeTypes.Package => SecretOwningScopeType.Package,
        _ => throw new SecretReferenceFailureException(SecretReferenceFailureReason.Invalid),
    };

    private static string ScopeName(SecretOwningScopeType value) => value switch
    {
        SecretOwningScopeType.System => SecretReferenceScopeTypes.System,
        SecretOwningScopeType.Package => SecretReferenceScopeTypes.Package,
        _ => throw new InvalidOperationException("Unknown secret-reference scope."),
    };

    private static Guid CurrentUserId(ClaimsPrincipal principal)
    {
        var identifier = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(identifier, out var userId) && userId != Guid.Empty
            ? userId
            : throw new SecretReferenceFailureException(SecretReferenceFailureReason.NotFound);
    }
}

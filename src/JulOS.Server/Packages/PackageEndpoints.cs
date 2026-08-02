using JulOS.Application.Packages;
using JulOS.Contracts.Packages;
using JulOS.Server.Authentication;
using JulOS.Server.Authorization;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace JulOS.Server.Packages;

internal sealed class InstallPackageForm
{
    public required IFormFile Artifact { get; init; }

    public required IFormFile Signature { get; init; }

    public required string ExpectedDigest { get; init; }

    public required string PublisherId { get; init; }

    public required string PublisherKeyId { get; init; }

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

    private static async Task<IResult> InstallAsync(
        HttpContext context,
        [FromForm] InstallPackageForm form,
        IAntiforgery antiforgery,
        IPackageManagementService service,
        CancellationToken cancellationToken)
    {
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);
        if (form.Artifact.Length <= 0 || form.Signature.Length is <= 0 or > 4096)
        {
            return Results.BadRequest(new { code = "package.upload_invalid", detail = "Package upload is invalid." });
        }

        await using var artifact = form.Artifact.OpenReadStream();
        await using var signatureStream = form.Signature.OpenReadStream();
        using var signatureBuffer = new MemoryStream();
        await signatureStream.CopyToAsync(signatureBuffer, cancellationToken).ConfigureAwait(false);
        try
        {
            var package = await service.InstallAsync(new PackageInstallInput(
                artifact,
                signatureBuffer.ToArray(),
                form.ExpectedDigest,
                form.PublisherId,
                form.PublisherKeyId,
                form.OperationKey), cancellationToken).ConfigureAwait(false);
            return TypedResults.Created($"/api/v1/packages/{package.PackageId}", ToResponse(package));
        }
        catch (PackageManagementException exception)
        {
            return Failure(exception);
        }
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
        CancellationToken cancellationToken)
    {
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);
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
        RemovePackageRequest request,
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
            "package.not_found" => StatusCodes.Status404NotFound,
            "package.already_installed" => StatusCodes.Status409Conflict,
            "package.configuration_invalid" => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status400BadRequest,
        };
        return Results.Json(new { code = exception.Code, detail = exception.Message }, statusCode: status);
    }
}

using JulOS.Application.Packages;
using JulOS.Contracts.Packages;
using JulOS.Server.Authentication;
using JulOS.Server.Authorization;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace JulOS.Server.Packages;

internal sealed class UpdatePackageForm
{
    public required IFormFile Artifact { get; init; }

    public required IFormFile Signature { get; init; }

    public required string ExpectedDigest { get; init; }

    public required string PublisherId { get; init; }

    public required string PublisherKeyId { get; init; }

    public int Revision { get; init; }

    public bool AllowIrreversibleMigrations { get; init; }
}

internal static class PackageUpdateEndpoints
{
    internal static IEndpointRouteBuilder MapJulOsPackageUpdates(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapPost("/api/v1/packages/{packageId}/update-preview", PreviewAsync)
            .WithTags("Packages")
            .RequireAuthorization(JulOsAuthorizationPolicies.PackageManage)
            .DisableAntiforgery();
        endpoints.MapPost("/api/v1/packages/{packageId}/update", UpdateAsync)
            .WithTags("Packages")
            .RequireAuthorization(JulOsAuthorizationPolicies.PackageManage)
            .DisableAntiforgery();
        return endpoints;
    }

    private static async Task<IResult> PreviewAsync(
        HttpContext context,
        string packageId,
        [FromForm] UpdatePackageForm form,
        IAntiforgery antiforgery,
        IPackageUpdateService service,
        CancellationToken cancellationToken)
    {
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);
        var input = await ReadInputAsync(form, cancellationToken).ConfigureAwait(false);
        try
        {
            var preview = await service.PreviewAsync(packageId, input, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(new PackageUpdatePreviewResponse(
                preview.PackageId,
                preview.CurrentVersion,
                preview.TargetVersion,
                preview.NewMigrations,
                preview.IrreversibleMigrations,
                preview.RequiresExplicitApproval));
        }
        catch (PackageManagementException exception)
        {
            return Failure(exception);
        }
    }

    private static async Task<IResult> UpdateAsync(
        HttpContext context,
        string packageId,
        [FromForm] UpdatePackageForm form,
        IAntiforgery antiforgery,
        IPackageUpdateService service,
        CancellationToken cancellationToken)
    {
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);
        var input = await ReadInputAsync(form, cancellationToken).ConfigureAwait(false);
        try
        {
            var package = await service.UpdateAsync(packageId, input, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(new PackageInstallationResponse(
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
                package.ArtifactDigest));
        }
        catch (PackageManagementException exception)
        {
            return Failure(exception);
        }
    }

    private static async Task<PackageUpdateInput> ReadInputAsync(
        UpdatePackageForm form,
        CancellationToken cancellationToken)
    {
        if (form.Artifact.Length <= 0 || form.Signature.Length is <= 0 or > 4096 || form.Revision < 1)
        {
            throw new PackageManagementException("package.update_upload_invalid", "Package update upload is invalid.");
        }

        var artifact = new MemoryStream();
        await using (var stream = form.Artifact.OpenReadStream())
        {
            await stream.CopyToAsync(artifact, cancellationToken).ConfigureAwait(false);
        }
        artifact.Position = 0;
        using var signature = new MemoryStream();
        await using (var stream = form.Signature.OpenReadStream())
        {
            await stream.CopyToAsync(signature, cancellationToken).ConfigureAwait(false);
        }
        return new PackageUpdateInput(
            artifact,
            signature.ToArray(),
            form.ExpectedDigest,
            form.PublisherId,
            form.PublisherKeyId,
            form.Revision,
            form.AllowIrreversibleMigrations);
    }

    private static IResult Failure(PackageManagementException exception)
    {
        var status = exception.Code switch
        {
            "package.not_found" => StatusCodes.Status404NotFound,
            "package.update_irreversible_approval_required" => StatusCodes.Status409Conflict,
            "package.update_state_invalid" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };
        return Results.Json(new { code = exception.Code, detail = exception.Message }, statusCode: status);
    }
}

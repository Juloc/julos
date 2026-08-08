using System.Security.Claims;

using JulOS.Application.Packages;
using JulOS.Server.Authentication;

namespace JulOS.Server.Applications;

internal sealed record DesktopApplicationFrontendResponse(
    string ModuleUrl,
    string Sha256,
    IReadOnlyList<string> ExportedElements);

internal sealed record DesktopLaunchTargetResponse(
    Guid LaunchTargetId,
    Guid ApplicationDefinitionId,
    string ExternalIdentity,
    string DisplayName);

internal sealed record SaveDesktopLaunchTargetRequest(
    string ExternalIdentity,
    string DisplayName);

internal sealed record DesktopApplicationResponse(
    Guid ApplicationDefinitionId,
    string PackageId,
    string PackageVersion,
    string StableKey,
    string DisplayNameKey,
    string InstancePolicy,
    int DefaultWidth,
    int DefaultHeight,
    int MinimumWidth,
    int MinimumHeight,
    IReadOnlyList<string> Viewports,
    string ElementName,
    DesktopApplicationFrontendResponse Frontend,
    IReadOnlyList<DesktopLaunchTargetResponse> LaunchTargets);

internal sealed record DesktopWidgetResponse(
    string WidgetKey,
    string PackageId,
    string PackageVersion,
    string StableKey,
    string DisplayNameKey,
    string ElementName,
    IReadOnlyList<string> Sizes,
    string DefaultSize,
    DesktopApplicationFrontendResponse Frontend);

internal static class ApplicationEndpoints
{
    internal static IEndpointRouteBuilder MapJulOsApplications(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/api/v1/applications", ListAsync)
            .WithTags("Applications")
            .RequireAuthorization();
        endpoints.MapGet("/api/v1/widgets", ListWidgetsAsync)
            .WithTags("Applications")
            .RequireAuthorization();
        endpoints.MapGet("/api/v1/packages/{packageId}/frontend/{version}", FrontendAsync)
            .WithTags("Applications")
            .RequireAuthorization();
        endpoints.MapPost("/api/v1/packages/{packageId}/applications/{stableKey}/targets", SaveTargetAsync)
            .WithTags("Applications")
            .RequireAuthorization()
            .RequireJulOsAntiforgery();
        endpoints.MapDelete("/api/v1/packages/{packageId}/applications/targets/{targetId:guid}", DeleteTargetAsync)
            .WithTags("Applications")
            .RequireAuthorization()
            .RequireJulOsAntiforgery();
        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        string? viewport,
        IDesktopApplicationCatalog catalog,
        CancellationToken cancellationToken)
    {
        try
        {
            var applications = await catalog.ListAsync(viewport ?? "desktop", cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(applications.Select(ToResponse).ToArray());
        }
        catch (PackageManagementException exception)
        {
            return Failure(exception);
        }
    }

    private static async Task<IResult> SaveTargetAsync(
        HttpContext context,
        string packageId,
        string stableKey,
        SaveDesktopLaunchTargetRequest request,
        IDesktopApplicationCatalog catalog,
        CancellationToken cancellationToken)
    {
        try
        {
            var target = await catalog.SaveLaunchTargetAsync(
                CurrentUserId(context.User),
                packageId,
                stableKey,
                request.ExternalIdentity,
                request.DisplayName,
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(ToResponse(target));
        }
        catch (PackageManagementException exception)
        {
            return Failure(exception);
        }
    }

    private static async Task<IResult> DeleteTargetAsync(
        string packageId,
        Guid targetId,
        IDesktopApplicationCatalog catalog,
        CancellationToken cancellationToken)
    {
        try
        {
            await catalog.DeleteLaunchTargetAsync(packageId, targetId, cancellationToken).ConfigureAwait(false);
            return TypedResults.NoContent();
        }
        catch (PackageManagementException exception)
        {
            return Failure(exception);
        }
    }

    private static async Task<IResult> ListWidgetsAsync(
        IDesktopApplicationCatalog catalog,
        CancellationToken cancellationToken)
    {
        try
        {
            var widgets = await catalog.ListWidgetsAsync(cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(widgets.Select(ToResponse).ToArray());
        }
        catch (PackageManagementException exception)
        {
            return Failure(exception);
        }
    }

    private static async Task<IResult> FrontendAsync(
        string packageId,
        string version,
        IDesktopApplicationCatalog catalog,
        CancellationToken cancellationToken)
    {
        try
        {
            var frontend = await catalog.ReadFrontendAsync(packageId, version, cancellationToken).ConfigureAwait(false);
            return Results.Bytes(frontend.Content, "text/javascript; charset=utf-8");
        }
        catch (PackageManagementException exception)
        {
            return Failure(exception);
        }
    }

    private static DesktopApplicationResponse ToResponse(DesktopPackageApplication application) => new(
        application.ApplicationDefinitionId,
        application.PackageId,
        application.PackageVersion,
        application.StableKey,
        application.DisplayNameKey,
        application.InstancePolicy,
        application.DefaultWidth,
        application.DefaultHeight,
        application.MinimumWidth,
        application.MinimumHeight,
        application.Viewports,
        application.ElementName,
        Frontend(application.PackageId, application.PackageVersion, application.FrontendSha256, application.FrontendExportedElements),
        application.LaunchTargets.Select(ToResponse).ToArray());

    private static DesktopLaunchTargetResponse ToResponse(DesktopPackageLaunchTarget target) => new(
        target.LaunchTargetId,
        target.ApplicationDefinitionId,
        target.ExternalIdentity,
        target.DisplayName);

    private static DesktopWidgetResponse ToResponse(DesktopPackageWidget widget) => new(
        widget.WidgetKey,
        widget.PackageId,
        widget.PackageVersion,
        widget.StableKey,
        widget.DisplayNameKey,
        widget.ElementName,
        widget.Sizes,
        widget.DefaultSize,
        Frontend(widget.PackageId, widget.PackageVersion, widget.FrontendSha256, widget.FrontendExportedElements));

    private static DesktopApplicationFrontendResponse Frontend(
        string packageId,
        string version,
        string sha256,
        IReadOnlyList<string> exportedElements) => new(
        $"/api/v1/packages/{packageId}/frontend/{version}",
        sha256,
        exportedElements);

    private static Guid CurrentUserId(ClaimsPrincipal principal)
    {
        var identifier = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(identifier, out var userId) && userId != Guid.Empty
            ? userId
            : throw new PackageManagementException("authentication.user_invalid", "Authenticated user is invalid.");
    }

    private static IResult Failure(PackageManagementException exception)
    {
        var status = exception.Code switch
        {
            "package.not_found" or "package.frontend_not_found" or "application.target_not_found" => StatusCodes.Status404NotFound,
            "package.frontend_digest_mismatch" or "application.target_conflict" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };
        return Results.Json(new { code = exception.Code, detail = exception.Message }, statusCode: status);
    }
}

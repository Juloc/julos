using JulOS.Domain.Applications;
using JulOS.Domain.Primitives;
using JulOS.Infrastructure.Persistence.Core;
using JulOS.PackageSdk;

using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Packages;

/// <summary>Keeps the Core application registry aligned with one verified package manifest.</summary>
internal static class PackageApplicationRegistration
{
    public static async Task SynchronizeAsync(
        CoreDbContext context,
        PackageManifest manifest,
        bool enabled,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var existing = await context.ApplicationDefinitions
            .Include(row => row.SupportedViewports)
            .Where(row => row.OwningPackageId == manifest.PackageId)
            .ToDictionaryAsync(row => row.StableKey, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);
        var declared = manifest.Applications.ToDictionary(application => application.StableKey, StringComparer.Ordinal);

        foreach (var application in manifest.Applications)
        {
            if (!existing.TryGetValue(application.StableKey, out var row))
            {
                var id = Guid.CreateVersion7(timeProvider.GetUtcNow());
                row = new ApplicationDefinitionRow
                {
                    Id = id,
                    OwningPackageId = manifest.PackageId,
                    StableKey = application.StableKey,
                    DisplayNameKey = application.DisplayNameKey,
                    InstancePolicy = MapPolicy(application.InstancePolicy),
                    DefaultWidth = application.DefaultWidth,
                    DefaultHeight = application.DefaultHeight,
                    MinimumWidth = application.MinimumWidth,
                    MinimumHeight = application.MinimumHeight,
                    IsEnabled = enabled,
                    SurfaceContractVersion = application.Surface?.ContractVersion,
                    SurfaceSupportsKeepActive = SupportsKeepActive(application),
                    SurfaceHandlesBack = application.Surface?.HandlesBack ?? false,
                    Revision = 1,
                };
                foreach (var viewport in application.Viewports)
                {
                    row.SupportedViewports.Add(new ApplicationViewportRow
                    {
                        ApplicationDefinitionId = id,
                        ViewportClass = MapViewport(viewport),
                    });
                }
                context.ApplicationDefinitions.Add(row);
                continue;
            }

            var desiredPolicy = MapPolicy(application.InstancePolicy);
            var desiredViewports = application.Viewports.Select(MapViewport).ToHashSet();
            var currentViewports = row.SupportedViewports.Select(item => item.ViewportClass).ToHashSet();
            var changed = !string.Equals(row.DisplayNameKey, application.DisplayNameKey, StringComparison.Ordinal)
                || row.InstancePolicy != desiredPolicy
                || row.DefaultWidth != application.DefaultWidth
                || row.DefaultHeight != application.DefaultHeight
                || row.MinimumWidth != application.MinimumWidth
                || row.MinimumHeight != application.MinimumHeight
                || row.IsEnabled != enabled
                || !string.Equals(
                    row.SurfaceContractVersion,
                    application.Surface?.ContractVersion,
                    StringComparison.Ordinal)
                || row.SurfaceSupportsKeepActive != SupportsKeepActive(application)
                || row.SurfaceHandlesBack != (application.Surface?.HandlesBack ?? false)
                || !currentViewports.SetEquals(desiredViewports);
            if (!changed)
            {
                continue;
            }

            row.DisplayNameKey = application.DisplayNameKey;
            row.InstancePolicy = desiredPolicy;
            row.DefaultWidth = application.DefaultWidth;
            row.DefaultHeight = application.DefaultHeight;
            row.MinimumWidth = application.MinimumWidth;
            row.MinimumHeight = application.MinimumHeight;
            row.IsEnabled = enabled;
            row.SurfaceContractVersion = application.Surface?.ContractVersion;
            row.SurfaceSupportsKeepActive = SupportsKeepActive(application);
            row.SurfaceHandlesBack = application.Surface?.HandlesBack ?? false;
            row.Revision = checked(row.Revision + 1);

            if (!currentViewports.SetEquals(desiredViewports))
            {
                row.SupportedViewports.Clear();
                foreach (var viewport in desiredViewports)
                {
                    row.SupportedViewports.Add(new ApplicationViewportRow
                    {
                        ApplicationDefinitionId = row.Id,
                        ViewportClass = viewport,
                    });
                }
            }
        }

        foreach (var row in existing.Values)
        {
            if (!declared.ContainsKey(row.StableKey) && row.IsEnabled)
            {
                row.IsEnabled = false;
                row.Revision = checked(row.Revision + 1);
            }
        }
    }

    /// <summary>
    /// Whether the manifest declares that the application can stay active in the background.
    /// </summary>
    private static bool SupportsKeepActive(PackageApplicationManifest application) =>
        application.Surface?.SupportedBackgroundModes
            .Contains("keep-surface-active", StringComparer.Ordinal) ?? false;

    private static ApplicationInstancePolicy MapPolicy(string value) => value switch
    {
        "single-instance-per-user" => ApplicationInstancePolicy.SingleInstancePerUser,
        "single-instance-per-target" => ApplicationInstancePolicy.SingleInstancePerTarget,
        "multiple-instances" => ApplicationInstancePolicy.MultipleInstances,
        _ => throw new InvalidOperationException($"Unsupported application instance policy '{value}'."),
    };

    private static ViewportClass MapViewport(string value) => value switch
    {
        "desktop" => ViewportClass.Desktop,
        "tablet" => ViewportClass.Tablet,
        "mobile" => ViewportClass.Mobile,
        _ => throw new InvalidOperationException($"Unsupported viewport '{value}'."),
    };
}

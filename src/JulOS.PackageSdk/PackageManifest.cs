using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace JulOS.PackageSdk;

/// <summary>The only manifest schema supported by JulOS 1.0.</summary>
public static class PackageManifestSchema
{
    /// <summary>The supported schema version.</summary>
    public const string Version = "1";
}

/// <summary>The signed declaration of one JulOS package.</summary>
/// <param name="SchemaVersion">Manifest schema version.</param>
/// <param name="PackageId">Stable reverse-DNS package identity.</param>
/// <param name="Version">Package semantic version.</param>
/// <param name="PublisherId">Trusted publisher identity.</param>
/// <param name="DisplayNameKey">Localized display-name resource key.</param>
/// <param name="DescriptionKey">Localized description resource key.</param>
/// <param name="Runtime">Declared runtime requirements.</param>
/// <param name="Permissions">Explicitly requested permission names.</param>
/// <param name="Applications">Applications exported by the package.</param>
/// <param name="Widgets">Widgets exported by the package.</param>
/// <param name="Capabilities">Capabilities provided or required by the package.</param>
/// <param name="Migrations">Versioned package migration declarations.</param>
/// <param name="Frontend">Optional signed frontend module declaration.</param>
public sealed record PackageManifest(
    string SchemaVersion,
    string PackageId,
    string Version,
    string PublisherId,
    string DisplayNameKey,
    string DescriptionKey,
    PackageRuntimeManifest Runtime,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<PackageApplicationManifest> Applications,
    IReadOnlyList<PackageWidgetManifest> Widgets,
    IReadOnlyList<PackageCapabilityManifest> Capabilities,
    IReadOnlyList<PackageMigrationManifest> Migrations,
    PackageFrontendManifest? Frontend);

/// <summary>Runtime requirements declared by a package.</summary>
/// <param name="Kind">Runtime kind: none, container or process.</param>
/// <param name="Image">Immutable container image reference when applicable.</param>
/// <param name="EntryPoint">Package-owned process entry point when applicable.</param>
/// <param name="MemoryLimitMegabytes">Maximum runtime memory in MiB.</param>
/// <param name="CpuLimit">Maximum runtime CPU allocation.</param>
/// <param name="StartupTimeoutSeconds">Maximum startup duration.</param>
/// <param name="NetworkAccess">Whether the approved runtime network is required.</param>
public sealed record PackageRuntimeManifest(
    string Kind,
    string? Image,
    string? EntryPoint,
    int MemoryLimitMegabytes,
    decimal CpuLimit,
    int StartupTimeoutSeconds,
    bool NetworkAccess);

/// <summary>One application exported by a package.</summary>
/// <param name="StableKey">Package-scoped stable application key.</param>
/// <param name="DisplayNameKey">Localized display-name resource key.</param>
/// <param name="InstancePolicy">Single-user, single-target or multiple-instance policy.</param>
/// <param name="DefaultWidth">Default window width.</param>
/// <param name="DefaultHeight">Default window height.</param>
/// <param name="MinimumWidth">Minimum usable window width.</param>
/// <param name="MinimumHeight">Minimum usable window height.</param>
/// <param name="Viewports">Supported desktop viewport classes.</param>
public sealed record PackageApplicationManifest(
    string StableKey,
    string DisplayNameKey,
    string InstancePolicy,
    int DefaultWidth,
    int DefaultHeight,
    int MinimumWidth,
    int MinimumHeight,
    IReadOnlyList<string> Viewports);

/// <summary>One widget exported by a package.</summary>
/// <param name="StableKey">Package-scoped stable widget key.</param>
/// <param name="DisplayNameKey">Localized display-name resource key.</param>
/// <param name="ElementName">Registered custom-element name.</param>
/// <param name="Sizes">Supported widget size variants.</param>
/// <param name="DefaultSize">Default widget size.</param>
public sealed record PackageWidgetManifest(
    string StableKey,
    string DisplayNameKey,
    string ElementName,
    IReadOnlyList<string> Sizes,
    string DefaultSize);

/// <summary>One capability provided or required by a package.</summary>
/// <param name="Name">Capability identity.</param>
/// <param name="Direction">Provides or requires direction.</param>
/// <param name="ContractVersion">Capability contract version.</param>
/// <param name="Required">Whether absence blocks package enablement.</param>
public sealed record PackageCapabilityManifest(
    string Name,
    string Direction,
    string ContractVersion,
    bool Required);

/// <summary>One package migration declaration.</summary>
/// <param name="MigrationId">Stable migration identity.</param>
/// <param name="Resource">Migrated resource type.</param>
/// <param name="Reversible">Whether automatic rollback is safe.</param>
/// <param name="Digest">SHA-256 digest of the migration content.</param>
public sealed record PackageMigrationManifest(
    string MigrationId,
    string Resource,
    bool Reversible,
    string Digest);

/// <summary>Signed browser frontend exported by a package.</summary>
/// <param name="ModulePath">Package-relative JavaScript module path.</param>
/// <param name="Sha256">Expected module SHA-256 digest.</param>
/// <param name="ExportedElements">Custom elements registered by the module.</param>
public sealed record PackageFrontendManifest(
    string ModulePath,
    string Sha256,
    IReadOnlyList<string> ExportedElements);

/// <summary>Raised when a package manifest violates the public schema or rules.</summary>
public sealed class PackageManifestException : Exception
{
    /// <summary>Creates a package manifest validation failure.</summary>
    /// <param name="code">Stable machine-readable failure code.</param>
    /// <param name="message">Caller-safe explanation.</param>
    /// <param name="innerException">Optional parsing cause retained server-side.</param>
    public PackageManifestException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        this.Code = code;
    }

    /// <summary>Gets the stable machine-readable failure code.</summary>
    public string Code { get; }
}

/// <summary>Parses and validates manifests without external schema libraries.</summary>
public static partial class PackageManifestReader
{
    private const int MaximumManifestBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Reads and validates one manifest stream.</summary>
    /// <param name="stream">UTF-8 JSON manifest stream.</param>
    /// <returns>The validated manifest.</returns>
    public static PackageManifest Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (stream.CanSeek && stream.Length > MaximumManifestBytes)
        {
            throw new PackageManifestException("package.manifest_too_large", "The package manifest is too large.");
        }

        PackageManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<PackageManifest>(stream, Options)
                ?? throw new PackageManifestException("package.manifest_empty", "The package manifest is empty.");
        }
        catch (JsonException exception)
        {
            throw new PackageManifestException(
                "package.manifest_invalid_json",
                "The package manifest is not valid JSON or contains unsupported fields.",
                exception);
        }

        Validate(manifest);
        return manifest;
    }

    /// <summary>Validates one materialized package manifest.</summary>
    /// <param name="manifest">Manifest to validate.</param>
    public static void Validate(PackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (!string.Equals(manifest.SchemaVersion, PackageManifestSchema.Version, StringComparison.Ordinal))
        {
            throw new PackageManifestException(
                "package.schema_incompatible",
                $"Package schema '{manifest.SchemaVersion}' is not supported by this JulOS release.");
        }

        ValidatePackageId(manifest.PackageId);
        ValidateIdentifier(manifest.PublisherId, nameof(manifest.PublisherId));
        ValidateSemanticVersion(manifest.Version);
        ValidateResourceKey(manifest.DisplayNameKey, nameof(manifest.DisplayNameKey));
        ValidateResourceKey(manifest.DescriptionKey, nameof(manifest.DescriptionKey));
        ValidateRuntime(manifest.Runtime);

        if (manifest.Permissions.Count == 0)
        {
            throw new PackageManifestException(
                "package.permissions_missing",
                "Every package must explicitly declare its required permissions.");
        }
        EnsureDistinct(manifest.Permissions, "package.permission_duplicate");
        foreach (var permission in manifest.Permissions)
        {
            ValidatePermission(permission);
        }

        EnsureDistinct(manifest.Applications.Select(application => application.StableKey), "package.application_duplicate");
        foreach (var application in manifest.Applications)
        {
            ValidateApplication(application);
        }

        EnsureDistinct(manifest.Widgets.Select(widget => widget.StableKey), "package.widget_duplicate");
        foreach (var widget in manifest.Widgets)
        {
            ValidateWidget(widget);
        }

        EnsureDistinct(manifest.Capabilities.Select(capability => $"{capability.Direction}:{capability.Name}"), "package.capability_duplicate");
        foreach (var capability in manifest.Capabilities)
        {
            ValidateCapability(capability);
        }

        EnsureDistinct(manifest.Migrations.Select(migration => migration.MigrationId), "package.migration_duplicate");
        foreach (var migration in manifest.Migrations)
        {
            ValidateMigration(migration);
        }

        if (manifest.Frontend is not null)
        {
            ValidateFrontend(manifest.Frontend);
        }
    }

    private static void ValidateRuntime(PackageRuntimeManifest runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (runtime.Kind is not ("none" or "container" or "process"))
        {
            Fail("package.runtime_kind_invalid", "Runtime kind must be none, container or process.");
        }
        if (runtime.Kind == "container" && string.IsNullOrWhiteSpace(runtime.Image))
        {
            Fail("package.runtime_image_missing", "A container package must declare a pinned image.");
        }
        if (runtime.Image is not null && (runtime.Image.Contains(":latest", StringComparison.OrdinalIgnoreCase) || !runtime.Image.Contains('@')))
        {
            Fail("package.runtime_image_unpinned", "Container images must be pinned by digest.");
        }
        if (runtime.Kind == "process" && string.IsNullOrWhiteSpace(runtime.EntryPoint))
        {
            Fail("package.runtime_entrypoint_missing", "A process package must declare an entry point.");
        }
        if (runtime.MemoryLimitMegabytes is < 16 or > 32768
            || runtime.CpuLimit is <= 0 or > 32
            || runtime.StartupTimeoutSeconds is < 1 or > 300)
        {
            Fail("package.runtime_limits_invalid", "Package runtime limits are invalid.");
        }
    }

    private static void ValidateApplication(PackageApplicationManifest application)
    {
        ValidateStableKey(application.StableKey);
        ValidateResourceKey(application.DisplayNameKey, nameof(application.DisplayNameKey));
        if (application.InstancePolicy is not ("single-instance-per-user" or "single-instance-per-target" or "multiple-instances"))
        {
            Fail("package.application_policy_invalid", "Application instance policy is invalid.");
        }
        if (application.MinimumWidth is < 120 or > 16384
            || application.MinimumHeight is < 120 or > 16384
            || application.DefaultWidth is < 120 or > 16384
            || application.DefaultHeight is < 120 or > 16384
            || application.DefaultWidth < application.MinimumWidth
            || application.DefaultHeight < application.MinimumHeight)
        {
            Fail("package.application_size_invalid", "Application window sizes are invalid.");
        }
        if (application.Viewports.Count == 0
            || application.Viewports.Any(viewport => viewport is not ("desktop" or "tablet" or "mobile")))
        {
            Fail("package.application_viewports_invalid", "Application viewports are invalid.");
        }
        EnsureDistinct(application.Viewports, "package.application_viewport_duplicate");
    }

    private static void ValidateWidget(PackageWidgetManifest widget)
    {
        ValidateStableKey(widget.StableKey);
        ValidateResourceKey(widget.DisplayNameKey, nameof(widget.DisplayNameKey));
        if (!CustomElementName().IsMatch(widget.ElementName)
            || widget.Sizes.Count == 0
            || widget.Sizes.Any(size => size is not ("small" or "medium" or "wide" or "large"))
            || !widget.Sizes.Contains(widget.DefaultSize, StringComparer.Ordinal))
        {
            Fail("package.widget_invalid", "Widget declaration is invalid.");
        }
        EnsureDistinct(widget.Sizes, "package.widget_size_duplicate");
    }

    private static void ValidateCapability(PackageCapabilityManifest capability)
    {
        ValidatePermission(capability.Name);
        if (capability.Direction is not ("provides" or "requires"))
        {
            Fail("package.capability_direction_invalid", "Capability direction is invalid.");
        }
        ValidateSemanticVersion(capability.ContractVersion);
    }

    private static void ValidateMigration(PackageMigrationManifest migration)
    {
        ValidateIdentifier(migration.MigrationId, nameof(migration.MigrationId));
        if (migration.Resource is not ("core-registration" or "package-database" or "runtime-data"))
        {
            Fail("package.migration_resource_invalid", "Migration resource is invalid.");
        }
        ValidateSha256(migration.Digest);
    }

    private static void ValidateFrontend(PackageFrontendManifest frontend)
    {
        if (string.IsNullOrWhiteSpace(frontend.ModulePath)
            || Path.IsPathRooted(frontend.ModulePath)
            || frontend.ModulePath.Split('/').Any(segment => segment is ".." or "." or ""))
        {
            Fail("package.frontend_path_invalid", "Frontend module path is invalid.");
        }
        ValidateSha256(frontend.Sha256);
        if (frontend.ExportedElements.Count == 0 || frontend.ExportedElements.Any(element => !CustomElementName().IsMatch(element)))
        {
            Fail("package.frontend_elements_invalid", "Frontend exported element names are invalid.");
        }
        EnsureDistinct(frontend.ExportedElements, "package.frontend_element_duplicate");
    }

    private static void ValidatePackageId(string value)
    {
        if (!PackageId().IsMatch(value))
        {
            Fail("package.id_invalid", "Package identifier must be a lowercase reverse-DNS name.");
        }
    }

    private static void ValidateStableKey(string value)
    {
        if (!StableKey().IsMatch(value))
        {
            Fail("package.stable_key_invalid", "Stable key is invalid.");
        }
    }

    private static void ValidatePermission(string value)
    {
        if (!PermissionName().IsMatch(value))
        {
            Fail("package.permission_invalid", "Permission or capability name is invalid.");
        }
    }

    private static void ValidateIdentifier(string value, string name)
    {
        if (!Identifier().IsMatch(value))
        {
            Fail("package.identifier_invalid", $"Package field '{name}' is invalid.");
        }
    }

    private static void ValidateResourceKey(string value, string name)
    {
        if (!ResourceKey().IsMatch(value))
        {
            Fail("package.resource_key_invalid", $"Resource key '{name}' is invalid.");
        }
    }

    private static void ValidateSemanticVersion(string value)
    {
        if (!SemanticVersion().IsMatch(value))
        {
            Fail("package.version_invalid", "Package version is not semantic version 2.0.");
        }
    }

    private static void ValidateSha256(string value)
    {
        if (!Sha256().IsMatch(value))
        {
            Fail("package.digest_invalid", "SHA-256 digest is invalid.");
        }
    }

    private static void EnsureDistinct(IEnumerable<string> values, string code)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (values.Any(value => !seen.Add(value)))
        {
            Fail(code, "A package manifest collection contains duplicate stable identities.");
        }
    }

    private static void Fail(string code, string message) => throw new PackageManifestException(code, message);

    [GeneratedRegex("^[a-z][a-z0-9]*(?:\\.[a-z][a-z0-9-]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageId();

    [GeneratedRegex("^[a-z][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex StableKey();

    [GeneratedRegex("^[a-z][a-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex PermissionName();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();

    [GeneratedRegex("^[a-z][a-z0-9_.-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex ResourceKey();

    [GeneratedRegex("^(0|[1-9]\\d*)\\.(0|[1-9]\\d*)\\.(0|[1-9]\\d*)(?:-[0-9A-Za-z.-]+)?(?:\\+[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex SemanticVersion();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256();

    [GeneratedRegex("^[a-z][a-z0-9]*(?:-[a-z0-9]+)+$", RegexOptions.CultureInvariant)]
    private static partial Regex CustomElementName();
}

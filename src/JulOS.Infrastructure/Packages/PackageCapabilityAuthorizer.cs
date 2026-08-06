using System.Text.Json;

using JulOS.Domain.Packages;
using JulOS.Infrastructure.Persistence.Core;

using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Packages;

/// <summary>One capability grant recovered from a verified installed package manifest.</summary>
/// <param name="PackageId">Authorized package identity.</param>
/// <param name="CapabilityName">Granted capability identity.</param>
/// <param name="ContractVersion">Signed required contract version.</param>
public sealed record PackageCapabilityGrant(
    string PackageId,
    string CapabilityName,
    string ContractVersion);

/// <summary>Authorizes package capability calls against durable lifecycle state and the signed manifest.</summary>
public sealed class PackageCapabilityAuthorizer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly CoreDbContext context;
    private readonly string packageRoot;

    /// <summary>Creates a package capability authorizer.</summary>
    /// <param name="context">Core persistence context.</param>
    /// <param name="packageRoot">Durable verified package root.</param>
    public PackageCapabilityAuthorizer(CoreDbContext context, string packageRoot)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        this.packageRoot = Path.GetFullPath(packageRoot);
    }

    /// <summary>Requires an enabled healthy package with a matching required capability declaration.</summary>
    /// <param name="packageId">Calling package identity.</param>
    /// <param name="capabilityName">Requested capability identity.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The signed capability grant.</returns>
    public async Task<PackageCapabilityGrant> AuthorizeAsync(
        string packageId,
        string capabilityName,
        CancellationToken cancellationToken = default)
    {
        ValidateValue(packageId, 128);
        ValidateValue(capabilityName, 128);

        var row = await this.context.PackageInstallations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.PackageId == packageId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw Failure("package.not_found", "Package is not installed.");

        if (row.State != PackageInstallationState.Enabled)
        {
            throw Failure(
                "package.capability_package_unavailable",
                "The package is not enabled.");
        }

        var metadata = await this.ReadMetadataAsync(packageId, cancellationToken).ConfigureAwait(false);
        if (!metadata.WorkerHealthy)
        {
            throw Failure(
                "package.capability_package_unavailable",
                "The package worker is not healthy.");
        }

        var capability = metadata.Manifest.Capabilities.SingleOrDefault(candidate =>
            string.Equals(candidate.Direction, "requires", StringComparison.Ordinal)
            && string.Equals(candidate.Name, capabilityName, StringComparison.Ordinal));
        if (capability is null)
        {
            throw Failure(
                "package.capability_not_granted",
                "The signed package manifest does not grant the requested capability.");
        }

        return new PackageCapabilityGrant(
            packageId,
            capability.Name,
            capability.ContractVersion);
    }

    private async Task<InstalledPackageMetadata> ReadMetadataAsync(
        string packageId,
        CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(Path.Combine(this.packageRoot, packageId, "state.json"));
        if (!path.StartsWith(this.packageRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw Failure("package.capability_value_invalid", "Package identity is invalid.");
        }

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                useAsync: true);
            return await JsonSerializer.DeserializeAsync<InstalledPackageMetadata>(
                    stream,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false)
                ?? throw Failure("package.metadata_invalid", "Package metadata is invalid.");
        }
        catch (PackageCapabilityAuthorizationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or JsonException)
        {
            throw Failure(
                "package.metadata_invalid",
                "Package metadata is unavailable or invalid.",
                exception);
        }
    }

    private static void ValidateValue(string value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value != value.Trim()
            || value.Length > maximumLength
            || value.Any(char.IsControl))
        {
            throw Failure(
                "package.capability_value_invalid",
                "Package capability value is invalid.");
        }
    }

    private static PackageCapabilityAuthorizationException Failure(
        string code,
        string message,
        Exception? innerException = null) =>
        new(code, message, innerException);
}

/// <summary>Stable caller-safe package capability authorization failure.</summary>
public sealed class PackageCapabilityAuthorizationException : Exception
{
    /// <summary>Creates a package capability authorization failure.</summary>
    /// <param name="code">Stable machine-readable failure code.</param>
    /// <param name="message">Caller-safe failure detail.</param>
    /// <param name="innerException">Optional server-side cause.</param>
    public PackageCapabilityAuthorizationException(
        string code,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        this.Code = code;
    }

    /// <summary>Gets the stable machine-readable failure code.</summary>
    public string Code { get; }
}

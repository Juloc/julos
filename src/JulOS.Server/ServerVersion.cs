using System.Reflection;

namespace JulOS.Server;

/// <summary>
/// The version this server was built from.
/// </summary>
/// <remarks>
/// The value originates in the repository <c>VERSION</c> file, which
/// <c>Directory.Build.props</c> turns into the assembly version at build time. Nothing
/// here restates a version number, so one file governs every build output.
/// </remarks>
internal static class ServerVersion
{
    /// <summary>The stable component name used in diagnostics and logs.</summary>
    internal const string ComponentName = "JulOS.Server";

    /// <summary>The semantic version, without the build metadata a deterministic build appends.</summary>
    internal static string Current { get; } = Read();

    private static string Read()
    {
        var informational = typeof(ServerVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            throw new InvalidOperationException(
                $"{ComponentName} has no informational version. Directory.Build.props derives it from the repository VERSION file.");
        }

        var buildMetadata = informational.IndexOf('+', StringComparison.Ordinal);

        return buildMetadata < 0 ? informational : informational[..buildMetadata];
    }
}

using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace JulOS.Agent;

internal sealed partial class AgentUpdatePolicy
{
    internal AgentUpdateDecision Validate(
        string currentVersion,
        string targetVersion,
        bool allowExplicitDowngrade,
        ReadOnlySpan<byte> artifact,
        string expectedSha256)
    {
        var current = Parse(currentVersion, nameof(currentVersion));
        var target = Parse(targetVersion, nameof(targetVersion));
        if (target.CompareTo(current) < 0 && !allowExplicitDowngrade)
        {
            throw new AgentUpdateException(
                "agent.update.downgrade_requires_approval",
                "An Agent downgrade requires explicit approval.");
        }
        if (target.CompareTo(current) == 0)
        {
            throw new AgentUpdateException(
                "agent.update.version_unchanged",
                "The target Agent version is already installed.");
        }

        if (!Sha256Pattern().IsMatch(expectedSha256))
        {
            throw new AgentUpdateException("agent.update.digest_invalid", "The Agent update digest is invalid.");
        }
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(artifact, digest);
        var expected = Convert.FromHexString(expectedSha256);
        if (!CryptographicOperations.FixedTimeEquals(digest, expected))
        {
            throw new AgentUpdateException("agent.update.digest_mismatch", "The Agent update digest does not match.");
        }

        return new AgentUpdateDecision(
            currentVersion,
            targetVersion,
            target.CompareTo(current) < 0,
            Convert.ToHexString(digest).ToLowerInvariant());
    }

    private static Version Parse(string value, string parameterName)
    {
        if (!SemanticVersionPattern().IsMatch(value))
        {
            throw new ArgumentException("Agent version must use semantic version core syntax.", parameterName);
        }
        return Version.Parse(value);
    }

    [GeneratedRegex("^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant)]
    private static partial Regex SemanticVersionPattern();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}

internal sealed record AgentUpdateDecision(
    string CurrentVersion,
    string TargetVersion,
    bool IsDowngrade,
    string ArtifactDigest);

internal sealed class AgentUpdateException : Exception
{
    internal AgentUpdateException(string code, string message)
        : base(message)
    {
        this.Code = code;
    }

    internal string Code { get; }
}

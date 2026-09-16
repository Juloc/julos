namespace JulOS.Domain.Packages;

/// <summary>
/// How much is known about who produced an installed package.
/// </summary>
/// <remarks>
/// <para>
/// This is trust, not integrity. Every installed artifact has already been verified byte
/// for byte against its recorded digest regardless of this value; what differs is whether
/// the signature that covers those bytes was made by a key this installation trusts.
/// </para>
/// <para>
/// A key delivered beside a package never makes it <see cref="TrustedSigned"/>. That is
/// the rule in <c>docs/OFFICIAL_PACKAGE_RELEASES.md</c>: trust comes from configuration,
/// never from the artifact describing itself.
/// </para>
/// </remarks>
public enum PackageSignatureState
{
    /// <summary>Signed by a key this installation is configured to trust.</summary>
    TrustedSigned = 1,

    /// <summary>
    /// Cryptographically valid, but signed by a key this installation does not trust.
    /// </summary>
    UnknownSigned = 2,

    /// <summary>
    /// Carries no publisher signature at all.
    /// </summary>
    /// <remarks>Named NotSigned rather than Unsigned so it is not read as a numeric type.</remarks>
    NotSigned = 3,
}

/// <summary>Whether a package's frontend and worker run on the isolated path.</summary>
public static class PackageIsolation
{
    /// <summary>
    /// Whether code from a package with this signature state must be isolated.
    /// </summary>
    /// <remarks>
    /// Anything that is not trusted runs isolated. The check is written as "trusted is the
    /// exception" rather than as a list of untrusted states, so a signature state added
    /// later is isolated by default instead of silently inheriting full access.
    /// </remarks>
    public static bool IsRequiredFor(PackageSignatureState state) =>
        state != PackageSignatureState.TrustedSigned;
}

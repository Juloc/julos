namespace JulOS.Server.Secrets;

/// <summary>Validated non-secret configuration for encrypted secret references.</summary>
internal sealed record SecretReferenceOptions(
    string ActiveKeyId,
    string KeyRingPath,
    TimeSpan LeaseLifetime)
{
    private const int DefaultLeaseLifetimeSeconds = 300;

    internal static SecretReferenceOptions Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var activeKeyId = configuration["Secrets:ActiveKeyId"];
        var keyRingPath = configuration["Secrets:KeyRingPath"];
        var leaseLifetimeSeconds = configuration.GetValue(
            "Secrets:LeaseLifetimeSeconds",
            DefaultLeaseLifetimeSeconds);

        if (string.IsNullOrWhiteSpace(activeKeyId))
        {
            throw new InvalidOperationException("Secrets:ActiveKeyId is required.");
        }

        if (string.IsNullOrWhiteSpace(keyRingPath))
        {
            throw new InvalidOperationException("Secrets:KeyRingPath is required.");
        }

        return new SecretReferenceOptions(
            activeKeyId,
            keyRingPath,
            TimeSpan.FromSeconds(leaseLifetimeSeconds));
    }
}

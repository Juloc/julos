using JulOS.Application.Secrets;

using Microsoft.Extensions.DependencyInjection;

namespace JulOS.Infrastructure.Secrets;

/// <summary>Registers encrypted secret references and operation-scoped leasing.</summary>
public static class SecretReferenceServiceCollectionExtensions
{
    /// <summary>Adds the external key ring, AES-GCM protection and Core-backed service.</summary>
    public static IServiceCollection AddJulOsSecretReferences(
        this IServiceCollection services,
        string activeKeyId,
        string keyRingPath,
        TimeSpan leaseLifetime)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (leaseLifetime < TimeSpan.FromSeconds(30) || leaseLifetime > TimeSpan.FromMinutes(15))
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaseLifetime),
                leaseLifetime,
                "A secret lease must last between 30 seconds and 15 minutes.");
        }

        var keyRing = SecretEncryptionKeyRing.Load(activeKeyId, keyRingPath);
        services.AddSingleton(_ => keyRing);
        services.AddSingleton<ISecretProtector, AesGcmSecretProtector>();
        services.AddSingleton(new SecretLeasePolicy(leaseLifetime));
        services.AddScoped<PostgresSecretReferenceService>();
        services.AddScoped<ISecretReferenceService>(provider =>
            provider.GetRequiredService<PostgresSecretReferenceService>());
        services.AddScoped<ISecretLeaseService>(provider =>
            provider.GetRequiredService<PostgresSecretReferenceService>());

        return services;
    }
}

internal sealed record SecretLeasePolicy(TimeSpan Lifetime);

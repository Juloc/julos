namespace JulOS.Contracts.Remote;

/// <summary>Credential operations exposed through the existing Remote session capability.</summary>
public static class RemoteCredentialCapabilityContract
{
    /// <summary>Creates one package-owned encrypted credential reference.</summary>
    public const string CreateOperation = "credential.create";

    /// <summary>Rotates one existing package-owned credential reference.</summary>
    public const string RotateOperation = "credential.rotate";

    /// <summary>Deletes one package-owned credential reference.</summary>
    public const string DeleteOperation = "credential.delete";
}

/// <summary>Creates one encrypted Remote credential from provider-compatible JSON.</summary>
public sealed record CreateRemoteCredentialRequest(string SecretValue);

/// <summary>Rotates one encrypted Remote credential without exposing its stored value.</summary>
public sealed record RotateRemoteCredentialRequest(Guid SecretReferenceId, string SecretValue);

/// <summary>Deletes one encrypted Remote credential.</summary>
public sealed record DeleteRemoteCredentialRequest(Guid SecretReferenceId);

/// <summary>Opaque reference returned to the Remote package frontend.</summary>
public sealed record RemoteCredentialReferenceResponse(Guid SecretReferenceId);

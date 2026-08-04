using JulOS.Contracts.Remote;
using JulOS.Infrastructure.Persistence.Core;

using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Remote;

/// <summary>Authorizes one browser display connection against durable session state.</summary>
public sealed class PostgresRemoteDisplayAuthorizationService
{
    private readonly CoreDbContext context;
    private readonly RemoteDisplayGateway gateway;
    private readonly TimeProvider timeProvider;

    /// <summary>Creates the PostgreSQL-backed display authorization service.</summary>
    public PostgresRemoteDisplayAuthorizationService(
        CoreDbContext context,
        RemoteDisplayGateway gateway,
        TimeProvider timeProvider)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>Returns the hidden provider URI only when the current grant is exact and active.</summary>
    public async Task<Uri> AuthorizeAsync(
        Guid ownerUserId,
        Guid sessionId,
        long revision,
        long expires,
        string? token,
        CancellationToken cancellationToken = default)
    {
        if (ownerUserId == Guid.Empty || sessionId == Guid.Empty || revision < 1)
        {
            throw new RemoteDisplayAuthorizationException(RemoteDisplayAuthorizationFailure.Unauthorized);
        }

        var row = await this.context.RemoteSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == sessionId
                    && candidate.OwnerUserId == ownerUserId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new RemoteDisplayAuthorizationException(RemoteDisplayAuthorizationFailure.Unauthorized);
        if (!string.Equals(row.State, RemoteSessionStates.Connected, StringComparison.Ordinal)
            || row.RuntimeId is null
            || row.DisplayKind is null
            || row.DisplayContractVersion is null
            || row.DisplayEndpoint is null
            || row.DisplayExpiresAtUtc is null)
        {
            throw new RemoteDisplayAuthorizationException(RemoteDisplayAuthorizationFailure.Unavailable);
        }
        if (row.Revision != revision
            || !string.Equals(row.DisplayKind, "websocket", StringComparison.Ordinal)
            || !string.Equals(row.DisplayContractVersion, "1.0.0", StringComparison.Ordinal)
            || row.DisplayExpiresAtUtc.Value.ToUnixTimeSeconds() != expires)
        {
            throw new RemoteDisplayAuthorizationException(RemoteDisplayAuthorizationFailure.Stale);
        }
        if (row.DisplayExpiresAtUtc <= this.timeProvider.GetUtcNow())
        {
            throw new RemoteDisplayAuthorizationException(RemoteDisplayAuthorizationFailure.Expired);
        }
        if (!this.gateway.Authenticate(
                row.Id,
                row.OwnerUserId,
                row.CallerPackageId,
                row.RuntimeId,
                row.Revision,
                expires,
                token))
        {
            throw new RemoteDisplayAuthorizationException(RemoteDisplayAuthorizationFailure.Unauthorized);
        }

        return this.gateway.ProviderEndpoint(row.RuntimeId);
    }
}

/// <summary>Stable display authorization refusal categories.</summary>
public enum RemoteDisplayAuthorizationFailure
{
    /// <summary>The user or grant token is invalid.</summary>
    Unauthorized,

    /// <summary>The session has no active display transport.</summary>
    Unavailable,

    /// <summary>The grant does not match the current session revision.</summary>
    Stale,

    /// <summary>The display grant has expired.</summary>
    Expired,
}

/// <summary>Caller-safe display authorization failure.</summary>
public sealed class RemoteDisplayAuthorizationException : Exception
{
    /// <summary>Creates a display authorization refusal.</summary>
    public RemoteDisplayAuthorizationException(RemoteDisplayAuthorizationFailure failure)
        : base(failure switch
        {
            RemoteDisplayAuthorizationFailure.Unauthorized => "Remote display authorization failed.",
            RemoteDisplayAuthorizationFailure.Unavailable => "The Remote display is unavailable.",
            RemoteDisplayAuthorizationFailure.Stale => "The Remote display grant is stale.",
            RemoteDisplayAuthorizationFailure.Expired => "The Remote display grant expired.",
            _ => "Remote display authorization failed.",
        })
    {
        this.Failure = failure;
    }

    /// <summary>Gets the stable refusal category.</summary>
    public RemoteDisplayAuthorizationFailure Failure { get; }
}

using System.Data;

using JulOS.Application.Authentication;
using JulOS.Infrastructure.Persistence.Core;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Authentication;

/// <summary>
/// Creates the one permitted initial administrator while holding the database setup lock.
/// </summary>
public sealed class InitialAdministratorProvisioner
{
    private const int SetupRowId = 1;

    private readonly CoreDbContext context;
    private readonly UserManager<LocalUser> userManager;
    private readonly RoleManager<LocalRole> roleManager;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the provisioner.</summary>
    public InitialAdministratorProvisioner(
        CoreDbContext context,
        UserManager<LocalUser> userManager,
        RoleManager<LocalRole> roleManager,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(userManager);
        ArgumentNullException.ThrowIfNull(roleManager);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.context = context;
        this.userManager = userManager;
        this.roleManager = roleManager;
        this.timeProvider = timeProvider;
    }

    /// <summary>Returns whether the one-time setup has not completed.</summary>
    public async Task<bool> IsSetupRequiredAsync(CancellationToken cancellationToken = default)
    {
        return !await this.context.AuthenticationSetup
            .AsNoTracking()
            .AnyAsync(
                row => row.Id == SetupRowId && row.CompletedAtUtc != null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Creates the first administrator exactly once.</summary>
    public async Task<LocalUser> CreateAsync(
        string userName,
        string displayName,
        string password,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(userName, displayName, password);

        await using var transaction = await this.context.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);

        var setup = await this.context.AuthenticationSetup
            .FromSqlRaw(
                """
                SELECT id, administrator_user_id, completed_at_utc
                FROM core.authentication_setup
                WHERE id = 1
                FOR UPDATE
                """)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        if (setup.CompletedAtUtc is not null)
        {
            throw new AuthenticationFailureException(
                AuthenticationFailureReason.SetupAlreadyCompleted);
        }

        var administratorRole = await this.roleManager
            .FindByNameAsync(LocalIdentityNames.AdministratorRole)
            .ConfigureAwait(false);

        if (administratorRole is null)
        {
            administratorRole = new LocalRole
            {
                Id = Guid.CreateVersion7(this.timeProvider.GetUtcNow()),
                Name = LocalIdentityNames.AdministratorRole,
                IsSystemRole = true,
                Revision = 1,
            };

            EnsureIdentitySucceeded(
                await this.roleManager.CreateAsync(administratorRole).ConfigureAwait(false),
                "The initial administrator role could not be created.");
        }

        var now = this.timeProvider.GetUtcNow();
        var user = new LocalUser
        {
            Id = Guid.CreateVersion7(now),
            UserName = userName,
            DisplayName = displayName,
            PreferredLanguage = "en",
            TimeZone = "UTC",
            Theme = "system",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Revision = 1,
        };

        var createResult = await this.userManager
            .CreateAsync(user, password)
            .ConfigureAwait(false);

        if (!createResult.Succeeded)
        {
            throw new AuthenticationFailureException(
                AuthenticationFailureReason.InvalidSetupRequest);
        }

        EnsureIdentitySucceeded(
            await this.userManager
                .AddToRoleAsync(user, LocalIdentityNames.AdministratorRole)
                .ConfigureAwait(false),
            "The initial administrator role could not be assigned.");

        setup.AdministratorUserId = user.Id;
        setup.CompletedAtUtc = now;

        await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return user;
    }

    private static void ValidateRequest(
        string userName,
        string displayName,
        string password)
    {
        if (string.IsNullOrWhiteSpace(userName)
            || userName.Length is < 3 or > 128
            || !string.Equals(userName, userName.Trim(), StringComparison.Ordinal)
            || userName.Any(character => !(char.IsAsciiLetterOrDigit(character)
                || character is '.' or '-' or '_' or '@' or '+')))
        {
            throw new AuthenticationFailureException(
                AuthenticationFailureReason.InvalidSetupRequest);
        }

        if (string.IsNullOrWhiteSpace(displayName)
            || displayName.Length > 256
            || !string.Equals(displayName, displayName.Trim(), StringComparison.Ordinal))
        {
            throw new AuthenticationFailureException(
                AuthenticationFailureReason.InvalidSetupRequest);
        }

        if (string.IsNullOrEmpty(password) || password.Length is < 12 or > 1024)
        {
            throw new AuthenticationFailureException(
                AuthenticationFailureReason.InvalidSetupRequest);
        }
    }

    private static void EnsureIdentitySucceeded(
        IdentityResult result,
        string failureMessage)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(failureMessage);
        }
    }
}

using JulOS.Infrastructure.Authentication;
using JulOS.Infrastructure.Persistence.Core;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JulOS.Infrastructure.Authorization;

/// <summary>
/// Grants the administrator role any platform permission that was added to the
/// catalog after the one-time setup completed.
/// </summary>
/// <remarks>
/// <see cref="InitialAdministratorProvisioner"/> seeds administrator grants once,
/// while the first administrator is created. An installation that later upgrades
/// into a newer permission (for example the web-app proxy permission, or a package
/// lifecycle permission) would otherwise keep an administrator role frozen at the
/// permission set of its original version, and every endpoint guarded by the new
/// permission would answer with 403 even for the administrator. Running this on
/// every startup keeps the administrator role aligned with the current catalog;
/// <see cref="SystemAuthorizationGrantSeeder"/> only adds missing grants, so the
/// reconciliation is idempotent.
/// </remarks>
public static class SystemAuthorizationReconciler
{
    private const int SetupRowId = 1;

    /// <summary>Aligns the administrator role with the current permission catalog.</summary>
    public static async Task ReconcileAdministratorPermissionsAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CoreDbContext>();

        var setup = await context.AuthenticationSetup
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == SetupRowId, cancellationToken)
            .ConfigureAwait(false);
        if (setup?.CompletedAtUtc is null
            || setup.AdministratorUserId is not Guid administratorUserId)
        {
            // Setup has not completed yet; the initial provisioner still owns the
            // first seed, so there is no administrator role to reconcile.
            return;
        }

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<LocalRole>>();
        var administratorRole = await roleManager
            .FindByNameAsync(LocalIdentityNames.AdministratorRole)
            .ConfigureAwait(false);
        if (administratorRole is null)
        {
            return;
        }

        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        await SystemAuthorizationGrantSeeder.EnsureAdministratorPermissionsAsync(
            context,
            administratorRole.Id,
            administratorUserId,
            timeProvider,
            cancellationToken).ConfigureAwait(false);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

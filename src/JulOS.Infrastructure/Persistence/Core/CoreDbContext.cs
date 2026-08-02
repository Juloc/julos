namespace JulOS.Infrastructure.Persistence.Core;

using JulOS.Application.Concurrency;
using JulOS.Infrastructure.Authentication;

using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

/// <summary>
/// Persistence boundary for JulOS-owned Core state.
/// </summary>
public sealed class CoreDbContext : IdentityDbContext<LocalUser, LocalRole, Guid>
{
    private readonly TimeProvider timeProvider;
    /// <summary>
    /// Gets the PostgreSQL schema owned by the JulOS core platform.
    /// </summary>
    public const string SchemaName = "core";

    /// <summary>
    /// Gets the schema-qualified Entity Framework migration-history table name.
    /// </summary>
    public const string MigrationHistoryTableName = "__ef_migrations_history";

    /// <summary>
    /// Initializes a new instance of the <see cref="CoreDbContext"/> class.
    /// </summary>
    /// <param name="options">The configured database-context options.</param>
    /// <param name="timeProvider">The clock used for identity timestamps and revisions.</param>
    public CoreDbContext(
        DbContextOptions<CoreDbContext> options,
        TimeProvider? timeProvider = null)
        : base(options)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    internal DbSet<AuthenticationSetupRow> AuthenticationSetup => this.Set<AuthenticationSetupRow>();
    internal DbSet<PermissionAssignmentRow> PermissionAssignments => this.Set<PermissionAssignmentRow>();
    internal DbSet<PackageInstallationRow> PackageInstallations => this.Set<PackageInstallationRow>();
    internal DbSet<ApplicationDefinitionRow> ApplicationDefinitions => this.Set<ApplicationDefinitionRow>();
    internal DbSet<LaunchTargetRow> LaunchTargets => this.Set<LaunchTargetRow>();
    internal DbSet<DesktopLayoutRow> DesktopLayouts => this.Set<DesktopLayoutRow>();
    internal DbSet<DesktopWindowRow> DesktopWindows => this.Set<DesktopWindowRow>();
    internal DbSet<WidgetPlacementRow> WidgetPlacements => this.Set<WidgetPlacementRow>();
    internal DbSet<SessionReferenceRow> Sessions => this.Set<SessionReferenceRow>();
    internal DbSet<AgentRow> Agents => this.Set<AgentRow>();
    internal DbSet<AgentCapabilityRow> AgentCapabilities => this.Set<AgentCapabilityRow>();
    internal DbSet<ProblemRow> Problems => this.Set<ProblemRow>();
    internal DbSet<NotificationRow> Notifications => this.Set<NotificationRow>();
    internal DbSet<AuditEventRow> AuditEvents => this.Set<AuditEventRow>();

    /// <inheritdoc />
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        try
        {
            this.PrepareIdentityRevisions();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw CreateConcurrencyConflict(exception);
        }
    }

    /// <inheritdoc />
    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        try
        {
            this.PrepareIdentityRevisions();
            return await base
                .SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw await CreateConcurrencyConflictAsync(exception, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        base.OnModelCreating(builder);
        CoreModelConfiguration.Configure(builder);
    }

    private void PrepareIdentityRevisions()
    {
        var now = this.timeProvider.GetUtcNow();

        foreach (var entry in this.ChangeTracker.Entries<LocalUser>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.Revision = 1;
                entry.Entity.CreatedAtUtc = entry.Entity.CreatedAtUtc == default ? now : entry.Entity.CreatedAtUtc;
                entry.Entity.UpdatedAtUtc = entry.Entity.UpdatedAtUtc == default ? now : entry.Entity.UpdatedAtUtc;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.Revision = checked(entry.OriginalValues.GetValue<int>(nameof(LocalUser.Revision)) + 1);
                entry.Entity.UpdatedAtUtc = now;
            }
        }

        foreach (var entry in this.ChangeTracker.Entries<LocalRole>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.Revision = 1;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.Revision = checked(entry.OriginalValues.GetValue<int>(nameof(LocalRole.Revision)) + 1);
            }
        }
    }

    private static ConcurrencyConflictException CreateConcurrencyConflict(
        DbUpdateConcurrencyException exception)
    {
        var currentRevision = exception.Entries
            .Select(ReadCurrentRevision)
            .FirstOrDefault(revision => revision is not null);

        return new ConcurrencyConflictException(currentRevision, exception);
    }

    private static async Task<ConcurrencyConflictException> CreateConcurrencyConflictAsync(
        DbUpdateConcurrencyException exception,
        CancellationToken cancellationToken)
    {
        foreach (var entry in exception.Entries)
        {
            var currentRevision = await ReadCurrentRevisionAsync(entry, cancellationToken).ConfigureAwait(false);
            if (currentRevision is not null)
            {
                return new ConcurrencyConflictException(currentRevision, exception);
            }
        }

        return new ConcurrencyConflictException(currentRevision: null, exception);
    }

    private static int? ReadCurrentRevision(EntityEntry entry)
    {
        var revisionProperty = entry.Metadata.FindProperty(nameof(PackageInstallationRow.Revision));
        var databaseValues = entry.GetDatabaseValues();

        return revisionProperty is null || databaseValues is null
            ? null
            : databaseValues.GetValue<int>(revisionProperty.Name);
    }

    private static async Task<int?> ReadCurrentRevisionAsync(
        EntityEntry entry,
        CancellationToken cancellationToken)
    {
        var revisionProperty = entry.Metadata.FindProperty(nameof(PackageInstallationRow.Revision));
        var databaseValues = await entry.GetDatabaseValuesAsync(cancellationToken).ConfigureAwait(false);

        return revisionProperty is null || databaseValues is null
            ? null
            : databaseValues.GetValue<int>(revisionProperty.Name);
    }
}

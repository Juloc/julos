namespace JulOS.Infrastructure.Persistence.Core;

using JulOS.Application.Concurrency;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

/// <summary>
/// Persistence boundary for JulOS-owned Core state.
/// </summary>
public sealed class CoreDbContext : DbContext
{
    public const string SchemaName = "core";
    public const string MigrationHistoryTableName = "__ef_migrations_history";

    public CoreDbContext(DbContextOptions<CoreDbContext> options)
        : base(options)
    {
    }

    internal DbSet<UserRow> Users => this.Set<UserRow>();
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
            return await base
                .SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw await CreateConcurrencyConflictAsync(exception, cancellationToken).ConfigureAwait(false);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        CoreModelConfiguration.Configure(modelBuilder);
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
            : databaseValues.GetValue<int>(revisionProperty);
    }

    private static async Task<int?> ReadCurrentRevisionAsync(
        EntityEntry entry,
        CancellationToken cancellationToken)
    {
        var revisionProperty = entry.Metadata.FindProperty(nameof(PackageInstallationRow.Revision));
        var databaseValues = await entry.GetDatabaseValuesAsync(cancellationToken).ConfigureAwait(false);

        return revisionProperty is null || databaseValues is null
            ? null
            : databaseValues.GetValue<int>(revisionProperty);
    }
}

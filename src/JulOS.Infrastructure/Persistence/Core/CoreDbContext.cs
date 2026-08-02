using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Persistence.Core;

/// <summary>
/// The authoritative PostgreSQL control-plane context.
/// </summary>
/// <remarks>
/// Package-owned tables never belong to this context. Every package receives a separate
/// schema and restricted role through PKG-004.
/// </remarks>
public sealed class CoreDbContext : DbContext
{
    /// <summary>Creates the context from configured provider options.</summary>
    public CoreDbContext(DbContextOptions<CoreDbContext> options)
        : base(options)
    {
    }

    internal DbSet<PackageInstallationRow> PackageInstallations => this.Set<PackageInstallationRow>();

    internal DbSet<ApplicationDefinitionRow> ApplicationDefinitions => this.Set<ApplicationDefinitionRow>();

    internal DbSet<LaunchTargetRow> LaunchTargets => this.Set<LaunchTargetRow>();

    internal DbSet<DesktopLayoutRow> DesktopLayouts => this.Set<DesktopLayoutRow>();

    internal DbSet<SessionReferenceRow> SessionReferences => this.Set<SessionReferenceRow>();

    internal DbSet<AgentRow> Agents => this.Set<AgentRow>();

    internal DbSet<AgentCapabilityRow> AgentCapabilities => this.Set<AgentCapabilityRow>();

    internal DbSet<ProblemRow> Problems => this.Set<ProblemRow>();

    internal DbSet<NotificationRow> Notifications => this.Set<NotificationRow>();

    internal DbSet<AuditEventRow> AuditEvents => this.Set<AuditEventRow>();

    internal DbSet<PermissionAssignmentRow> PermissionAssignments => this.Set<PermissionAssignmentRow>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        CoreModelConfiguration.Configure(modelBuilder);
    }
}

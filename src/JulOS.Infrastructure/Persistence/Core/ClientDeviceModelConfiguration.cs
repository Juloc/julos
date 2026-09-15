using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JulOS.Infrastructure.Persistence.Core;

/// <summary>Maps the MOB-003 client-device tables into the Core schema.</summary>
internal static class ClientDeviceModelConfiguration
{
    private const string Schema = CoreDbContext.SchemaName;

    internal static void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<ClientDeviceRow>(ConfigureClientDevices);
        modelBuilder.Entity<DeviceWorkspacePreferenceRow>(ConfigurePreferences);
        modelBuilder.Entity<ApplicationExecutionPreferenceRow>(ConfigureExecutionPreferences);
    }

    private static void ConfigureClientDevices(EntityTypeBuilder<ClientDeviceRow> entity)
    {
        entity.ToTable("client_devices", Schema, table =>
        {
            table.HasCheckConstraint("ck_client_devices_revision", "revision >= 1");
            table.HasCheckConstraint(
                "ck_client_devices_workspace",
                "last_detected_workspace_class IN ('Phone', 'Tablet', 'DesktopSingle')");
            // A device is never pinned to multi-display: that is entered through the
            // Multi-Display controller, not through a stored preference.
            table.HasCheckConstraint(
                "ck_client_devices_override",
                "workspace_class_override IS NULL "
                + "OR workspace_class_override IN ('Phone', 'Tablet', 'DesktopSingle')");
            table.HasCheckConstraint("ck_client_devices_seen", "last_seen_at_utc >= created_at_utc");
        });

        entity.HasKey(row => row.Id).HasName("pk_client_devices");
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(row => row.OwnerUserId).HasColumnName("owner_user_id");
        entity.Property(row => row.ClientInstanceKeyHash)
            .HasColumnName("client_instance_key_hash")
            .HasMaxLength(64)
            .IsRequired();
        entity.Property(row => row.DisplayName).HasColumnName("display_name").HasMaxLength(128).IsRequired();
        entity.Property(row => row.LastDetectedWorkspaceClass)
            .HasColumnName("last_detected_workspace_class")
            .HasConversion<string>()
            .HasMaxLength(16);
        entity.Property(row => row.WorkspaceClassOverride)
            .HasColumnName("workspace_class_override")
            .HasConversion<string>()
            .HasMaxLength(16);
        entity.Property(row => row.CreatedAtUtc).HasColumnName("created_at_utc");
        entity.Property(row => row.LastSeenAtUtc).HasColumnName("last_seen_at_utc");
        entity.Property(row => row.Revision).HasColumnName("revision").IsConcurrencyToken();

        // The hash is globally unique so a key can resolve to at most one device, and the
        // lookup is always additionally filtered by the authenticated user.
        entity.HasIndex(row => row.ClientInstanceKeyHash)
            .IsUnique()
            .HasDatabaseName("ux_client_devices_key_hash");
        entity.HasIndex(row => row.OwnerUserId).HasDatabaseName("ix_client_devices_owner");

        entity.HasMany(row => row.Preferences)
            .WithOne()
            .HasForeignKey(row => row.ClientDeviceId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_device_workspace_preferences_device");
    }

    private static void ConfigurePreferences(EntityTypeBuilder<DeviceWorkspacePreferenceRow> entity)
    {
        entity.ToTable("device_workspace_preferences", Schema, table =>
        {
            table.HasCheckConstraint(
                "ck_device_workspace_preferences_scope",
                "layout_scope IN ('Shared', 'Device')");
            table.HasCheckConstraint(
                "ck_device_workspace_preferences_restore",
                "restore_mode IN ('Resume', 'Fresh')");
        });

        // One preference per device and workspace class, which is the natural key.
        entity.HasKey(row => new { row.ClientDeviceId, row.WorkspaceClass })
            .HasName("pk_device_workspace_preferences");
        entity.Property(row => row.ClientDeviceId).HasColumnName("client_device_id");
        entity.Property(row => row.WorkspaceClass)
            .HasColumnName("workspace_class")
            .HasConversion<string>()
            .HasMaxLength(16);
        entity.Property(row => row.LayoutScope)
            .HasColumnName("layout_scope")
            .HasConversion<string>()
            .HasMaxLength(16);
        entity.Property(row => row.RestoreMode)
            .HasColumnName("restore_mode")
            .HasConversion<string>()
            .HasMaxLength(16);
    }

    private static void ConfigureExecutionPreferences(EntityTypeBuilder<ApplicationExecutionPreferenceRow> entity)
    {
        entity.ToTable("application_execution_preferences", Schema, table =>
        {
            table.HasCheckConstraint("ck_application_execution_preferences_revision", "revision >= 1");
            table.HasCheckConstraint(
                "ck_application_execution_preferences_mode",
                "background_mode IN ('Suspend', 'KeepSurfaceActive')");
        });

        entity.HasKey(row => row.Id).HasName("pk_application_execution_preferences");
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(row => row.OwnerUserId).HasColumnName("owner_user_id");
        entity.Property(row => row.ApplicationDefinitionId).HasColumnName("application_definition_id");
        entity.Property(row => row.WorkspaceClass)
            .HasColumnName("workspace_class")
            .HasConversion<string>()
            .HasMaxLength(16);
        entity.Property(row => row.ClientDeviceId).HasColumnName("client_device_id");
        entity.Property(row => row.BackgroundMode)
            .HasColumnName("background_mode")
            .HasConversion<string>()
            .HasMaxLength(24);
        entity.Property(row => row.Revision).HasColumnName("revision").IsConcurrencyToken();

        // The shared preference (no device) and each device-scoped preference are distinct
        // rows, so they need two partial unique indexes rather than one nullable key.
        entity.HasIndex(row => new { row.OwnerUserId, row.ApplicationDefinitionId, row.WorkspaceClass })
            .IsUnique()
            .HasFilter("client_device_id IS NULL")
            .HasDatabaseName("ux_application_execution_preferences_shared");
        entity.HasIndex(row => new
        {
            row.OwnerUserId,
            row.ApplicationDefinitionId,
            row.WorkspaceClass,
            row.ClientDeviceId,
        })
            .IsUnique()
            .HasFilter("client_device_id IS NOT NULL")
            .HasDatabaseName("ux_application_execution_preferences_device");

        entity.HasOne<ClientDeviceRow>()
            .WithMany()
            .HasForeignKey(row => row.ClientDeviceId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_application_execution_preferences_device");
    }
}

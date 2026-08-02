using JulOS.Domain.Agents;
using JulOS.Domain.Applications;
using JulOS.Domain.Layouts;
using JulOS.Domain.Observability;
using JulOS.Domain.Packages;
using JulOS.Domain.Permissions;
using JulOS.Domain.Primitives;
using JulOS.Domain.Sessions;
using JulOS.Infrastructure.Authentication;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JulOS.Infrastructure.Persistence.Core;

internal static class CoreModelConfiguration
{
    internal const string Schema = "core";

    internal static void Configure(ModelBuilder modelBuilder)
    {
        ConfigureIdentity(modelBuilder);
        ConfigureAuthenticationSetup(modelBuilder.Entity<AuthenticationSetupRow>());
        ConfigurePackageInstallations(modelBuilder.Entity<PackageInstallationRow>());
        ConfigureApplications(modelBuilder.Entity<ApplicationDefinitionRow>());
        ConfigureApplicationViewports(modelBuilder.Entity<ApplicationViewportRow>());
        ConfigureLaunchTargets(modelBuilder.Entity<LaunchTargetRow>());
        ConfigureLayouts(modelBuilder.Entity<DesktopLayoutRow>());
        ConfigureWindows(modelBuilder.Entity<DesktopWindowRow>());
        ConfigureWidgets(modelBuilder.Entity<WidgetPlacementRow>());
        ConfigureSessions(modelBuilder.Entity<SessionReferenceRow>());
        ConfigureAgents(modelBuilder.Entity<AgentRow>());
        ConfigureAgentCapabilities(modelBuilder.Entity<AgentCapabilityRow>());
        ConfigureProblems(modelBuilder.Entity<ProblemRow>());
        ConfigureNotifications(modelBuilder.Entity<NotificationRow>());
        ConfigureAuditEvents(modelBuilder.Entity<AuditEventRow>());
        ConfigurePermissionAssignments(modelBuilder.Entity<PermissionAssignmentRow>());
        OperationModelConfiguration.Configure(modelBuilder);
    }

    private static void ConfigureIdentity(ModelBuilder modelBuilder)
    {
        var users = modelBuilder.Entity<LocalUser>();
        users.ToTable("users", Schema, table =>
        {
            table.HasCheckConstraint("ck_users_revision", "revision >= 1");
            table.HasCheckConstraint("ck_users_language", "preferred_language IN ('en', 'de')");
            table.HasCheckConstraint("ck_users_theme", "theme IN ('system', 'light', 'dark')");
            table.HasCheckConstraint("ck_users_motion", "motion IN ('enabled', 'reduced')");
        });
        users.Property(user => user.Id).HasColumnName("id").ValueGeneratedNever();
        users.Property(user => user.UserName).HasColumnName("user_name").HasMaxLength(128);
        users.Property(user => user.NormalizedUserName).HasColumnName("normalized_user_name").HasMaxLength(128);
        users.Property(user => user.Email).HasColumnName("email").HasMaxLength(256);
        users.Property(user => user.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(256);
        users.Property(user => user.EmailConfirmed).HasColumnName("email_confirmed");
        users.Property(user => user.PasswordHash).HasColumnName("password_hash");
        users.Property(user => user.SecurityStamp).HasColumnName("security_stamp");
        users.Property(user => user.ConcurrencyStamp).HasColumnName("concurrency_stamp").IsConcurrencyToken();
        users.Property(user => user.PhoneNumber).HasColumnName("phone_number").HasMaxLength(64);
        users.Property(user => user.PhoneNumberConfirmed).HasColumnName("phone_number_confirmed");
        users.Property(user => user.TwoFactorEnabled).HasColumnName("two_factor_enabled");
        users.Property(user => user.LockoutEnd).HasColumnName("lockout_end_utc");
        users.Property(user => user.LockoutEnabled).HasColumnName("lockout_enabled");
        users.Property(user => user.AccessFailedCount).HasColumnName("access_failed_count");
        users.Property(user => user.DisplayName).HasColumnName("display_name").HasMaxLength(256).IsRequired();
        users.Property(user => user.PreferredLanguage).HasColumnName("preferred_language").HasMaxLength(32).IsRequired();
        users.Property(user => user.TimeZone).HasColumnName("time_zone").HasMaxLength(128).IsRequired();
        users.Property(user => user.Theme).HasColumnName("theme").HasMaxLength(16).IsRequired();
        users.Property(user => user.Motion).HasColumnName("motion").HasMaxLength(16).IsRequired();
        users.Property(user => user.CreatedAtUtc).HasColumnName("created_at_utc");
        users.Property(user => user.UpdatedAtUtc).HasColumnName("updated_at_utc");
        users.Property(user => user.Revision).HasColumnName("revision").IsConcurrencyToken();
        users.HasIndex(user => user.NormalizedUserName)
            .IsUnique()
            .HasDatabaseName("ux_users_normalized_user_name");
        users.HasIndex(user => user.NormalizedEmail)
            .HasDatabaseName("ix_users_normalized_email");

        var roles = modelBuilder.Entity<LocalRole>();
        roles.ToTable("roles", Schema, table =>
        {
            table.HasCheckConstraint("ck_roles_revision", "revision >= 1");
        });
        roles.Property(role => role.Id).HasColumnName("id").ValueGeneratedNever();
        roles.Property(role => role.Name).HasColumnName("name").HasMaxLength(128);
        roles.Property(role => role.NormalizedName).HasColumnName("normalized_name").HasMaxLength(128);
        roles.Property(role => role.ConcurrencyStamp).HasColumnName("concurrency_stamp").IsConcurrencyToken();
        roles.Property(role => role.Description).HasColumnName("description").HasMaxLength(512).IsRequired();
        roles.Property(role => role.IsSystemRole).HasColumnName("is_system_role");
        roles.Property(role => role.Revision).HasColumnName("revision").IsConcurrencyToken();
        roles.HasIndex(role => role.NormalizedName)
            .IsUnique()
            .HasDatabaseName("ux_roles_normalized_name");

        var userRoles = modelBuilder.Entity<IdentityUserRole<Guid>>();
        userRoles.ToTable("user_roles", Schema);
        userRoles.Property(item => item.UserId).HasColumnName("user_id");
        userRoles.Property(item => item.RoleId).HasColumnName("role_id");

        var userClaims = modelBuilder.Entity<IdentityUserClaim<Guid>>();
        userClaims.ToTable("user_claims", Schema);
        userClaims.Property(item => item.Id).HasColumnName("id");
        userClaims.Property(item => item.UserId).HasColumnName("user_id");
        userClaims.Property(item => item.ClaimType).HasColumnName("claim_type");
        userClaims.Property(item => item.ClaimValue).HasColumnName("claim_value");

        var userLogins = modelBuilder.Entity<IdentityUserLogin<Guid>>();
        userLogins.ToTable("user_logins", Schema);
        userLogins.Property(item => item.LoginProvider).HasColumnName("login_provider").HasMaxLength(128);
        userLogins.Property(item => item.ProviderKey).HasColumnName("provider_key").HasMaxLength(256);
        userLogins.Property(item => item.ProviderDisplayName).HasColumnName("provider_display_name").HasMaxLength(256);
        userLogins.Property(item => item.UserId).HasColumnName("user_id");

        var userTokens = modelBuilder.Entity<IdentityUserToken<Guid>>();
        userTokens.ToTable("user_tokens", Schema);
        userTokens.Property(item => item.UserId).HasColumnName("user_id");
        userTokens.Property(item => item.LoginProvider).HasColumnName("login_provider").HasMaxLength(128);
        userTokens.Property(item => item.Name).HasColumnName("name").HasMaxLength(128);
        userTokens.Property(item => item.Value).HasColumnName("value");

        var roleClaims = modelBuilder.Entity<IdentityRoleClaim<Guid>>();
        roleClaims.ToTable("role_claims", Schema);
        roleClaims.Property(item => item.Id).HasColumnName("id");
        roleClaims.Property(item => item.RoleId).HasColumnName("role_id");
        roleClaims.Property(item => item.ClaimType).HasColumnName("claim_type");
        roleClaims.Property(item => item.ClaimValue).HasColumnName("claim_value");
    }

    private static void ConfigureAuthenticationSetup(EntityTypeBuilder<AuthenticationSetupRow> entity)
    {
        entity.ToTable("authentication_setup", Schema, table =>
        {
            table.HasCheckConstraint("ck_authentication_setup_singleton", "id = 1");
            table.HasCheckConstraint(
                "ck_authentication_setup_completion",
                "(completed_at_utc IS NULL AND administrator_user_id IS NULL) OR "
                + "(completed_at_utc IS NOT NULL AND administrator_user_id IS NOT NULL)");
        });
        entity.HasKey(row => row.Id).HasName("pk_authentication_setup");
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(row => row.AdministratorUserId).HasColumnName("administrator_user_id");
        entity.Property(row => row.CompletedAtUtc).HasColumnName("completed_at_utc");
        entity.HasOne<LocalUser>()
            .WithMany()
            .HasForeignKey(row => row.AdministratorUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_authentication_setup_administrator");
        entity.HasData(new AuthenticationSetupRow { Id = 1 });
    }

    private static void ConfigurePackageInstallations(EntityTypeBuilder<PackageInstallationRow> entity)
    {
        entity.ToTable("package_installations", Schema, table =>
        {
            table.HasCheckConstraint("ck_package_installations_revision", "revision >= 1");
            table.HasCheckConstraint(
                "ck_package_installations_fault_metadata",
                "(state = 'Faulted' AND fault_code IS NOT NULL AND fault_detail IS NOT NULL AND faulted_at_utc IS NOT NULL) OR "
                + "(state <> 'Faulted' AND fault_code IS NULL AND fault_detail IS NULL AND faulted_at_utc IS NULL)");
        });

        entity.HasKey(row => row.Id).HasName("pk_package_installations");
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(row => row.PackageId).HasColumnName("package_id").HasMaxLength(128).IsRequired();
        entity.Property(row => row.State).HasColumnName("state").HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(row => row.Revision).HasColumnName("revision").IsConcurrencyToken();
        entity.Property(row => row.FaultCode).HasColumnName("fault_code").HasMaxLength(256);
        entity.Property(row => row.FaultDetail).HasColumnName("fault_detail").HasMaxLength(2048);
        entity.Property(row => row.FaultedAtUtc).HasColumnName("faulted_at_utc");
    }

    private static void ConfigureApplications(EntityTypeBuilder<ApplicationDefinitionRow> entity)
    {
        entity.ToTable("application_definitions", Schema, table =>
        {
            table.HasCheckConstraint("ck_application_definitions_revision", "revision >= 1");
            table.HasCheckConstraint(
                "ck_application_definitions_window_size",
                "minimum_width BETWEEN 120 AND 16384 AND minimum_height BETWEEN 120 AND 16384 "
                + "AND default_width BETWEEN minimum_width AND 16384 AND default_height BETWEEN minimum_height AND 16384");
        });

        entity.HasKey(row => row.Id).HasName("pk_application_definitions");
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(row => row.OwningPackageId).HasColumnName("owning_package_id").HasMaxLength(128).IsRequired();
        entity.Property(row => row.StableKey).HasColumnName("stable_key").HasMaxLength(64).IsRequired();
        entity.Property(row => row.DisplayNameKey).HasColumnName("display_name_key").HasMaxLength(128).IsRequired();
        entity.Property(row => row.InstancePolicy).HasColumnName("instance_policy").HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(row => row.DefaultWidth).HasColumnName("default_width");
        entity.Property(row => row.DefaultHeight).HasColumnName("default_height");
        entity.Property(row => row.MinimumWidth).HasColumnName("minimum_width");
        entity.Property(row => row.MinimumHeight).HasColumnName("minimum_height");
        entity.Property(row => row.IsEnabled).HasColumnName("is_enabled");
        entity.Property(row => row.Revision).HasColumnName("revision").IsConcurrencyToken();

        entity.HasIndex(row => new { row.OwningPackageId, row.StableKey })
            .IsUnique()
            .HasDatabaseName("ux_application_definitions_package_stable_key");

        entity.HasMany(row => row.SupportedViewports)
            .WithOne()
            .HasForeignKey(row => row.ApplicationDefinitionId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_application_viewports_application");
    }

    private static void ConfigureApplicationViewports(EntityTypeBuilder<ApplicationViewportRow> entity)
    {
        entity.ToTable("application_viewports", Schema);
        entity.HasKey(row => new { row.ApplicationDefinitionId, row.ViewportClass })
            .HasName("pk_application_viewports");
        entity.Property(row => row.ApplicationDefinitionId).HasColumnName("application_definition_id");
        entity.Property(row => row.ViewportClass).HasColumnName("viewport_class").HasConversion<string>().HasMaxLength(16);
    }

    private static void ConfigureLaunchTargets(EntityTypeBuilder<LaunchTargetRow> entity)
    {
        entity.ToTable("launch_targets", Schema, table =>
        {
            table.HasCheckConstraint("ck_launch_targets_revision", "revision >= 1");
            table.HasCheckConstraint(
                "ck_launch_targets_approval",
                "(approval_state = 'Approved' AND approved_at_utc IS NOT NULL AND approved_by_user_id IS NOT NULL) OR "
                + "(approval_state <> 'Approved' AND approved_at_utc IS NULL AND approved_by_user_id IS NULL)");
        });

        entity.HasKey(row => row.Id).HasName("pk_launch_targets");
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(row => row.ApplicationDefinitionId).HasColumnName("application_definition_id");
        entity.Property(row => row.OwningPackageId).HasColumnName("owning_package_id").HasMaxLength(128).IsRequired();
        entity.Property(row => row.ExternalIdentity).HasColumnName("external_identity").HasMaxLength(256).IsRequired();
        entity.Property(row => row.DisplayName).HasColumnName("display_name").HasMaxLength(256).IsRequired();
        entity.Property(row => row.ApprovalState).HasColumnName("approval_state").HasConversion<string>().HasMaxLength(16);
        entity.Property(row => row.FirstObservedAtUtc).HasColumnName("first_observed_at_utc");
        entity.Property(row => row.LastObservedAtUtc).HasColumnName("last_observed_at_utc");
        entity.Property(row => row.ApprovedAtUtc).HasColumnName("approved_at_utc");
        entity.Property(row => row.ApprovedByUserId).HasColumnName("approved_by_user_id");
        entity.Property(row => row.Revision).HasColumnName("revision").IsConcurrencyToken();

        entity.HasIndex(row => new { row.OwningPackageId, row.ExternalIdentity })
            .IsUnique()
            .HasDatabaseName("ux_launch_targets_package_external_identity");

        entity.HasOne<ApplicationDefinitionRow>()
            .WithMany()
            .HasForeignKey(row => row.ApplicationDefinitionId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_launch_targets_application");
    }

    private static void ConfigureLayouts(EntityTypeBuilder<DesktopLayoutRow> entity)
    {
        entity.ToTable("desktop_layouts", Schema, table =>
        {
            table.HasCheckConstraint("ck_desktop_layouts_revision", "revision >= 1");
        });

        entity.HasKey(row => row.Id).HasName("pk_desktop_layouts");
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(row => row.UserId).HasColumnName("user_id");
        entity.Property(row => row.ViewportClass).HasColumnName("viewport_class").HasConversion<string>().HasMaxLength(16);
        entity.Property(row => row.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
        entity.Property(row => row.IsDefault).HasColumnName("is_default");
        entity.Property(row => row.Revision).HasColumnName("revision").IsConcurrencyToken();
        entity.Property(row => row.UpdatedAtUtc).HasColumnName("updated_at_utc");

        entity.HasIndex(row => new { row.UserId, row.ViewportClass, row.Name })
            .IsUnique()
            .HasDatabaseName("ux_desktop_layouts_user_viewport_name");
        entity.HasIndex(row => new { row.UserId, row.ViewportClass })
            .IsUnique()
            .HasFilter("is_default")
            .HasDatabaseName("ux_desktop_layouts_default_per_viewport");

        entity.HasMany(row => row.Windows)
            .WithOne()
            .HasForeignKey(row => row.DesktopLayoutId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_desktop_windows_layout");
        entity.HasMany(row => row.Widgets)
            .WithOne()
            .HasForeignKey(row => row.DesktopLayoutId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_widget_placements_layout");
    }

    private static void ConfigureWindows(EntityTypeBuilder<DesktopWindowRow> entity)
    {
        entity.ToTable("desktop_windows", Schema, table =>
        {
            table.HasCheckConstraint("ck_desktop_windows_revision", "revision >= 1");
            table.HasCheckConstraint("ck_desktop_windows_z_index", "z_index >= 0");
            table.HasCheckConstraint(
                "ck_desktop_windows_bounds",
                "width BETWEEN 1 AND 16384 AND height BETWEEN 1 AND 16384 "
                + "AND restore_width BETWEEN 1 AND 16384 AND restore_height BETWEEN 1 AND 16384 "
                + "AND abs(x) <= 65536 AND abs(y) <= 65536 AND abs(restore_x) <= 65536 AND abs(restore_y) <= 65536");
        });

        entity.HasKey(row => row.Id).HasName("pk_desktop_windows");
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(row => row.DesktopLayoutId).HasColumnName("desktop_layout_id");
        entity.Property(row => row.ApplicationDefinitionId).HasColumnName("application_definition_id");
        entity.Property(row => row.LaunchTargetId).HasColumnName("launch_target_id");
        entity.Property(row => row.State).HasColumnName("state").HasConversion<string>().HasMaxLength(24);
        entity.Property(row => row.X).HasColumnName("x");
        entity.Property(row => row.Y).HasColumnName("y");
        entity.Property(row => row.Width).HasColumnName("width");
        entity.Property(row => row.Height).HasColumnName("height");
        entity.Property(row => row.RestoreX).HasColumnName("restore_x");
        entity.Property(row => row.RestoreY).HasColumnName("restore_y");
        entity.Property(row => row.RestoreWidth).HasColumnName("restore_width");
        entity.Property(row => row.RestoreHeight).HasColumnName("restore_height");
        entity.Property(row => row.ZIndex).HasColumnName("z_index");
        entity.Property(row => row.SessionReferenceId).HasColumnName("session_reference_id");
        entity.Property(row => row.CreatedAtUtc).HasColumnName("created_at_utc");
        entity.Property(row => row.UpdatedAtUtc).HasColumnName("updated_at_utc");
        entity.Property(row => row.Revision).HasColumnName("revision").IsConcurrencyToken();

        entity.HasIndex(row => new { row.DesktopLayoutId, row.ZIndex })
            .IsUnique()
            .HasDatabaseName("ux_desktop_windows_layout_z_index");
        entity.HasOne<ApplicationDefinitionRow>()
            .WithMany()
            .HasForeignKey(row => row.ApplicationDefinitionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_desktop_windows_application");
        entity.HasOne<LaunchTargetRow>()
            .WithMany()
            .HasForeignKey(row => row.LaunchTargetId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("fk_desktop_windows_launch_target");
        entity.HasOne<SessionReferenceRow>()
            .WithMany()
            .HasForeignKey(row => row.SessionReferenceId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("fk_desktop_windows_session");
    }

    private static void ConfigureWidgets(EntityTypeBuilder<WidgetPlacementRow> entity)
    {
        entity.ToTable("widget_placements", Schema, table =>
        {
            table.HasCheckConstraint("ck_widget_placements_revision", "revision >= 1");
            table.HasCheckConstraint(
                "ck_widget_placements_grid",
                "grid_column >= 0 AND grid_row >= 0 AND width_units > 0 AND height_units > 0 "
                + "AND grid_column + width_units <= 64 AND grid_row + height_units <= 64");
        });

        entity.HasKey(row => row.Id).HasName("pk_widget_placements");
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(row => row.DesktopLayoutId).HasColumnName("desktop_layout_id");
        entity.Property(row => row.WidgetKey).HasColumnName("widget_key").HasMaxLength(128).IsRequired();
        entity.Property(row => row.Column).HasColumnName("grid_column");
        entity.Property(row => row.Row).HasColumnName("grid_row");
        entity.Property(row => row.WidthUnits).HasColumnName("width_units");
        entity.Property(row => row.HeightUnits).HasColumnName("height_units");
        entity.Property(row => row.Revision).HasColumnName("revision").IsConcurrencyToken();
    }

    private static void ConfigureSessions(EntityTypeBuilder<SessionReferenceRow> entity)
    {
        entity.ToTable("session_references", Schema, table =>
        {
            table.HasCheckConstraint("ck_session_references_revision", "revision >= 1");
            table.HasCheckConstraint(
                "ck_session_references_ended_at",
                "(state = 'Ended' AND ended_at_utc IS NOT NULL) OR (state <> 'Ended' AND ended_at_utc IS NULL)");
            table.HasCheckConstraint(
                "ck_session_references_expiry",
                "expires_at_utc IS NULL OR expires_at_utc > created_at_utc");
        });

        entity.HasKey(row => row.Id).HasName("pk_session_references");
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(row => row.OwningPackageId).HasColumnName("owning_package_id").HasMaxLength(128).IsRequired();
        entity.Property(row => row.SessionKind).HasColumnName("session_kind").HasMaxLength(128).IsRequired();
        entity.Property(row => row.TargetReference).HasColumnName("target_reference").HasMaxLength(512).IsRequired();
        entity.Property(row => row.UserId).HasColumnName("user_id");
        entity.Property(row => row.State).HasColumnName("state").HasConversion<string>().HasMaxLength(24);
        entity.Property(row => row.LifecyclePolicy).HasColumnName("lifecycle_policy").HasConversion<string>().HasMaxLength(32);
        entity.Property(row => row.CreatedAtUtc).HasColumnName("created_at_utc");
        entity.Property(row => row.ConnectedAtUtc).HasColumnName("connected_at_utc");
        entity.Property(row => row.LastActivityAtUtc).HasColumnName("last_activity_at_utc");
        entity.Property(row => row.ExpiresAtUtc).HasColumnName("expires_at_utc");
        entity.Property(row => row.EndedAtUtc).HasColumnName("ended_at_utc");
        entity.Property(row => row.FailureCode).HasColumnName("failure_code").HasMaxLength(256);
        entity.Property(row => row.Revision).HasColumnName("revision").IsConcurrencyToken();
    }

    private static void ConfigureAgents(EntityTypeBuilder<AgentRow> entity)
    {
        entity.ToTable("agents", Schema, table =>
        {
            table.HasCheckConstraint("ck_agents_revision", "revision >= 1");
            table.HasCheckConstraint(
                "ck_agents_revoked_at",
                "(state = 'Revoked' AND revoked_at_utc IS NOT NULL) OR (state <> 'Revoked' AND revoked_at_utc IS NULL)");
        });

        entity.HasKey(row => row.Id).HasName("pk_agents");
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(row => row.Name).HasColumnName("name").HasMaxLength(256).IsRequired();
        entity.Property(row => row.MachineIdentity).HasColumnName("machine_identity").HasMaxLength(256).IsRequired();
        entity.Property(row => row.OperatingSystem).HasColumnName("operating_system").HasMaxLength(256).IsRequired();
        entity.Property(row => row.Architecture).HasColumnName("architecture").HasMaxLength(256).IsRequired();
        entity.Property(row => row.Version).HasColumnName("version").HasMaxLength(256).IsRequired();
        entity.Property(row => row.State).HasColumnName("state").HasConversion<string>().HasMaxLength(24);
        entity.Property(row => row.EnrolledAtUtc).HasColumnName("enrolled_at_utc");
        entity.Property(row => row.LastSeenAtUtc).HasColumnName("last_seen_at_utc");
        entity.Property(row => row.RevokedAtUtc).HasColumnName("revoked_at_utc");
        entity.Property(row => row.Revision).HasColumnName("revision").IsConcurrencyToken();
        entity.HasIndex(row => row.MachineIdentity).HasDatabaseName("ix_agents_machine_identity");
    }

    private static void ConfigureAgentCapabilities(EntityTypeBuilder<AgentCapabilityRow> entity)
    {
        entity.ToTable("agent_capabilities", Schema, table =>
        {
            table.HasCheckConstraint("ck_agent_capabilities_version", "capability_version >= 1 AND metadata_version >= 1");
            table.HasCheckConstraint("ck_agent_capabilities_revision", "revision >= 1");
            table.HasCheckConstraint("ck_agent_capabilities_metadata_length", "char_length(metadata) <= 8192");
        });

        entity.HasKey(row => row.Id).HasName("pk_agent_capabilities");
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(row => row.AgentId).HasColumnName("agent_id");
        entity.Property(row => row.CapabilityName).HasColumnName("capability_name").HasMaxLength(128).IsRequired();
        entity.Property(row => row.CapabilityVersion).HasColumnName("capability_version");
        entity.Property(row => row.Enabled).HasColumnName("enabled");
        entity.Property(row => row.MetadataVersion).HasColumnName("metadata_version");
        entity.Property(row => row.Metadata).HasColumnName("metadata").HasMaxLength(8192).IsRequired();
        entity.Property(row => row.ObservedAtUtc).HasColumnName("observed_at_utc");
        entity.Property(row => row.Revision).HasColumnName("revision").IsConcurrencyToken();

        entity.HasIndex(row => new { row.AgentId, row.CapabilityName })
            .IsUnique()
            .HasDatabaseName("ux_agent_capabilities_agent_name");
        entity.HasOne<AgentRow>()
            .WithMany()
            .HasForeignKey(row => row.AgentId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_agent_capabilities_agent");
    }

    private static void ConfigureProblems(EntityTypeBuilder<ProblemRow> entity)
    {
        entity.ToTable("problems", Schema, table =>
        {
            table.HasCheckConstraint("ck_problems_revision", "revision >= 1");
            table.HasCheckConstraint("ck_problems_observation_count", "observation_count >= 1");
            table.HasCheckConstraint(
                "ck_problems_state_timestamps",
                "(state = 'Active' AND acknowledged_at_utc IS NULL AND acknowledged_by_user_id IS NULL AND resolved_at_utc IS NULL) OR "
                + "(state = 'Acknowledged' AND acknowledged_at_utc IS NOT NULL AND acknowledged_by_user_id IS NOT NULL AND resolved_at_utc IS NULL) OR "
                + "(state = 'Resolved' AND resolved_at_utc IS NOT NULL) OR state = 'Suppressed'");
        });

        entity.HasKey(row => row.Id).HasName("pk_problems");
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(row => row.SourcePackageId).HasColumnName("source_package_id").HasMaxLength(128).IsRequired();
        entity.Property(row => row.ProblemType).HasColumnName("problem_type").HasMaxLength(256).IsRequired();
        entity.Property(row => row.StableResourceIdentity).HasColumnName("stable_resource_identity").HasMaxLength(256).IsRequired();
        entity.Property(row => row.Severity).HasColumnName("severity").HasConversion<string>().HasMaxLength(16);
        entity.Property(row => row.State).HasColumnName("state").HasConversion<string>().HasMaxLength(16);
        entity.Property(row => row.TitleKey).HasColumnName("title_key").HasMaxLength(256).IsRequired();
        entity.Property(row => row.FirstDetectedAtUtc).HasColumnName("first_detected_at_utc");
        entity.Property(row => row.LastObservedAtUtc).HasColumnName("last_observed_at_utc");
        entity.Property(row => row.AcknowledgedAtUtc).HasColumnName("acknowledged_at_utc");
        entity.Property(row => row.AcknowledgedByUserId).HasColumnName("acknowledged_by_user_id");
        entity.Property(row => row.ResolvedAtUtc).HasColumnName("resolved_at_utc");
        entity.Property(row => row.ObservationCount).HasColumnName("observation_count");
        entity.Property(row => row.Revision).HasColumnName("revision").IsConcurrencyToken();

        entity.HasIndex(row => new { row.SourcePackageId, row.ProblemType, row.StableResourceIdentity })
            .IsUnique()
            .HasDatabaseName("ux_problems_identity");
    }

    private static void ConfigureNotifications(EntityTypeBuilder<NotificationRow> entity)
    {
        entity.ToTable("notifications", Schema);
        entity.HasKey(row => row.Id).HasName("pk_notifications");
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(row => row.UserId).HasColumnName("user_id");
        entity.Property(row => row.SourcePackageId).HasColumnName("source_package_id").HasMaxLength(128);
        entity.Property(row => row.Severity).HasColumnName("severity").HasConversion<string>().HasMaxLength(16);
        entity.Property(row => row.TitleKey).HasColumnName("title_key").HasMaxLength(256).IsRequired();
        entity.Property(row => row.DeduplicationKey).HasColumnName("deduplication_key").HasMaxLength(512).IsRequired();
        entity.Property(row => row.CreatedAtUtc).HasColumnName("created_at_utc");
        entity.Property(row => row.ReadAtUtc).HasColumnName("read_at_utc");
        entity.Property(row => row.ActionLink).HasColumnName("action_link").HasMaxLength(1024);

        entity.HasIndex(row => new { row.UserId, row.DeduplicationKey })
            .IsUnique()
            .HasDatabaseName("ux_notifications_user_deduplication");
    }

    private static void ConfigureAuditEvents(EntityTypeBuilder<AuditEventRow> entity)
    {
        entity.ToTable("audit_events", Schema);
        entity.HasKey(row => row.Id).HasName("pk_audit_events");
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(row => row.OccurredAtUtc).HasColumnName("occurred_at_utc");
        entity.Property(row => row.UserId).HasColumnName("user_id");
        entity.Property(row => row.AgentId).HasColumnName("agent_id");
        entity.Property(row => row.SourcePackageId).HasColumnName("source_package_id").HasMaxLength(128);
        entity.Property(row => row.Action).HasColumnName("action").HasMaxLength(256).IsRequired();
        entity.Property(row => row.TargetType).HasColumnName("target_type").HasMaxLength(128).IsRequired();
        entity.Property(row => row.TargetId).HasColumnName("target_id").HasMaxLength(512).IsRequired();
        entity.Property(row => row.Outcome).HasColumnName("outcome").HasConversion<string>().HasMaxLength(16);
        entity.Property(row => row.CorrelationId).HasColumnName("correlation_id").HasMaxLength(64).IsRequired();
        entity.Property(row => row.RemoteAddress).HasColumnName("remote_address").HasMaxLength(128);
        entity.Property(row => row.Summary).HasColumnName("summary").HasMaxLength(512).IsRequired();
        entity.Property(row => row.SafeDetails).HasColumnName("safe_details").HasMaxLength(8192).IsRequired();
        entity.HasIndex(row => row.OccurredAtUtc).HasDatabaseName("ix_audit_events_occurred_at_utc");
    }

    private static void ConfigurePermissionAssignments(EntityTypeBuilder<PermissionAssignmentRow> entity)
    {
        entity.ToTable("permission_assignments", Schema, table =>
        {
            table.HasCheckConstraint(
                "ck_permission_assignments_scope",
                "(scope_kind = 'Global' AND scope_id IS NULL) OR (scope_kind <> 'Global' AND scope_id IS NOT NULL)");
        });

        entity.HasKey(row => row.Id).HasName("pk_permission_assignments");
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(row => row.SubjectKind).HasColumnName("subject_kind").HasConversion<string>().HasMaxLength(16);
        entity.Property(row => row.SubjectId).HasColumnName("subject_id");
        entity.Property(row => row.Permission).HasColumnName("permission").HasMaxLength(128).IsRequired();
        entity.Property(row => row.ScopeKind).HasColumnName("scope_kind").HasConversion<string>().HasMaxLength(16);
        entity.Property(row => row.ScopeId).HasColumnName("scope_id").HasMaxLength(512);
        entity.Property(row => row.GrantedAtUtc).HasColumnName("granted_at_utc");
        entity.Property(row => row.GrantedByUserId).HasColumnName("granted_by_user_id");

        entity.HasIndex(row => new { row.SubjectKind, row.SubjectId, row.Permission, row.ScopeKind, row.ScopeId })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasDatabaseName("ux_permission_assignments_grant");
    }
}

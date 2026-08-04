using JulOS.Contracts.Remote;
using JulOS.Infrastructure.Authentication;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JulOS.Infrastructure.Persistence.Core;

internal static class RemoteSessionModelConfiguration
{
    internal static void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ConfigureSessions(modelBuilder.Entity<RemoteSessionRow>());
    }

    private static void ConfigureSessions(EntityTypeBuilder<RemoteSessionRow> entity)
    {
        entity.ToTable("remote_sessions", CoreModelConfiguration.Schema, table =>
        {
            table.HasCheckConstraint("ck_remote_sessions_revision", "revision >= 1");
            table.HasCheckConstraint("ck_remote_sessions_target_port", "target_port BETWEEN 1 AND 65535");
            table.HasCheckConstraint(
                "ck_remote_sessions_viewport",
                "viewport_width BETWEEN 320 AND 7680 AND viewport_height BETWEEN 240 AND 4320 "
                + "AND device_scale_factor BETWEEN 0.5 AND 4");
            table.HasCheckConstraint(
                "ck_remote_sessions_timeouts",
                "idle_timeout_seconds BETWEEN 60 AND 86400 "
                + "AND maximum_session_seconds BETWEEN 300 AND 604800 "
                + "AND idle_timeout_seconds <= maximum_session_seconds");
            table.HasCheckConstraint(
                "ck_remote_sessions_state",
                $"state IN ('{RemoteSessionStates.Requested}', '{RemoteSessionStates.Provisioning}', "
                + $"'{RemoteSessionStates.Connecting}', '{RemoteSessionStates.Connected}', "
                + $"'{RemoteSessionStates.Disconnecting}', '{RemoteSessionStates.Disconnected}', "
                + $"'{RemoteSessionStates.Cancelled}', '{RemoteSessionStates.Expired}', "
                + $"'{RemoteSessionStates.Failed}')");
            table.HasCheckConstraint(
                "ck_remote_sessions_timestamps",
                "updated_at_utc >= created_at_utc AND last_activity_at_utc >= created_at_utc "
                + "AND expires_at_utc > created_at_utc");
            table.HasCheckConstraint(
                "ck_remote_sessions_terminal_time",
                $"(state IN ('{RemoteSessionStates.Disconnected}', '{RemoteSessionStates.Cancelled}', "
                + $"'{RemoteSessionStates.Expired}', '{RemoteSessionStates.Failed}') AND ended_at_utc IS NOT NULL) OR "
                + $"(state NOT IN ('{RemoteSessionStates.Disconnected}', '{RemoteSessionStates.Cancelled}', "
                + $"'{RemoteSessionStates.Expired}', '{RemoteSessionStates.Failed}') AND ended_at_utc IS NULL)");
            table.HasCheckConstraint(
                "ck_remote_sessions_display",
                "(display_kind IS NULL AND display_contract_version IS NULL AND display_endpoint IS NULL AND display_expires_at_utc IS NULL) OR "
                + "(state = 'connected' AND display_kind IS NOT NULL AND display_contract_version IS NOT NULL "
                + "AND display_endpoint IS NOT NULL AND display_expires_at_utc IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_remote_sessions_failure",
                $"(state = '{RemoteSessionStates.Failed}' AND failure_code IS NOT NULL "
                + "AND failure_detail IS NOT NULL AND failure_retryable IS NOT NULL) OR "
                + $"(state <> '{RemoteSessionStates.Failed}' AND failure_code IS NULL "
                + "AND failure_detail IS NULL AND failure_retryable IS NULL)");
            table.HasCheckConstraint(
                "ck_remote_sessions_cancellation",
                $"(state = '{RemoteSessionStates.Cancelled}' AND cancellation_operation_key IS NOT NULL) OR "
                + $"(state <> '{RemoteSessionStates.Cancelled}' AND cancellation_operation_key IS NULL "
                + "AND cancellation_reason IS NULL)");
        });

        entity.HasKey(row => row.Id).HasName("pk_remote_sessions");
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(row => row.OwnerUserId).HasColumnName("owner_user_id");
        entity.Property(row => row.CallerPackageId).HasColumnName("caller_package_id").HasMaxLength(128).IsRequired();
        entity.Property(row => row.OperationKey).HasColumnName("operation_key").HasMaxLength(128).IsRequired();
        entity.Property(row => row.RequestIdentity).HasColumnName("request_identity").HasMaxLength(64).IsRequired();
        entity.Property(row => row.Protocol).HasColumnName("protocol").HasMaxLength(32).IsRequired();
        entity.Property(row => row.TargetHost).HasColumnName("target_host").HasMaxLength(253).IsRequired();
        entity.Property(row => row.TargetPort).HasColumnName("target_port");
        entity.Property(row => row.SecretReferenceId).HasColumnName("secret_reference_id");
        entity.Property(row => row.ProfileId).HasColumnName("profile_id");
        entity.Property(row => row.NetworkProfileId).HasColumnName("network_profile_id");
        entity.Property(row => row.ViewportWidth).HasColumnName("viewport_width");
        entity.Property(row => row.ViewportHeight).HasColumnName("viewport_height");
        entity.Property(row => row.DeviceScaleFactor).HasColumnName("device_scale_factor").HasPrecision(4, 2);
        entity.Property(row => row.IdleTimeoutSeconds).HasColumnName("idle_timeout_seconds");
        entity.Property(row => row.MaximumSessionSeconds).HasColumnName("maximum_session_seconds");
        entity.Property(row => row.State).HasColumnName("state").HasMaxLength(32).IsRequired();
        entity.Property(row => row.CreatedAtUtc).HasColumnName("created_at_utc");
        entity.Property(row => row.UpdatedAtUtc).HasColumnName("updated_at_utc");
        entity.Property(row => row.LastActivityAtUtc).HasColumnName("last_activity_at_utc");
        entity.Property(row => row.ExpiresAtUtc).HasColumnName("expires_at_utc");
        entity.Property(row => row.ConnectedAtUtc).HasColumnName("connected_at_utc");
        entity.Property(row => row.EndedAtUtc).HasColumnName("ended_at_utc");
        entity.Property(row => row.RuntimeId).HasColumnName("runtime_id").HasMaxLength(128);
        entity.Property(row => row.DisplayKind).HasColumnName("display_kind").HasMaxLength(16);
        entity.Property(row => row.DisplayContractVersion).HasColumnName("display_contract_version").HasMaxLength(32);
        entity.Property(row => row.DisplayEndpoint).HasColumnName("display_endpoint").HasMaxLength(1024);
        entity.Property(row => row.DisplayExpiresAtUtc).HasColumnName("display_expires_at_utc");
        entity.Property(row => row.FailureCode).HasColumnName("failure_code").HasMaxLength(128);
        entity.Property(row => row.FailureDetail).HasColumnName("failure_detail").HasMaxLength(1024);
        entity.Property(row => row.FailureRetryable).HasColumnName("failure_retryable");
        entity.Property(row => row.CancellationOperationKey).HasColumnName("cancellation_operation_key").HasMaxLength(128);
        entity.Property(row => row.CancellationReason).HasColumnName("cancellation_reason").HasMaxLength(256);
        entity.Property(row => row.Revision).HasColumnName("revision").IsConcurrencyToken();

        entity.HasIndex(row => new { row.OwnerUserId, row.CallerPackageId, row.OperationKey })
            .IsUnique()
            .HasDatabaseName("ux_remote_sessions_owner_package_operation");
        entity.HasIndex(row => new { row.OwnerUserId, row.CallerPackageId, row.Id })
            .HasDatabaseName("ix_remote_sessions_owner_package_page");
        entity.HasIndex(row => row.RuntimeId)
            .IsUnique()
            .HasFilter("runtime_id IS NOT NULL")
            .HasDatabaseName("ux_remote_sessions_runtime");

        entity.HasOne<LocalUser>()
            .WithMany()
            .HasForeignKey(row => row.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_remote_sessions_owner");
    }
}

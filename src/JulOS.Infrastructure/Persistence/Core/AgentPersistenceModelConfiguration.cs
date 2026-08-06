using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Persistence.Core;

internal static class AgentPersistenceModelConfiguration
{
    internal static void Configure(ModelBuilder modelBuilder)
    {
        var enrollment = modelBuilder.Entity<AgentEnrollmentTokenRow>();
        enrollment.ToTable("agent_enrollment_tokens", CoreModelConfiguration.Schema, table =>
        {
            table.HasCheckConstraint(
                "ck_agent_enrollment_tokens_expiry",
                "expires_at_utc > created_at_utc");
            table.HasCheckConstraint(
                "ck_agent_enrollment_tokens_redemption",
                "(redeemed_at_utc IS NULL AND redeemed_by_agent_id IS NULL) OR "
                + "(redeemed_at_utc IS NOT NULL AND redeemed_by_agent_id IS NOT NULL)");
        });
        enrollment.HasKey(row => row.Id).HasName("pk_agent_enrollment_tokens");
        enrollment.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        enrollment.Property(row => row.TokenHash).HasColumnName("token_hash").HasMaxLength(32).IsRequired();
        enrollment.Property(row => row.CreatedByUserId).HasColumnName("created_by_user_id");
        enrollment.Property(row => row.Description).HasColumnName("description").HasMaxLength(256).IsRequired();
        enrollment.Property(row => row.CreatedAtUtc).HasColumnName("created_at_utc");
        enrollment.Property(row => row.ExpiresAtUtc).HasColumnName("expires_at_utc");
        enrollment.Property(row => row.RedeemedAtUtc).HasColumnName("redeemed_at_utc");
        enrollment.Property(row => row.RedeemedByAgentId).HasColumnName("redeemed_by_agent_id");
        enrollment.HasIndex(row => row.TokenHash)
            .IsUnique()
            .HasDatabaseName("ux_agent_enrollment_tokens_hash");
        enrollment.HasOne<AgentRow>()
            .WithMany()
            .HasForeignKey(row => row.RedeemedByAgentId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_agent_enrollment_tokens_agent");

        var credential = modelBuilder.Entity<AgentCredentialRow>();
        credential.ToTable("agent_credentials", CoreModelConfiguration.Schema, table =>
        {
            table.HasCheckConstraint("ck_agent_credentials_revision", "revision >= 1");
        });
        credential.HasKey(row => row.AgentId).HasName("pk_agent_credentials");
        credential.Property(row => row.AgentId).HasColumnName("agent_id").ValueGeneratedNever();
        credential.Property(row => row.CredentialHash).HasColumnName("credential_hash").HasMaxLength(32).IsRequired();
        credential.Property(row => row.CreatedAtUtc).HasColumnName("created_at_utc");
        credential.Property(row => row.RotatedAtUtc).HasColumnName("rotated_at_utc");
        credential.Property(row => row.RevokedAtUtc).HasColumnName("revoked_at_utc");
        credential.Property(row => row.Revision).HasColumnName("revision").IsConcurrencyToken();
        credential.HasOne<AgentRow>()
            .WithOne()
            .HasForeignKey<AgentCredentialRow>(row => row.AgentId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_agent_credentials_agent");

        var command = modelBuilder.Entity<AgentCommandRow>();
        command.ToTable("agent_commands", CoreModelConfiguration.Schema, table =>
        {
            table.HasCheckConstraint("ck_agent_commands_revision", "revision >= 1");
            table.HasCheckConstraint(
                "ck_agent_commands_state",
                "state IN ('queued', 'running', 'succeeded', 'failed', 'expired', 'cancelled')");
            table.HasCheckConstraint("ck_agent_commands_expiry", "expires_at_utc > created_at_utc");
        });
        command.HasKey(row => row.Id).HasName("pk_agent_commands");
        command.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        command.Property(row => row.AgentId).HasColumnName("agent_id");
        command.Property(row => row.OperationKey).HasColumnName("operation_key").HasMaxLength(256).IsRequired();
        command.Property(row => row.CommandType).HasColumnName("command_type").HasMaxLength(128).IsRequired();
        command.Property(row => row.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb").IsRequired();
        command.Property(row => row.State).HasColumnName("state").HasMaxLength(16).IsRequired();
        command.Property(row => row.CreatedAtUtc).HasColumnName("created_at_utc");
        command.Property(row => row.ExpiresAtUtc).HasColumnName("expires_at_utc");
        command.Property(row => row.StartedAtUtc).HasColumnName("started_at_utc");
        command.Property(row => row.CompletedAtUtc).HasColumnName("completed_at_utc");
        command.Property(row => row.ResultJson).HasColumnName("result_json").HasColumnType("jsonb");
        command.Property(row => row.ErrorCode).HasColumnName("error_code").HasMaxLength(256);
        command.Property(row => row.Revision).HasColumnName("revision").IsConcurrencyToken();
        command.HasIndex(row => new { row.AgentId, row.OperationKey })
            .IsUnique()
            .HasDatabaseName("ux_agent_commands_agent_operation_key");
        command.HasIndex(row => new { row.AgentId, row.State, row.CreatedAtUtc })
            .HasDatabaseName("ix_agent_commands_agent_state_created");
        command.HasOne<AgentRow>()
            .WithMany()
            .HasForeignKey(row => row.AgentId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_agent_commands_agent");

        var metric = modelBuilder.Entity<AgentMetricSampleRow>();
        metric.ToTable("agent_metric_samples", CoreModelConfiguration.Schema);
        metric.HasKey(row => row.Id).HasName("pk_agent_metric_samples");
        metric.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        metric.Property(row => row.AgentId).HasColumnName("agent_id");
        metric.Property(row => row.MetricName).HasColumnName("metric_name").HasMaxLength(128).IsRequired();
        metric.Property(row => row.Value).HasColumnName("value");
        metric.Property(row => row.Unit).HasColumnName("unit").HasMaxLength(32).IsRequired();
        metric.Property(row => row.LabelsJson).HasColumnName("labels_json").HasColumnType("jsonb").IsRequired();
        metric.Property(row => row.ObservedAtUtc).HasColumnName("observed_at_utc");
        metric.Property(row => row.ReceivedAtUtc).HasColumnName("received_at_utc");
        metric.HasIndex(row => new { row.AgentId, row.MetricName, row.ObservedAtUtc })
            .HasDatabaseName("ix_agent_metric_samples_agent_metric_time");
        metric.HasOne<AgentRow>()
            .WithMany()
            .HasForeignKey(row => row.AgentId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_agent_metric_samples_agent");
    }
}

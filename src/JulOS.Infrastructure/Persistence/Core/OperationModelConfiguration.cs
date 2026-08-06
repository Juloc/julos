using JulOS.Application.Operations;
using JulOS.Infrastructure.Authentication;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JulOS.Infrastructure.Persistence.Core;

internal static class OperationModelConfiguration
{
    internal static void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        ConfigureOperations(modelBuilder.Entity<OperationRow>());
        ConfigureProgressEvents(modelBuilder.Entity<OperationProgressEventRow>());
    }

    private static void ConfigureOperations(EntityTypeBuilder<OperationRow> entity)
    {
        entity.ToTable("operations", CoreModelConfiguration.Schema, table =>
        {
            table.HasCheckConstraint("ck_operations_revision", "revision >= 1");
            table.HasCheckConstraint(
                "ck_operations_state",
                "state IN ('Queued', 'Running', 'Succeeded', 'Failed', 'Cancelled')");
            table.HasCheckConstraint(
                "ck_operations_progress",
                "progress_percent IS NULL OR progress_percent BETWEEN 0 AND 100");
            table.HasCheckConstraint(
                "ck_operations_lifecycle",
                "(state = 'Queued' AND started_at_utc IS NULL AND completed_at_utc IS NULL AND failure_code IS NULL AND failure_detail IS NULL) OR "
                + "(state = 'Running' AND started_at_utc IS NOT NULL AND completed_at_utc IS NULL AND failure_code IS NULL AND failure_detail IS NULL) OR "
                + "(state = 'Succeeded' AND started_at_utc IS NOT NULL AND completed_at_utc IS NOT NULL AND failure_code IS NULL AND failure_detail IS NULL) OR "
                + "(state = 'Failed' AND started_at_utc IS NOT NULL AND completed_at_utc IS NOT NULL AND failure_code IS NOT NULL AND failure_detail IS NOT NULL) OR "
                + "(state = 'Cancelled' AND completed_at_utc IS NOT NULL AND failure_code IS NULL AND failure_detail IS NULL)");
        });

        entity.HasKey(row => row.Id).HasName("pk_operations");
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(row => row.OwnerUserId).HasColumnName("owner_user_id");
        entity.Property(row => row.OperationType).HasColumnName("operation_type").HasMaxLength(128).IsRequired();
        entity.Property(row => row.SourcePackageId).HasColumnName("source_package_id").HasMaxLength(128);
        entity.Property(row => row.TargetReference).HasColumnName("target_reference").HasMaxLength(512).IsRequired();
        entity.Property(row => row.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128).IsRequired();
        entity.Property(row => row.RequestFingerprint).HasColumnName("request_fingerprint").HasMaxLength(64).IsFixedLength().IsRequired();
        entity.Property(row => row.State).HasColumnName("state").HasConversion<string>().HasMaxLength(16).IsRequired();
        entity.Property(row => row.ProgressPercent).HasColumnName("progress_percent");
        entity.Property(row => row.CurrentStep).HasColumnName("current_step").HasMaxLength(256);
        entity.Property(row => row.CreatedAtUtc).HasColumnName("created_at_utc");
        entity.Property(row => row.StartedAtUtc).HasColumnName("started_at_utc");
        entity.Property(row => row.CompletedAtUtc).HasColumnName("completed_at_utc");
        entity.Property(row => row.FailureCode).HasColumnName("failure_code").HasMaxLength(128);
        entity.Property(row => row.FailureDetail).HasColumnName("failure_detail").HasMaxLength(1024);
        entity.Property(row => row.CorrelationId).HasColumnName("correlation_id").HasMaxLength(64).IsRequired();
        entity.Property(row => row.CancellationRequestedAtUtc).HasColumnName("cancellation_requested_at_utc");
        entity.Property(row => row.Revision).HasColumnName("revision").IsConcurrencyToken();

        entity.HasIndex(row => new { row.OwnerUserId, row.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ux_operations_owner_idempotency");
        entity.HasIndex(row => new { row.OwnerUserId, row.CreatedAtUtc })
            .HasDatabaseName("ix_operations_owner_created_at_utc");
        entity.HasOne<LocalUser>()
            .WithMany()
            .HasForeignKey(row => row.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_operations_owner_user");
    }

    private static void ConfigureProgressEvents(EntityTypeBuilder<OperationProgressEventRow> entity)
    {
        entity.ToTable("operation_progress_events", CoreModelConfiguration.Schema, table =>
        {
            table.HasCheckConstraint(
                "ck_operation_progress_events_progress",
                "progress_percent IS NULL OR progress_percent BETWEEN 0 AND 100");
        });

        entity.HasKey(row => row.Id).HasName("pk_operation_progress_events");
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(row => row.OperationId).HasColumnName("operation_id");
        entity.Property(row => row.ProgressPercent).HasColumnName("progress_percent");
        entity.Property(row => row.CurrentStep).HasColumnName("current_step").HasMaxLength(256).IsRequired();
        entity.Property(row => row.OccurredAtUtc).HasColumnName("occurred_at_utc");

        entity.HasIndex(row => new { row.OperationId, row.OccurredAtUtc, row.Id })
            .HasDatabaseName("ix_operation_progress_events_order");
        entity.HasOne<OperationRow>()
            .WithMany(row => row.ProgressEvents)
            .HasForeignKey(row => row.OperationId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_operation_progress_events_operation");
    }
}

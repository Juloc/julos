using JulOS.Application.Secrets;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JulOS.Infrastructure.Persistence.Core;

internal static class SecretReferenceModelConfiguration
{
    internal static void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ConfigureSecretReferences(modelBuilder.Entity<SecretReferenceRow>());
    }

    private static void ConfigureSecretReferences(EntityTypeBuilder<SecretReferenceRow> entity)
    {
        entity.ToTable("secret_references", CoreModelConfiguration.Schema, table =>
        {
            table.HasCheckConstraint("ck_secret_references_revision", "revision >= 1");
            table.HasCheckConstraint(
                "ck_secret_references_scope",
                "(owning_scope_type = 'System' AND owning_scope_id IS NULL) OR "
                + "(owning_scope_type = 'Package' AND owning_scope_id IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_secret_references_rotation",
                "rotated_at_utc IS NULL OR rotated_at_utc >= created_at_utc");
            table.HasCheckConstraint(
                "ck_secret_references_deletion",
                "deleted_at_utc IS NULL OR deleted_at_utc >= created_at_utc");
            table.HasCheckConstraint(
                "ck_secret_references_protected_value",
                "(deleted_at_utc IS NULL "
                + "AND encryption_key_id IS NOT NULL "
                + "AND nonce IS NOT NULL AND octet_length(nonce) = 12 "
                + "AND ciphertext IS NOT NULL AND octet_length(ciphertext) > 0 "
                + "AND authentication_tag IS NOT NULL AND octet_length(authentication_tag) = 16) "
                + "OR (deleted_at_utc IS NOT NULL "
                + "AND encryption_key_id IS NULL AND nonce IS NULL "
                + "AND ciphertext IS NULL AND authentication_tag IS NULL)");
        });

        entity.HasKey(row => row.Id).HasName("pk_secret_references");
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(row => row.OwningScopeType)
            .HasColumnName("owning_scope_type")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();
        entity.Property(row => row.OwningScopeId).HasColumnName("owning_scope_id").HasMaxLength(128);
        entity.Property(row => row.Purpose).HasColumnName("purpose").HasMaxLength(128).IsRequired();
        entity.Property(row => row.StorageProvider).HasColumnName("storage_provider").HasMaxLength(64).IsRequired();
        entity.Property(row => row.EncryptionKeyId).HasColumnName("encryption_key_id").HasMaxLength(64);
        entity.Property(row => row.Nonce).HasColumnName("nonce");
        entity.Property(row => row.Ciphertext).HasColumnName("ciphertext");
        entity.Property(row => row.AuthenticationTag).HasColumnName("authentication_tag");
        entity.Property(row => row.CreatedAtUtc).HasColumnName("created_at_utc");
        entity.Property(row => row.RotatedAtUtc).HasColumnName("rotated_at_utc");
        entity.Property(row => row.DeletedAtUtc).HasColumnName("deleted_at_utc");
        entity.Property(row => row.Revision).HasColumnName("revision").IsConcurrencyToken();

        entity.HasIndex(row => new { row.OwningScopeType, row.OwningScopeId, row.Purpose })
            .HasDatabaseName("ix_secret_references_scope_purpose");
    }
}

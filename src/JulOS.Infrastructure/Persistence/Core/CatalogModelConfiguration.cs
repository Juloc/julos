using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JulOS.Infrastructure.Persistence.Core;

/// <summary>Maps the CAT-002 catalog tables into the Core schema.</summary>
internal static class CatalogModelConfiguration
{
    private const string Schema = CoreDbContext.SchemaName;

    internal static void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<CatalogSourceRow>(ConfigureSources);
        modelBuilder.Entity<CatalogPublisherKeyRow>(ConfigurePublisherKeys);
        modelBuilder.Entity<CatalogEntryCacheRow>(ConfigureEntryCache);
    }

    private static void ConfigureEntryCache(EntityTypeBuilder<CatalogEntryCacheRow> entity)
    {
        entity.ToTable("catalog_entry_cache", Schema, table =>
        {
            table.HasCheckConstraint("ck_catalog_entry_cache_revision", "revision >= 1");
            table.HasCheckConstraint(
                "ck_catalog_entry_cache_signature_state",
                "signature_state IN ('TrustedSigned', 'UnknownSigned', 'NotSigned', 'InvalidSignature')");
            // A signed entry names who signed it and with which key; an unsigned one names
            // nobody. Half of that identity would make the recorded state unexplainable.
            table.HasCheckConstraint(
                "ck_catalog_entry_cache_signer",
                "(signature_state = 'NotSigned' AND publisher_id IS NULL AND signature_key_id IS NULL "
                + "AND public_key_fingerprint IS NULL) "
                + "OR (signature_state <> 'NotSigned' AND publisher_id IS NOT NULL "
                + "AND signature_key_id IS NOT NULL AND public_key_fingerprint IS NOT NULL)");
        });

        entity.HasKey(row => row.Id).HasName("pk_catalog_entry_cache");
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(row => row.CatalogSourceId).HasColumnName("catalog_source_id");
        entity.Property(row => row.AppId).HasColumnName("app_id").HasMaxLength(64).IsRequired();
        entity.Property(row => row.Version).HasColumnName("version").HasMaxLength(64).IsRequired();
        entity.Property(row => row.SourceRevision).HasColumnName("source_revision");
        entity.Property(row => row.SourceDigest).HasColumnName("source_digest").HasMaxLength(256).IsRequired();
        entity.Property(row => row.DefinitionDigest)
            .HasColumnName("definition_digest")
            .HasMaxLength(64)
            .IsRequired();
        entity.Property(row => row.Definition).HasColumnName("definition").HasColumnType("jsonb").IsRequired();
        entity.Property(row => row.PublisherId).HasColumnName("publisher_id").HasMaxLength(128);
        entity.Property(row => row.SignatureKeyId).HasColumnName("signature_key_id").HasMaxLength(128);
        entity.Property(row => row.PublicKeyFingerprint)
            .HasColumnName("public_key_fingerprint")
            .HasMaxLength(80);
        entity.Property(row => row.SignatureState)
            .HasColumnName("signature_state")
            .HasConversion<string>()
            .HasMaxLength(24);
        entity.Property(row => row.TrustAssessmentDigest)
            .HasColumnName("trust_assessment_digest")
            .HasMaxLength(64)
            .IsRequired();
        entity.Property(row => row.CachedAtUtc).HasColumnName("cached_at_utc");
        entity.Property(row => row.Revision).HasColumnName("revision").IsConcurrencyToken();

        // One version of one application has one cached definition per source. The index
        // enforces what the index parser already refuses, so a source cannot reach a state
        // the document format forbids by way of two partially applied refreshes.
        entity.HasIndex(row => new { row.CatalogSourceId, row.AppId, row.Version })
            .IsUnique()
            .HasDatabaseName("ux_catalog_entry_cache_identity");

        entity.HasOne<CatalogSourceRow>()
            .WithMany()
            .HasForeignKey(row => row.CatalogSourceId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_catalog_entry_cache_source");
    }

    private static void ConfigureSources(EntityTypeBuilder<CatalogSourceRow> entity)
    {
        entity.ToTable("catalog_sources", Schema, table =>
        {
            table.HasCheckConstraint("ck_catalog_sources_revision", "revision >= 1");
            table.HasCheckConstraint(
                "ck_catalog_sources_kind",
                "kind IN ('Official', 'Https', 'Git', 'Oci', 'Local')");
            // Only the built-in source may be official: otherwise an administrator could
            // mint a second one and inherit the pinned key set that belongs to the real one.
            table.HasCheckConstraint(
                "ck_catalog_sources_trust_level",
                "(kind = 'Official' AND trust_level = 'Official') "
                + "OR (kind <> 'Official' AND trust_level IN ('AdministratorTrusted', 'Custom'))");
            table.HasCheckConstraint(
                "ck_catalog_sources_refresh_state",
                "last_refresh_state IN ('Never', 'Fresh', 'Stale')");
            // A stale marker means a previous catalog is still being served, so there has to
            // be one; and a fresh one is the catalog the last refresh produced.
            table.HasCheckConstraint(
                "ck_catalog_sources_refresh_evidence",
                "(last_refresh_state = 'Never' AND last_successful_revision IS NULL) "
                + "OR (last_refresh_state <> 'Never' AND last_successful_revision IS NOT NULL "
                + "AND last_successful_digest IS NOT NULL)");
        });

        entity.HasKey(row => row.Id).HasName("pk_catalog_sources");
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(row => row.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(16);
        entity.Property(row => row.DisplayName).HasColumnName("display_name").HasMaxLength(128).IsRequired();
        entity.Property(row => row.Location).HasColumnName("location").HasMaxLength(512).IsRequired();
        entity.Property(row => row.AuthenticationSecretReferenceId)
            .HasColumnName("authentication_secret_reference_id");
        entity.Property(row => row.SourceIdentity).HasColumnName("source_identity").HasMaxLength(128);
        entity.Property(row => row.TrustLevel)
            .HasColumnName("trust_level")
            .HasConversion<string>()
            .HasMaxLength(24);
        entity.Property(row => row.Enabled).HasColumnName("enabled");
        entity.Property(row => row.DeletedAtUtc).HasColumnName("deleted_at_utc");
        entity.Property(row => row.LastSuccessfulRevision).HasColumnName("last_successful_revision");
        entity.Property(row => row.LastSuccessfulDigest).HasColumnName("last_successful_digest").HasMaxLength(256);
        entity.Property(row => row.LastRefreshAtUtc).HasColumnName("last_refresh_at_utc");
        entity.Property(row => row.LastRefreshState)
            .HasColumnName("last_refresh_state")
            .HasConversion<string>()
            .HasMaxLength(16);
        entity.Property(row => row.LastFailureCode).HasColumnName("last_failure_code").HasMaxLength(128);
        entity.Property(row => row.Revision).HasColumnName("revision").IsConcurrencyToken();

        // One live source per location. A tombstone keeps its row, so the uniqueness is
        // partial: removing a source must not block adding the same location again.
        entity.HasIndex(row => row.Location)
            .IsUnique()
            .HasFilter("deleted_at_utc IS NULL")
            .HasDatabaseName("ux_catalog_sources_location");

        entity.HasMany(row => row.PublisherKeys)
            .WithOne()
            .HasForeignKey(row => row.CatalogSourceId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_catalog_publisher_keys_source");
    }

    private static void ConfigurePublisherKeys(EntityTypeBuilder<CatalogPublisherKeyRow> entity)
    {
        entity.ToTable("catalog_publisher_keys", Schema, table =>
        {
            table.HasCheckConstraint("ck_catalog_publisher_keys_revision", "revision >= 1");
            table.HasCheckConstraint(
                "ck_catalog_publisher_keys_trust",
                "administrator_trust_state IN ('Unknown', 'Trusted', 'Distrusted')");
            // A decision records who made it; clearing it back to unknown clears them, so an
            // undecided key never points at an administrator who no longer stands behind it.
            table.HasCheckConstraint(
                "ck_catalog_publisher_keys_decision",
                "(administrator_trust_state = 'Unknown' AND administrator_decision_by_user_id IS NULL "
                + "AND administrator_decision_at_utc IS NULL) "
                + "OR (administrator_trust_state <> 'Unknown' AND administrator_decision_by_user_id IS NOT NULL "
                + "AND administrator_decision_at_utc IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_catalog_publisher_keys_validity",
                "valid_until_utc IS NULL OR valid_until_utc > valid_from_utc");
        });

        entity.HasKey(row => row.Id).HasName("pk_catalog_publisher_keys");
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(row => row.CatalogSourceId).HasColumnName("catalog_source_id");
        entity.Property(row => row.PublisherId).HasColumnName("publisher_id").HasMaxLength(128).IsRequired();
        entity.Property(row => row.KeyId).HasColumnName("key_id").HasMaxLength(128).IsRequired();
        entity.Property(row => row.Algorithm).HasColumnName("algorithm").HasMaxLength(64).IsRequired();
        entity.Property(row => row.PublicKeySpki).HasColumnName("public_key_spki").HasMaxLength(4096).IsRequired();
        entity.Property(row => row.PublicKeyFingerprint)
            .HasColumnName("public_key_fingerprint")
            .HasMaxLength(80)
            .IsRequired();
        entity.Property(row => row.ValidFromUtc).HasColumnName("valid_from_utc");
        entity.Property(row => row.ValidUntilUtc).HasColumnName("valid_until_utc");
        entity.Property(row => row.RevokedAtUtc).HasColumnName("revoked_at_utc");
        entity.Property(row => row.AdministratorTrustState)
            .HasColumnName("administrator_trust_state")
            .HasConversion<string>()
            .HasMaxLength(16);
        entity.Property(row => row.AdministratorDecisionByUserId)
            .HasColumnName("administrator_decision_by_user_id");
        entity.Property(row => row.AdministratorDecisionAtUtc).HasColumnName("administrator_decision_at_utc");
        entity.Property(row => row.FirstObservedSourceRevision).HasColumnName("first_observed_source_revision");
        entity.Property(row => row.LastObservedSourceRevision).HasColumnName("last_observed_source_revision");
        entity.Property(row => row.Revision).HasColumnName("revision").IsConcurrencyToken();

        // A key identity belongs to exactly one key within one source. The database holds
        // that as well as the aggregate, because a source that republishes an identity with
        // different bytes is replacing what vouches for everything it ever published.
        entity.HasIndex(row => new { row.CatalogSourceId, row.PublisherId, row.KeyId })
            .IsUnique()
            .HasDatabaseName("ux_catalog_publisher_keys_identity");
    }
}

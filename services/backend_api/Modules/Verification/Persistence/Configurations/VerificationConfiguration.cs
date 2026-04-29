using BackendApi.Modules.Verification.Entities;
using BackendApi.Modules.Verification.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BackendApi.Modules.Verification.Persistence.Configurations;

/// <summary>EF configuration for the <c>verifications</c> table per spec 020 data-model §2.1.</summary>
public sealed class VerificationConfiguration : IEntityTypeConfiguration<Entities.Verification>
{
    public void Configure(EntityTypeBuilder<Entities.Verification> builder)
    {
        builder.ToTable("verifications", "verification", t =>
        {
            t.HasCheckConstraint(
                "CK_verifications_market_code_enum",
                "\"MarketCode\" IN ('eg','ksa')");
            t.HasCheckConstraint(
                "CK_verifications_state_enum",
                "\"State\" IN ('submitted','in-review','info-requested','approved','rejected','expired','revoked','superseded','void')");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.CustomerId).IsRequired();
        builder.Property(x => x.MarketCode).HasColumnType("text").IsRequired();
        builder.Property(x => x.SchemaVersion).IsRequired();
        builder.Property(x => x.Profession).HasColumnType("text").IsRequired();
        builder.Property(x => x.RegulatorIdentifier).HasColumnType("text").IsRequired();
        // CHECK constraint matches the enum's wire-format mapper exactly. Conversion
        // ensures EF persists the snake-case slug, not the C# enum name or integer.
        builder.Property(x => x.State)
            .HasConversion(new ValueConverter<VerificationState, string>(
                v => v.ToWireValue(),
                s => ParseStateWireValue(s)))
            .HasColumnType("text")
            .IsRequired();
        builder.Property(x => x.SubmittedAt).IsRequired();
        builder.Property(x => x.RestrictionPolicySnapshotJson)
            .HasColumnType("jsonb")
            .HasColumnName("RestrictionPolicySnapshot")
            .IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.Xmin).IsRowVersion().HasColumnName("xmin");

        // §2.1 indexes
        builder.HasIndex(x => new { x.CustomerId, x.State, x.MarketCode })
            .HasDatabaseName("IX_verifications_customer_state_market");

        builder.HasIndex(x => new { x.State, x.MarketCode, x.SubmittedAt })
            .HasDatabaseName("IX_verifications_state_market_submitted")
            .HasFilter("\"State\" IN ('submitted','in-review','info-requested')");

        builder.HasIndex(x => x.ExpiresAt)
            .HasDatabaseName("IX_verifications_expires_at")
            .HasFilter("\"State\" = 'approved'");

        builder.HasIndex(x => x.SupersedesId)
            .HasDatabaseName("IX_verifications_supersedes")
            .HasFilter("\"SupersedesId\" IS NOT NULL");
        // The concurrency guard — at most one non-terminal renewal pointing at
        // a given prior approval — is a partial UNIQUE index added via raw SQL
        // in the init migration alongside this index. EF cannot model two
        // distinct indexes on the same column expression, so we keep this one
        // non-unique for general supersession lookups and add the unique guard
        // imperatively. The handler catches the unique-violation and returns
        // RenewalAlreadyPending.

        // FK to market schema is composite (MarketCode, SchemaVersion) — declared as a
        // value-only relationship via HasOne to avoid duplicating the FK columns. EF
        // models this as a navigation-less FK using the existing scalar properties.
        builder.HasOne<VerificationMarketSchema>()
            .WithMany()
            .HasForeignKey(x => new { x.MarketCode, x.SchemaVersion })
            .HasPrincipalKey(s => new { s.MarketCode, s.Version })
            .OnDelete(DeleteBehavior.Restrict);

        // Self-referential renewal pointers (FR-020).
        builder.HasOne<Entities.Verification>()
            .WithMany()
            .HasForeignKey(x => x.SupersedesId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Entities.Verification>()
            .WithMany()
            .HasForeignKey(x => x.SupersededById)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static VerificationState ParseStateWireValue(string wire)
    {
        if (!VerificationStateExtensions.TryParseWireValue(wire, out var parsed))
        {
            throw new InvalidOperationException(
                $"Unknown VerificationState wire value: '{wire}'.");
        }
        return parsed;
    }
}

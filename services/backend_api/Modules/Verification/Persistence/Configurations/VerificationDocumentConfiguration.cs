using BackendApi.Modules.Verification.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Verification.Persistence.Configurations;

/// <summary>EF configuration for <c>verification_documents</c> per spec 020 data-model §2.2.</summary>
public sealed class VerificationDocumentConfiguration : IEntityTypeConfiguration<VerificationDocument>
{
    public void Configure(EntityTypeBuilder<VerificationDocument> builder)
    {
        builder.ToTable("verification_documents", "verification", t =>
        {
            t.HasCheckConstraint(
                "CK_verification_documents_content_type_allowlist",
                "\"ContentType\" IN ('application/pdf','image/jpeg','image/png','image/heic')");
            t.HasCheckConstraint(
                "CK_verification_documents_size_bytes_limit",
                "\"SizeBytes\" > 0 AND \"SizeBytes\" <= 10485760");
            t.HasCheckConstraint(
                "CK_verification_documents_scan_status_enum",
                "\"ScanStatus\" IN ('pending','clean','infected','error')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.VerificationId).IsRequired();
        builder.Property(x => x.StorageKey).HasColumnType("text");
        builder.Property(x => x.ContentType).HasColumnType("text").IsRequired();
        builder.Property(x => x.SizeBytes).IsRequired();
        builder.Property(x => x.ScanStatus).HasColumnType("text").IsRequired().HasDefaultValue("pending");
        builder.Property(x => x.UploadedAt).IsRequired();

        builder.HasIndex(x => x.VerificationId)
            .HasDatabaseName("IX_verification_documents_verification");

        builder.HasIndex(x => x.PurgeAfter)
            .HasDatabaseName("IX_verification_documents_purge_after")
            .HasFilter("\"PurgedAt\" IS NULL AND \"PurgeAfter\" IS NOT NULL");

        builder.HasOne<Entities.Verification>()
            .WithMany()
            .HasForeignKey(x => x.VerificationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

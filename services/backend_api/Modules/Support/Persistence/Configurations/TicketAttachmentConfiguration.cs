using BackendApi.Modules.Support.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Support.Persistence.Configurations;

public sealed class TicketAttachmentConfiguration : IEntityTypeConfiguration<TicketAttachment>
{
    public void Configure(EntityTypeBuilder<TicketAttachment> builder)
    {
        builder.ToTable("ticket_attachments", "support", t =>
        {
            t.HasCheckConstraint("CK_ticket_attachments_state",
                "\"State\" IN ('active','redacted')");
            t.HasCheckConstraint("CK_ticket_attachments_size_bytes",
                "\"SizeBytes\" >= 0");
            t.HasCheckConstraint("CK_ticket_attachments_redaction_consistency",
                "(\"State\" = 'active' AND \"StorageObjectId\" IS NOT NULL AND \"RedactedAtUtc\" IS NULL) OR (\"State\" = 'redacted' AND \"RedactedAtUtc\" IS NOT NULL)");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.MessageId).IsRequired();
        builder.Property(x => x.TicketId).IsRequired();
        builder.Property(x => x.StorageObjectId).HasColumnType("text");
        builder.Property(x => x.MimeType).HasColumnType("text").IsRequired();
        builder.Property(x => x.SizeBytes).IsRequired();
        builder.Property(x => x.OriginalFilename).HasColumnType("text").IsRequired();
        builder.Property(x => x.State).HasColumnType("text").IsRequired().HasDefaultValue("active");
        builder.Property(x => x.RedactedAtUtc);
        builder.Property(x => x.RedactedByRole).HasColumnType("text");
        builder.Property(x => x.RedactionReasonNote).HasColumnType("text");
        builder.Property(x => x.CreatedAtUtc).IsRequired();

        builder.HasIndex(x => x.TicketId).HasDatabaseName("IX_ticket_attachments_ticket");
        builder.HasIndex(x => x.MessageId).HasDatabaseName("IX_ticket_attachments_message");

        builder.HasOne<TicketMessage>()
            .WithMany()
            .HasForeignKey(x => x.MessageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

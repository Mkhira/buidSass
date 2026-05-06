using BackendApi.Modules.Support.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Support.Persistence.Configurations;

public sealed class TicketMessageConfiguration : IEntityTypeConfiguration<TicketMessage>
{
    public void Configure(EntityTypeBuilder<TicketMessage> builder)
    {
        builder.ToTable("ticket_messages", "support", t =>
        {
            t.HasCheckConstraint("CK_ticket_messages_kind",
                "\"Kind\" IN ('customer_reply','agent_reply','internal_note','system_event')");
            t.HasCheckConstraint("CK_ticket_messages_body_locale",
                "\"BodyLocale\" IS NULL OR \"BodyLocale\" IN ('ar','en')");
            t.HasCheckConstraint("CK_ticket_messages_body_len",
                "\"Body\" IS NULL OR char_length(\"Body\") BETWEEN 1 AND 8000");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.TicketId).IsRequired();
        builder.Property(x => x.Kind).HasColumnType("text").IsRequired();
        builder.Property(x => x.ActorId);
        builder.Property(x => x.ActorRole).HasColumnType("text").IsRequired();
        builder.Property(x => x.Body).HasColumnType("text");
        builder.Property(x => x.BodyLocale).HasColumnType("text");
        builder.Property(x => x.LeadIntervention).IsRequired();
        builder.Property(x => x.RedactedAtUtc);
        builder.Property(x => x.RedactedByRole).HasColumnType("text");
        builder.Property(x => x.OriginatingRedactionRequestTicketId);
        builder.Property(x => x.CreatedAtUtc).IsRequired();

        builder.HasIndex(x => new { x.TicketId, x.CreatedAtUtc })
            .HasDatabaseName("IX_ticket_messages_ticket");
        builder.HasIndex(x => new { x.TicketId, x.Kind })
            .HasDatabaseName("IX_ticket_messages_ticket_kind");

        builder.HasOne<SupportTicket>()
            .WithMany()
            .HasForeignKey(x => x.TicketId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

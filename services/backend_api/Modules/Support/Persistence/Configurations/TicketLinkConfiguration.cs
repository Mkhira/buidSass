using BackendApi.Modules.Support.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Support.Persistence.Configurations;

public sealed class TicketLinkConfiguration : IEntityTypeConfiguration<TicketLink>
{
    public void Configure(EntityTypeBuilder<TicketLink> builder)
    {
        builder.ToTable("ticket_links", "support", t =>
        {
            t.HasCheckConstraint("CK_ticket_links_kind",
                "\"Kind\" IN ('order','order_line','return_request','quote','review','verification')");
            t.HasCheckConstraint("CK_ticket_links_created_via",
                "\"CreatedVia\" IN ('submission','conversion','lead_link')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.TicketId).IsRequired();
        builder.Property(x => x.Kind).HasColumnType("text").IsRequired();
        builder.Property(x => x.LinkedEntityId).IsRequired();
        builder.Property(x => x.CreatedVia).HasColumnType("text").IsRequired();
        builder.Property(x => x.IdempotencyKey).HasColumnType("text");
        builder.Property(x => x.CreatedAtUtc).IsRequired();

        builder.HasIndex(x => x.TicketId).HasDatabaseName("IX_ticket_links_ticket");
        builder.HasIndex(x => new { x.Kind, x.LinkedEntityId })
            .HasDatabaseName("IX_ticket_links_lookup");
        // Per-conversion idempotency: same (ticket_id, idempotency_key) returns the existing row.
        builder.HasIndex(x => new { x.TicketId, x.IdempotencyKey })
            .HasDatabaseName("UX_ticket_links_idempotency_key")
            .IsUnique()
            .HasFilter("\"IdempotencyKey\" IS NOT NULL");

        builder.HasOne<SupportTicket>()
            .WithMany()
            .HasForeignKey(x => x.TicketId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

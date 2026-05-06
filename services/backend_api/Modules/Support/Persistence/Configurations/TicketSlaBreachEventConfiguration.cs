using BackendApi.Modules.Support.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Support.Persistence.Configurations;

public sealed class TicketSlaBreachEventConfiguration : IEntityTypeConfiguration<TicketSlaBreachEvent>
{
    public void Configure(EntityTypeBuilder<TicketSlaBreachEvent> builder)
    {
        builder.ToTable("ticket_sla_breach_events", "support", t =>
        {
            t.HasCheckConstraint("CK_breach_events_kind",
                "\"BreachKind\" IN ('first_response','resolution')");
        });

        // Composite PK gives natural reopen-tolerance per data-model §4.
        builder.HasKey(x => new { x.TicketId, x.BreachKind, x.DetectedAtUtc });

        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.TicketId).IsRequired();
        builder.Property(x => x.BreachKind).HasColumnType("text").IsRequired();
        builder.Property(x => x.DetectedAtUtc).IsRequired();
        builder.Property(x => x.TargetDueUtc).IsRequired();
        builder.Property(x => x.EventEmitted).IsRequired();
        builder.Property(x => x.SupersededByEventId);

        builder.HasIndex(x => new { x.TicketId, x.BreachKind })
            .HasDatabaseName("IX_breach_events_ticket_kind");

        // Surrogate id is unique to support superseded-by lookups.
        builder.HasIndex(x => x.Id)
            .HasDatabaseName("UX_breach_events_surrogate_id")
            .IsUnique();

        builder.HasOne<SupportTicket>()
            .WithMany()
            .HasForeignKey(x => x.TicketId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

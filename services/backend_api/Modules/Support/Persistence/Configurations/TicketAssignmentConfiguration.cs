using BackendApi.Modules.Support.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Support.Persistence.Configurations;

public sealed class TicketAssignmentConfiguration : IEntityTypeConfiguration<TicketAssignment>
{
    public void Configure(EntityTypeBuilder<TicketAssignment> builder)
    {
        builder.ToTable("ticket_assignments", "support", t =>
        {
            t.HasCheckConstraint("CK_ticket_assignments_kind",
                "\"AssignmentKind\" IN ('self_claim','auto_assignment','lead_reassignment','reclaim_after_offboard')");
            t.HasCheckConstraint("CK_ticket_assignments_superseded_reason",
                "\"SupersededReason\" IS NULL OR \"SupersededReason\" IN ('reassigned','reclaimed_offboard','reopened_back_to_queue')");
            t.HasCheckConstraint("CK_ticket_assignments_supersede_consistency",
                "(\"SupersededAtUtc\" IS NULL AND \"SupersededReason\" IS NULL) OR (\"SupersededAtUtc\" IS NOT NULL AND \"SupersededReason\" IS NOT NULL)");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.TicketId).IsRequired();
        builder.Property(x => x.AgentId).IsRequired();
        builder.Property(x => x.AssignmentKind).HasColumnType("text").IsRequired();
        builder.Property(x => x.AssignedByActorId);
        builder.Property(x => x.JustificationNote).HasColumnType("text");
        builder.Property(x => x.AssignedAtUtc).IsRequired();
        builder.Property(x => x.SupersededAtUtc);
        builder.Property(x => x.SupersededReason).HasColumnType("text");

        builder.HasIndex(x => new { x.TicketId, x.AssignedAtUtc })
            .HasDatabaseName("IX_ticket_assignments_ticket");

        // Partial unique: at most ONE active (non-superseded) assignment per ticket.
        builder.HasIndex(x => x.TicketId)
            .HasDatabaseName("UX_ticket_assignments_active_per_ticket")
            .IsUnique()
            .HasFilter("\"SupersededAtUtc\" IS NULL");

        builder.HasOne<SupportTicket>()
            .WithMany()
            .HasForeignKey(x => x.TicketId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

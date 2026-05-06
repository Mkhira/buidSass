using BackendApi.Modules.Support.Entities;
using BackendApi.Modules.Support.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Support.Persistence.Configurations;

public sealed class SupportTicketConfiguration : IEntityTypeConfiguration<SupportTicket>
{
    public void Configure(EntityTypeBuilder<SupportTicket> builder)
    {
        builder.ToTable("tickets", "support", t =>
        {
            t.HasCheckConstraint("CK_tickets_market_code", "\"MarketCode\" IN ('SA','EG')");
            t.HasCheckConstraint("CK_tickets_locale", "\"Locale\" IN ('ar','en')");
            t.HasCheckConstraint("CK_tickets_state",
                "\"State\" IN ('open','in_progress','waiting_customer','resolved','closed')");
            t.HasCheckConstraint("CK_tickets_priority",
                "\"Priority\" IN ('low','normal','high','urgent')");
            t.HasCheckConstraint("CK_tickets_category",
                "\"Category\" IN ('order_issue','payment_issue','shipping_issue','product_defect','return_refund_request','quote_question','account_verification','general_question','review_dispute','verification_query','redaction_request')");
            t.HasCheckConstraint("CK_tickets_subject_len",
                "char_length(\"Subject\") BETWEEN 1 AND 150");
            t.HasCheckConstraint("CK_tickets_body_len",
                "char_length(\"Body\") BETWEEN 1 AND 8000");
            t.HasCheckConstraint("CK_tickets_linked_kind_consistency",
                "(\"LinkedEntityKind\" IS NULL AND \"LinkedEntityId\" IS NULL) OR (\"LinkedEntityKind\" IS NOT NULL AND \"LinkedEntityId\" IS NOT NULL)");
            t.HasCheckConstraint("CK_tickets_linked_entity_kind",
                "\"LinkedEntityKind\" IS NULL OR \"LinkedEntityKind\" IN ('order','order_line','return_request','quote','review','verification')");
            t.HasCheckConstraint("CK_tickets_reopen_count_nonneg",
                "\"ReopenCount\" >= 0");
            t.HasCheckConstraint("CK_tickets_sla_targets_positive",
                "\"FirstResponseTargetMinutesSnapshot\" > 0 AND \"ResolutionTargetMinutesSnapshot\" > 0");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.CustomerId).IsRequired();
        builder.Property(x => x.CompanyId);
        builder.Property(x => x.MarketCode).HasColumnType("text").IsRequired();
        builder.Property(x => x.Locale).HasColumnType("text").IsRequired();
        builder.Property(x => x.Category).HasColumnType("text").IsRequired();
        builder.Property(x => x.Priority).HasColumnType("text").IsRequired();
        builder.Property(x => x.State).HasColumnType("text").IsRequired();
        builder.Property(x => x.Subject).HasColumnType("text").IsRequired();
        builder.Property(x => x.Body).HasColumnType("text").IsRequired();
        builder.Property(x => x.LinkedEntityKind).HasColumnType("text");
        builder.Property(x => x.LinkedEntityId);
        builder.Property(x => x.VendorId);
        builder.Property(x => x.AssignedAgentId);
        builder.Property(x => x.FirstResponseTargetMinutesSnapshot).IsRequired();
        builder.Property(x => x.ResolutionTargetMinutesSnapshot).IsRequired();
        builder.Property(x => x.FirstResponseDueUtc).IsRequired();
        builder.Property(x => x.ResolutionDueUtc).IsRequired();
        builder.Property(x => x.BreachAcknowledgedAtFirstResponse);
        builder.Property(x => x.BreachAcknowledgedAtResolution);
        builder.Property(x => x.ReopenCount).IsRequired();
        builder.Property(x => x.ReopenedAtUtc);
        builder.Property(x => x.ResolvedAtUtc);
        builder.Property(x => x.ClosedAtUtc);
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();
        builder.Property(x => x.Xmin).IsRowVersion().HasColumnName("xmin");

        builder.HasIndex(x => new { x.MarketCode, x.State, x.FirstResponseDueUtc, x.CreatedAtUtc })
            .HasDatabaseName("IX_tickets_queue");
        builder.HasIndex(x => new { x.CustomerId, x.CreatedAtUtc })
            .HasDatabaseName("IX_tickets_customer");
        builder.HasIndex(x => new { x.AssignedAgentId, x.State })
            .HasDatabaseName("IX_tickets_assigned_agent")
            .HasFilter("\"State\" IN ('in_progress','waiting_customer')");
        builder.HasIndex(x => new { x.LinkedEntityKind, x.LinkedEntityId })
            .HasDatabaseName("IX_tickets_linked_entity");
        builder.HasIndex(x => x.VendorId)
            .HasDatabaseName("IX_tickets_vendor")
            .HasFilter("\"VendorId\" IS NOT NULL");
        builder.HasIndex(x => new { x.CompanyId, x.State })
            .HasDatabaseName("IX_tickets_company")
            .HasFilter("\"CompanyId\" IS NOT NULL");
        builder.HasIndex(x => new { x.State, x.FirstResponseDueUtc, x.ResolutionDueUtc })
            .HasDatabaseName("IX_tickets_breach_scan")
            .HasFilter("\"State\" NOT IN ('closed')");
    }
}

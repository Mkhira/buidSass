using BackendApi.Modules.Returns.Entities;
using BackendApi.Modules.Returns.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Returns.Persistence.Configurations;

public sealed class ReturnRequestConfiguration : IEntityTypeConfiguration<ReturnRequest>
{
    public void Configure(EntityTypeBuilder<ReturnRequest> builder)
    {
        builder.ToTable("return_requests", "returns", t =>
        {
            t.HasCheckConstraint("CK_returns_return_requests_state_enum",
                "\"State\" IN ('pending_review','approved','approved_partial','rejected','received','inspected','refunded','refund_failed')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ReturnNumber).HasColumnType("text").IsRequired();
        builder.HasIndex(x => x.ReturnNumber).IsUnique();
        builder.Property(x => x.MarketCode).HasColumnType("citext").IsRequired();
        builder.Property(x => x.State).HasColumnType("citext").IsRequired()
            .HasDefaultValue(ReturnStateMachine.PendingReview);
        builder.Property(x => x.ReasonCode).HasColumnType("citext").IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasMany(x => x.Lines).WithOne(l => l.ReturnRequest!)
            .HasForeignKey(l => l.ReturnRequestId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Photos).WithOne(p => p.ReturnRequest!)
            .HasForeignKey(p => p.ReturnRequestId).OnDelete(DeleteBehavior.SetNull);
        builder.HasMany(x => x.Inspections).WithOne(i => i.ReturnRequest!)
            .HasForeignKey(i => i.ReturnRequestId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Refunds).WithOne(r => r.ReturnRequest!)
            .HasForeignKey(r => r.ReturnRequestId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.OrderId).HasDatabaseName("IX_returns_return_requests_order");
        builder.HasIndex(x => new { x.AccountId, x.SubmittedAt })
            .HasDatabaseName("IX_returns_return_requests_account_submitted");
        builder.HasIndex(x => new { x.MarketCode, x.State, x.SubmittedAt })
            .HasDatabaseName("IX_returns_return_requests_market_state_submitted");
    }
}

public sealed class ReturnLineConfiguration : IEntityTypeConfiguration<ReturnLine>
{
    public void Configure(EntityTypeBuilder<ReturnLine> builder)
    {
        builder.ToTable("return_lines", "returns", t =>
        {
            t.HasCheckConstraint("CK_returns_return_lines_requested_qty_positive",
                "\"RequestedQty\" > 0");
            t.HasCheckConstraint("CK_returns_return_lines_approved_qty_bounds",
                "\"ApprovedQty\" IS NULL OR (\"ApprovedQty\" >= 0 AND \"ApprovedQty\" <= \"RequestedQty\")");
            t.HasCheckConstraint("CK_returns_return_lines_received_qty_bounds",
                "\"ReceivedQty\" IS NULL OR (\"ReceivedQty\" >= 0 AND \"ReceivedQty\" <= \"ApprovedQty\")");
            t.HasCheckConstraint("CK_returns_return_lines_inspection_qty_balance",
                "(\"SellableQty\" IS NULL AND \"DefectiveQty\" IS NULL) "
                + "OR (\"SellableQty\" + \"DefectiveQty\" = \"ReceivedQty\")");
            t.HasCheckConstraint("CK_returns_return_lines_unit_price_non_negative",
                "\"UnitPriceMinor\" >= 0");
            t.HasCheckConstraint("CK_returns_return_lines_tax_rate_bounds",
                "\"TaxRateBp\" >= 0 AND \"TaxRateBp\" <= 10000");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.LineReasonCode).HasColumnType("citext");
        builder.HasIndex(x => x.ReturnRequestId);
        builder.HasIndex(x => x.OrderLineId);
    }
}

public sealed class InspectionConfiguration : IEntityTypeConfiguration<Inspection>
{
    public void Configure(EntityTypeBuilder<Inspection> builder)
    {
        builder.ToTable("inspections", "returns", t =>
        {
            t.HasCheckConstraint("CK_returns_inspections_state_enum",
                "\"State\" IN ('pending','in_progress','complete')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.State).HasColumnType("citext").IsRequired()
            .HasDefaultValue(InspectionStateMachine.Pending);
        builder.HasMany(x => x.Lines).WithOne(l => l.Inspection!)
            .HasForeignKey(l => l.InspectionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.ReturnRequestId);
    }
}

public sealed class InspectionLineConfiguration : IEntityTypeConfiguration<InspectionLine>
{
    public void Configure(EntityTypeBuilder<InspectionLine> builder)
    {
        builder.ToTable("inspection_lines", "returns", t =>
        {
            t.HasCheckConstraint("CK_returns_inspection_lines_qty_non_negative",
                "\"SellableQty\" >= 0 AND \"DefectiveQty\" >= 0");
        });
        builder.HasKey(x => new { x.InspectionId, x.ReturnLineId });
        builder.Property(x => x.PhotosJson).HasColumnType("jsonb");
    }
}

public sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.ToTable("refunds", "returns", t =>
        {
            t.HasCheckConstraint("CK_returns_refunds_state_enum",
                "\"State\" IN ('pending','in_progress','pending_manual_transfer','completed','failed')");
            t.HasCheckConstraint("CK_returns_refunds_amount_non_negative",
                "\"AmountMinor\" >= 0");
            t.HasCheckConstraint("CK_returns_refunds_attempts_non_negative",
                "\"Attempts\" >= 0");
            t.HasCheckConstraint("CK_returns_refunds_restocking_fee_non_negative",
                "\"RestockingFeeMinor\" >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ProviderId).HasColumnType("citext");
        builder.Property(x => x.Currency).HasColumnType("citext").IsRequired();
        builder.Property(x => x.State).HasColumnType("citext").IsRequired()
            .HasDefaultValue(RefundStateMachine.Pending);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasMany(x => x.Lines).WithOne(l => l.Refund!)
            .HasForeignKey(l => l.RefundId).OnDelete(DeleteBehavior.Cascade);

        // Spec 013 SC-005: at most one in-flight or completed refund per (return_request_id).
        // Without this guard a duplicate `issue-refund` click could create a second row before
        // the first commits — the partial unique index serialises that race at the DB.
        builder.HasIndex(x => x.ReturnRequestId)
            .IsUnique()
            .HasFilter("\"State\" IN ('pending','in_progress','pending_manual_transfer','completed')")
            .HasDatabaseName("IX_returns_refunds_request_active_unique");
        builder.HasIndex(x => x.State).HasDatabaseName("IX_returns_refunds_state");
        builder.HasIndex(x => x.NextRetryAt)
            .HasFilter("\"State\" = 'failed' AND \"NextRetryAt\" IS NOT NULL")
            .HasDatabaseName("IX_returns_refunds_retry_pending");
    }
}

public sealed class RefundLineConfiguration : IEntityTypeConfiguration<RefundLine>
{
    public void Configure(EntityTypeBuilder<RefundLine> builder)
    {
        builder.ToTable("refund_lines", "returns", t =>
        {
            t.HasCheckConstraint("CK_returns_refund_lines_qty_positive", "\"Qty\" > 0");
            t.HasCheckConstraint("CK_returns_refund_lines_amounts_non_negative",
                "\"LineSubtotalMinor\" >= 0 AND \"LineDiscountMinor\" >= 0 AND \"LineTaxMinor\" >= 0 AND \"LineAmountMinor\" >= 0");
            t.HasCheckConstraint("CK_returns_refund_lines_tax_rate_bounds",
                "\"TaxRateBp\" >= 0 AND \"TaxRateBp\" <= 10000");
        });
        builder.HasKey(x => new { x.RefundId, x.ReturnLineId });
    }
}

public sealed class ReturnPhotoConfiguration : IEntityTypeConfiguration<ReturnPhoto>
{
    public void Configure(EntityTypeBuilder<ReturnPhoto> builder)
    {
        builder.ToTable("return_photos", "returns", t =>
        {
            t.HasCheckConstraint("CK_returns_return_photos_size_positive", "\"SizeBytes\" > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Mime).HasColumnType("citext").IsRequired();
        builder.HasIndex(x => x.ReturnRequestId);
        builder.HasIndex(x => x.AccountId);
    }
}

public sealed class ReturnPolicyConfiguration : IEntityTypeConfiguration<ReturnPolicy>
{
    public void Configure(EntityTypeBuilder<ReturnPolicy> builder)
    {
        builder.ToTable("return_policies", "returns", t =>
        {
            t.HasCheckConstraint("CK_returns_return_policies_window_non_negative",
                "\"ReturnWindowDays\" >= 0");
            t.HasCheckConstraint("CK_returns_return_policies_restocking_fee_bounds",
                "\"RestockingFeeBp\" >= 0 AND \"RestockingFeeBp\" <= 10000");
        });
        builder.HasKey(x => x.MarketCode);
        builder.Property(x => x.MarketCode).HasColumnType("citext");
    }
}

public sealed class ReturnsOutboxEntryConfiguration : IEntityTypeConfiguration<ReturnsOutboxEntry>
{
    public void Configure(EntityTypeBuilder<ReturnsOutboxEntry> builder)
    {
        builder.ToTable("returns_outbox", "returns");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventType).HasColumnType("citext").IsRequired();
        builder.Property(x => x.PayloadJson).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.HasIndex(x => x.CommittedAt)
            .HasFilter("\"DispatchedAt\" IS NULL")
            .HasDatabaseName("IX_returns_outbox_pending");
        builder.HasIndex(x => x.AggregateId);
    }
}

public sealed class ReturnStateTransitionConfiguration : IEntityTypeConfiguration<ReturnStateTransition>
{
    public void Configure(EntityTypeBuilder<ReturnStateTransition> builder)
    {
        builder.ToTable("return_state_transitions", "returns", t =>
        {
            t.HasCheckConstraint("CK_returns_state_transitions_machine_enum",
                "\"Machine\" IN ('return','refund','inspection')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Machine).HasColumnType("citext").IsRequired();
        builder.Property(x => x.FromState).HasColumnType("citext").IsRequired();
        builder.Property(x => x.ToState).HasColumnType("citext").IsRequired();
        builder.Property(x => x.Trigger).HasColumnType("citext").IsRequired();
        builder.Property(x => x.ContextJson).HasColumnType("jsonb");
        builder.HasIndex(x => new { x.ReturnRequestId, x.OccurredAt })
            .HasDatabaseName("IX_returns_state_transitions_request_occurred");
    }
}

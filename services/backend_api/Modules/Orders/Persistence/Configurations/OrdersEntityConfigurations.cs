using BackendApi.Modules.Orders.Entities;
using BackendApi.Modules.Orders.Primitives.StateMachines;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Orders.Persistence.Configurations;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders", "orders", t =>
        {
            t.HasCheckConstraint("CK_orders_orders_order_state_enum",
                "\"OrderState\" IN ('placed','cancellation_pending','cancelled')");
            t.HasCheckConstraint("CK_orders_orders_payment_state_enum",
                "\"PaymentState\" IN ('authorized','captured','pending_cod','pending_bank_transfer','failed','voided','refunded','partially_refunded')");
            t.HasCheckConstraint("CK_orders_orders_fulfillment_state_enum",
                "\"FulfillmentState\" IN ('not_started','awaiting_stock','picking','packed','handed_to_carrier','delivered','cancelled')");
            t.HasCheckConstraint("CK_orders_orders_refund_state_enum",
                "\"RefundState\" IN ('none','requested','partial','full')");
            t.HasCheckConstraint("CK_orders_orders_grand_total_non_negative",
                "\"GrandTotalMinor\" >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.OrderNumber).HasColumnType("text").IsRequired();
        builder.HasIndex(x => x.OrderNumber).IsUnique();
        builder.Property(x => x.MarketCode).HasColumnType("citext").IsRequired();
        builder.Property(x => x.Currency).HasColumnType("citext").IsRequired();
        builder.Property(x => x.OrderState).HasColumnType("citext").IsRequired()
            .HasDefaultValue(OrderSm.Placed);
        builder.Property(x => x.PaymentState).HasColumnType("citext").IsRequired()
            .HasDefaultValue(PaymentSm.Authorized);
        builder.Property(x => x.FulfillmentState).HasColumnType("citext").IsRequired()
            .HasDefaultValue(FulfillmentSm.NotStarted);
        builder.Property(x => x.RefundState).HasColumnType("citext").IsRequired()
            .HasDefaultValue(RefundSm.None);
        builder.Property(x => x.CouponCode).HasColumnType("citext");
        builder.Property(x => x.PaymentProviderId).HasColumnType("citext");
        builder.Property(x => x.ShippingAddressJson).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(x => x.BillingAddressJson).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(x => x.OwnerId).HasColumnType("citext").IsRequired().HasDefaultValue("platform");
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasMany(x => x.Lines).WithOne(l => l.Order!).HasForeignKey(l => l.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Shipments).WithOne(s => s.Order!).HasForeignKey(s => s.OrderId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.AccountId, x.PlacedAt }).HasDatabaseName("IX_orders_orders_account_placed");
        builder.HasIndex(x => new { x.MarketCode, x.PlacedAt }).HasDatabaseName("IX_orders_orders_market_placed");
        builder.HasIndex(x => x.PaymentState).HasDatabaseName("IX_orders_orders_payment_state");
        builder.HasIndex(x => x.FulfillmentState).HasDatabaseName("IX_orders_orders_fulfillment_state");
        builder.HasIndex(x => x.CheckoutSessionId).HasDatabaseName("IX_orders_orders_checkout_session");
    }
}

public sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        builder.ToTable("order_lines", "orders", t =>
        {
            t.HasCheckConstraint("CK_orders_order_lines_qty_positive", "\"Qty\" > 0");
            t.HasCheckConstraint("CK_orders_order_lines_cancelled_qty_bounds",
                "\"CancelledQty\" >= 0 AND \"CancelledQty\" <= \"Qty\"");
            t.HasCheckConstraint("CK_orders_order_lines_returned_qty_bounds",
                "\"ReturnedQty\" >= 0 AND (\"ReturnedQty\" + \"CancelledQty\") <= \"Qty\"");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Sku).HasColumnType("citext").IsRequired();
        builder.Property(x => x.AttributesJson).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.ProductId);
    }
}

public sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.ToTable("shipments", "orders", t =>
        {
            t.HasCheckConstraint("CK_orders_shipments_state_enum",
                "\"State\" IN ('created','handed_to_carrier','in_transit','out_for_delivery','delivered','returned','failed')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ProviderId).HasColumnType("citext").IsRequired();
        builder.Property(x => x.MethodCode).HasColumnType("citext").IsRequired();
        builder.Property(x => x.State).HasColumnType("citext").IsRequired()
            .HasDefaultValue(Shipment.StateCreated);
        builder.Property(x => x.PayloadJson).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.HasMany(x => x.Lines).WithOne(l => l.Shipment!).HasForeignKey(l => l.ShipmentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => new { x.ProviderId, x.TrackingNumber });
    }
}

public sealed class ShipmentLineConfiguration : IEntityTypeConfiguration<ShipmentLine>
{
    public void Configure(EntityTypeBuilder<ShipmentLine> builder)
    {
        builder.ToTable("shipment_lines", "orders", t =>
        {
            t.HasCheckConstraint("CK_orders_shipment_lines_qty_positive", "\"Qty\" > 0");
        });
        builder.HasKey(x => new { x.ShipmentId, x.OrderLineId });
        builder.HasOne(x => x.OrderLine).WithMany().HasForeignKey(x => x.OrderLineId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class OrderStateTransitionConfiguration : IEntityTypeConfiguration<OrderStateTransition>
{
    public void Configure(EntityTypeBuilder<OrderStateTransition> builder)
    {
        builder.ToTable("order_state_transitions", "orders", t =>
        {
            t.HasCheckConstraint("CK_orders_state_transitions_machine_enum",
                "\"Machine\" IN ('order','payment','fulfillment','refund')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Machine).HasColumnType("citext").IsRequired();
        builder.Property(x => x.FromState).HasColumnType("citext").IsRequired();
        builder.Property(x => x.ToState).HasColumnType("citext").IsRequired();
        builder.Property(x => x.Trigger).HasColumnType("citext").IsRequired();
        builder.Property(x => x.ContextJson).HasColumnType("jsonb");
        builder.HasIndex(x => new { x.OrderId, x.OccurredAt }).HasDatabaseName("IX_orders_state_transitions_order_occurred");
    }
}

public sealed class QuotationConfiguration : IEntityTypeConfiguration<Quotation>
{
    public void Configure(EntityTypeBuilder<Quotation> builder)
    {
        builder.ToTable("quotations", "orders", t =>
        {
            t.HasCheckConstraint("CK_orders_quotations_status_enum",
                "\"Status\" IN ('draft','active','accepted','rejected','expired','converted')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.QuoteNumber).HasColumnType("text").IsRequired();
        builder.HasIndex(x => x.QuoteNumber).IsUnique();
        builder.Property(x => x.MarketCode).HasColumnType("citext").IsRequired();
        builder.Property(x => x.Status).HasColumnType("citext").IsRequired().HasDefaultValue(Quotation.StatusDraft);
        builder.HasMany(x => x.Lines).WithOne(l => l.Quotation!).HasForeignKey(l => l.QuotationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.AccountId, x.Status });
        builder.HasIndex(x => x.ValidUntil);
    }
}

public sealed class QuotationLineConfiguration : IEntityTypeConfiguration<QuotationLine>
{
    public void Configure(EntityTypeBuilder<QuotationLine> builder)
    {
        builder.ToTable("quotation_lines", "orders", t =>
        {
            t.HasCheckConstraint("CK_orders_quotation_lines_qty_positive", "\"Qty\" > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Sku).HasColumnType("citext").IsRequired();
        builder.Property(x => x.AttributesJson).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.HasIndex(x => x.QuotationId);
    }
}

public sealed class OrdersOutboxEntryConfiguration : IEntityTypeConfiguration<OrdersOutboxEntry>
{
    public void Configure(EntityTypeBuilder<OrdersOutboxEntry> builder)
    {
        builder.ToTable("orders_outbox", "orders");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventType).HasColumnType("citext").IsRequired();
        builder.Property(x => x.PayloadJson).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        // Pending-dispatch partial index (data-model.md §8 — "Partial index WHERE dispatched_at IS NULL").
        builder.HasIndex(x => x.CommittedAt)
            .HasFilter("\"DispatchedAt\" IS NULL")
            .HasDatabaseName("IX_orders_outbox_pending");
        builder.HasIndex(x => x.AggregateId);
    }
}

public sealed class CancellationPolicyRowConfiguration : IEntityTypeConfiguration<CancellationPolicyRow>
{
    public void Configure(EntityTypeBuilder<CancellationPolicyRow> builder)
    {
        builder.ToTable("cancellation_policies", "orders", t =>
        {
            t.HasCheckConstraint("CK_orders_cancellation_policies_hours_non_negative",
                "\"CapturedCancelHours\" >= 0");
        });
        builder.HasKey(x => x.MarketCode);
        builder.Property(x => x.MarketCode).HasColumnType("citext");
    }
}

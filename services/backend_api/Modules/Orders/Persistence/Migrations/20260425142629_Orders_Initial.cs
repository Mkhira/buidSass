using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BackendApi.Modules.Orders.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Orders_Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "orders");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "cancellation_policies",
                schema: "orders",
                columns: table => new
                {
                    MarketCode = table.Column<string>(type: "citext", nullable: false),
                    AuthorizedCancelAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    CapturedCancelHours = table.Column<int>(type: "integer", nullable: false),
                    UpdatedByAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cancellation_policies", x => x.MarketCode);
                    table.CheckConstraint("CK_orders_cancellation_policies_hours_non_negative", "\"CapturedCancelHours\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "order_state_transitions",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Machine = table.Column<string>(type: "citext", nullable: false),
                    FromState = table.Column<string>(type: "citext", nullable: false),
                    ToState = table.Column<string>(type: "citext", nullable: false),
                    ActorAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Trigger = table.Column<string>(type: "citext", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ContextJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_state_transitions", x => x.Id);
                    table.CheckConstraint("CK_orders_state_transitions_machine_enum", "\"Machine\" IN ('order','payment','fulfillment','refund')");
                });

            migrationBuilder.CreateTable(
                name: "orders",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderNumber = table.Column<string>(type: "text", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketCode = table.Column<string>(type: "citext", nullable: false),
                    Currency = table.Column<string>(type: "citext", nullable: false),
                    SubtotalMinor = table.Column<long>(type: "bigint", nullable: false),
                    DiscountMinor = table.Column<long>(type: "bigint", nullable: false),
                    TaxMinor = table.Column<long>(type: "bigint", nullable: false),
                    ShippingMinor = table.Column<long>(type: "bigint", nullable: false),
                    GrandTotalMinor = table.Column<long>(type: "bigint", nullable: false),
                    PriceExplanationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CouponCode = table.Column<string>(type: "citext", nullable: true),
                    ShippingAddressJson = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    BillingAddressJson = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    B2bPoNumber = table.Column<string>(type: "text", nullable: true),
                    B2bReference = table.Column<string>(type: "text", nullable: true),
                    B2bNotes = table.Column<string>(type: "text", nullable: true),
                    B2bRequestedDeliveryFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    B2bRequestedDeliveryTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrderState = table.Column<string>(type: "citext", nullable: false, defaultValue: "placed"),
                    PaymentState = table.Column<string>(type: "citext", nullable: false, defaultValue: "authorized"),
                    FulfillmentState = table.Column<string>(type: "citext", nullable: false, defaultValue: "not_started"),
                    RefundState = table.Column<string>(type: "citext", nullable: false, defaultValue: "none"),
                    PlacedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    QuotationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CheckoutSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaymentProviderId = table.Column<string>(type: "citext", nullable: true),
                    PaymentProviderTxnId = table.Column<string>(type: "text", nullable: true),
                    OwnerId = table.Column<string>(type: "citext", nullable: false, defaultValue: "platform"),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orders", x => x.Id);
                    table.CheckConstraint("CK_orders_orders_fulfillment_state_enum", "\"FulfillmentState\" IN ('not_started','awaiting_stock','picking','packed','handed_to_carrier','delivered','cancelled')");
                    table.CheckConstraint("CK_orders_orders_grand_total_non_negative", "\"GrandTotalMinor\" >= 0");
                    table.CheckConstraint("CK_orders_orders_order_state_enum", "\"OrderState\" IN ('placed','cancellation_pending','cancelled')");
                    table.CheckConstraint("CK_orders_orders_payment_state_enum", "\"PaymentState\" IN ('authorized','captured','pending_cod','pending_bank_transfer','failed','voided','refunded','partially_refunded')");
                    table.CheckConstraint("CK_orders_orders_refund_state_enum", "\"RefundState\" IN ('none','requested','partial','full')");
                });

            migrationBuilder.CreateTable(
                name: "orders_outbox",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventType = table.Column<string>(type: "citext", nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    CommittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DispatchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orders_outbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "quotations",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QuoteNumber = table.Column<string>(type: "text", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketCode = table.Column<string>(type: "citext", nullable: false),
                    Status = table.Column<string>(type: "citext", nullable: false, defaultValue: "draft"),
                    PriceExplanationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValidUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConvertedOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quotations", x => x.Id);
                    table.CheckConstraint("CK_orders_quotations_status_enum", "\"Status\" IN ('draft','active','accepted','rejected','expired','converted')");
                });

            migrationBuilder.CreateTable(
                name: "order_lines",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "citext", nullable: false),
                    NameAr = table.Column<string>(type: "text", nullable: false),
                    NameEn = table.Column<string>(type: "text", nullable: false),
                    Qty = table.Column<int>(type: "integer", nullable: false),
                    UnitPriceMinor = table.Column<long>(type: "bigint", nullable: false),
                    LineDiscountMinor = table.Column<long>(type: "bigint", nullable: false),
                    LineTaxMinor = table.Column<long>(type: "bigint", nullable: false),
                    LineTotalMinor = table.Column<long>(type: "bigint", nullable: false),
                    Restricted = table.Column<bool>(type: "boolean", nullable: false),
                    AttributesJson = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    CancelledQty = table.Column<int>(type: "integer", nullable: false),
                    ReturnedQty = table.Column<int>(type: "integer", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_lines", x => x.Id);
                    table.CheckConstraint("CK_orders_order_lines_cancelled_qty_bounds", "\"CancelledQty\" >= 0 AND \"CancelledQty\" <= \"Qty\"");
                    table.CheckConstraint("CK_orders_order_lines_qty_positive", "\"Qty\" > 0");
                    table.CheckConstraint("CK_orders_order_lines_returned_qty_bounds", "\"ReturnedQty\" >= 0 AND (\"ReturnedQty\" + \"CancelledQty\") <= \"Qty\"");
                    table.ForeignKey(
                        name: "FK_order_lines_orders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "orders",
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shipments",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderId = table.Column<string>(type: "citext", nullable: false),
                    MethodCode = table.Column<string>(type: "citext", nullable: false),
                    TrackingNumber = table.Column<string>(type: "text", nullable: true),
                    CarrierLabelUrl = table.Column<string>(type: "text", nullable: true),
                    EtaFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EtaTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    State = table.Column<string>(type: "citext", nullable: false, defaultValue: "created"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    HandedToCarrierAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipments", x => x.Id);
                    table.CheckConstraint("CK_orders_shipments_state_enum", "\"State\" IN ('created','handed_to_carrier','in_transit','out_for_delivery','delivered','returned','failed')");
                    table.ForeignKey(
                        name: "FK_shipments_orders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "orders",
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "quotation_lines",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QuotationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "citext", nullable: false),
                    NameAr = table.Column<string>(type: "text", nullable: false),
                    NameEn = table.Column<string>(type: "text", nullable: false),
                    Qty = table.Column<int>(type: "integer", nullable: false),
                    UnitPriceMinor = table.Column<long>(type: "bigint", nullable: false),
                    LineDiscountMinor = table.Column<long>(type: "bigint", nullable: false),
                    LineTaxMinor = table.Column<long>(type: "bigint", nullable: false),
                    LineTotalMinor = table.Column<long>(type: "bigint", nullable: false),
                    Restricted = table.Column<bool>(type: "boolean", nullable: false),
                    AttributesJson = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quotation_lines", x => x.Id);
                    table.CheckConstraint("CK_orders_quotation_lines_qty_positive", "\"Qty\" > 0");
                    table.ForeignKey(
                        name: "FK_quotation_lines_quotations_QuotationId",
                        column: x => x.QuotationId,
                        principalSchema: "orders",
                        principalTable: "quotations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shipment_lines",
                schema: "orders",
                columns: table => new
                {
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Qty = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipment_lines", x => new { x.ShipmentId, x.OrderLineId });
                    table.CheckConstraint("CK_orders_shipment_lines_qty_positive", "\"Qty\" > 0");
                    table.ForeignKey(
                        name: "FK_shipment_lines_order_lines_OrderLineId",
                        column: x => x.OrderLineId,
                        principalSchema: "orders",
                        principalTable: "order_lines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_shipment_lines_shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalSchema: "orders",
                        principalTable: "shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_order_lines_OrderId",
                schema: "orders",
                table: "order_lines",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_order_lines_ProductId",
                schema: "orders",
                table: "order_lines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_orders_state_transitions_order_occurred",
                schema: "orders",
                table: "order_state_transitions",
                columns: new[] { "OrderId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_orders_OrderNumber",
                schema: "orders",
                table: "orders",
                column: "OrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_orders_orders_account_placed",
                schema: "orders",
                table: "orders",
                columns: new[] { "AccountId", "PlacedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_orders_orders_checkout_session",
                schema: "orders",
                table: "orders",
                column: "CheckoutSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_orders_orders_fulfillment_state",
                schema: "orders",
                table: "orders",
                column: "FulfillmentState");

            migrationBuilder.CreateIndex(
                name: "IX_orders_orders_market_placed",
                schema: "orders",
                table: "orders",
                columns: new[] { "MarketCode", "PlacedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_orders_orders_payment_state",
                schema: "orders",
                table: "orders",
                column: "PaymentState");

            migrationBuilder.CreateIndex(
                name: "IX_orders_outbox_AggregateId",
                schema: "orders",
                table: "orders_outbox",
                column: "AggregateId");

            migrationBuilder.CreateIndex(
                name: "IX_orders_outbox_pending",
                schema: "orders",
                table: "orders_outbox",
                column: "CommittedAt",
                filter: "\"DispatchedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_quotation_lines_QuotationId",
                schema: "orders",
                table: "quotation_lines",
                column: "QuotationId");

            migrationBuilder.CreateIndex(
                name: "IX_quotations_AccountId_Status",
                schema: "orders",
                table: "quotations",
                columns: new[] { "AccountId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_quotations_QuoteNumber",
                schema: "orders",
                table: "quotations",
                column: "QuoteNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_quotations_ValidUntil",
                schema: "orders",
                table: "quotations",
                column: "ValidUntil");

            migrationBuilder.CreateIndex(
                name: "IX_shipment_lines_OrderLineId",
                schema: "orders",
                table: "shipment_lines",
                column: "OrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_shipments_OrderId",
                schema: "orders",
                table: "shipments",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_shipments_ProviderId_TrackingNumber",
                schema: "orders",
                table: "shipments",
                columns: new[] { "ProviderId", "TrackingNumber" });

            // B3 — seed launch cancellation policies (FR-022). Idempotent: re-applied migrations
            // keep existing admin overrides via ON CONFLICT DO NOTHING.
            migrationBuilder.Sql(@"
                INSERT INTO orders.cancellation_policies
                    (""MarketCode"", ""AuthorizedCancelAllowed"", ""CapturedCancelHours"", ""UpdatedByAccountId"", ""UpdatedAt"")
                VALUES
                    ('KSA', TRUE, 24, NULL, NOW()),
                    ('EG',  TRUE, 24, NULL, NOW())
                ON CONFLICT (""MarketCode"") DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cancellation_policies",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "order_state_transitions",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "orders_outbox",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "quotation_lines",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "shipment_lines",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "quotations",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "order_lines",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "shipments",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "orders",
                schema: "orders");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BackendApi.Modules.Returns.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Returns_Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "returns");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "return_policies",
                schema: "returns",
                columns: table => new
                {
                    MarketCode = table.Column<string>(type: "citext", nullable: false),
                    ReturnWindowDays = table.Column<int>(type: "integer", nullable: false),
                    AutoApproveUnderDays = table.Column<int>(type: "integer", nullable: true),
                    RestockingFeeBp = table.Column<int>(type: "integer", nullable: false),
                    ShippingRefundOnFullOnly = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedByAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_return_policies", x => x.MarketCode);
                    table.CheckConstraint("CK_returns_return_policies_restocking_fee_bounds", "\"RestockingFeeBp\" >= 0 AND \"RestockingFeeBp\" <= 10000");
                    table.CheckConstraint("CK_returns_return_policies_window_non_negative", "\"ReturnWindowDays\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "return_requests",
                schema: "returns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnNumber = table.Column<string>(type: "text", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketCode = table.Column<string>(type: "citext", nullable: false),
                    State = table.Column<string>(type: "citext", nullable: false, defaultValue: "pending_review"),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReasonCode = table.Column<string>(type: "citext", nullable: false),
                    CustomerNotes = table.Column<string>(type: "text", nullable: true),
                    AdminNotes = table.Column<string>(type: "text", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecidedByAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    ForceRefund = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_return_requests", x => x.Id);
                    table.CheckConstraint("CK_returns_return_requests_state_enum", "\"State\" IN ('pending_review','approved','approved_partial','rejected','received','inspected','refunded','refund_failed')");
                });

            migrationBuilder.CreateTable(
                name: "returns_outbox",
                schema: "returns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MarketCode = table.Column<string>(type: "citext", nullable: false),
                    EventType = table.Column<string>(type: "citext", nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    CommittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DispatchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DispatchAttempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_returns_outbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "inspections",
                schema: "returns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketCode = table.Column<string>(type: "citext", nullable: false),
                    InspectorAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "citext", nullable: false, defaultValue: "pending"),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inspections", x => x.Id);
                    table.CheckConstraint("CK_returns_inspections_state_enum", "\"State\" IN ('pending','in_progress','complete')");
                    table.ForeignKey(
                        name: "FK_inspections_return_requests_ReturnRequestId",
                        column: x => x.ReturnRequestId,
                        principalSchema: "returns",
                        principalTable: "return_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refunds",
                schema: "returns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketCode = table.Column<string>(type: "citext", nullable: false),
                    ProviderId = table.Column<string>(type: "citext", nullable: true),
                    CapturedTransactionId = table.Column<string>(type: "text", nullable: true),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "citext", nullable: false),
                    State = table.Column<string>(type: "citext", nullable: false, defaultValue: "pending"),
                    InitiatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NextRetryAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    GatewayRef = table.Column<string>(type: "text", nullable: true),
                    FailureReason = table.Column<string>(type: "text", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    ManualIban = table.Column<string>(type: "text", nullable: true),
                    ManualBeneficiaryName = table.Column<string>(type: "text", nullable: true),
                    ManualBankName = table.Column<string>(type: "text", nullable: true),
                    ManualReference = table.Column<string>(type: "text", nullable: true),
                    ManualConfirmedByAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    ManualConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RestockingFeeMinor = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refunds", x => x.Id);
                    table.CheckConstraint("CK_returns_refunds_amount_non_negative", "\"AmountMinor\" >= 0");
                    table.CheckConstraint("CK_returns_refunds_attempts_non_negative", "\"Attempts\" >= 0");
                    table.CheckConstraint("CK_returns_refunds_restocking_fee_non_negative", "\"RestockingFeeMinor\" >= 0");
                    table.CheckConstraint("CK_returns_refunds_state_enum", "\"State\" IN ('pending','in_progress','pending_manual_transfer','completed','failed')");
                    table.ForeignKey(
                        name: "FK_refunds_return_requests_ReturnRequestId",
                        column: x => x.ReturnRequestId,
                        principalSchema: "returns",
                        principalTable: "return_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "return_lines",
                schema: "returns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketCode = table.Column<string>(type: "citext", nullable: false),
                    OrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedQty = table.Column<int>(type: "integer", nullable: false),
                    ApprovedQty = table.Column<int>(type: "integer", nullable: true),
                    ReceivedQty = table.Column<int>(type: "integer", nullable: true),
                    SellableQty = table.Column<int>(type: "integer", nullable: true),
                    DefectiveQty = table.Column<int>(type: "integer", nullable: true),
                    LineReasonCode = table.Column<string>(type: "citext", nullable: true),
                    UnitPriceMinor = table.Column<long>(type: "bigint", nullable: false),
                    OriginalDiscountMinor = table.Column<long>(type: "bigint", nullable: false),
                    OriginalTaxMinor = table.Column<long>(type: "bigint", nullable: false),
                    TaxRateBp = table.Column<int>(type: "integer", nullable: false),
                    OriginalQty = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_return_lines", x => x.Id);
                    table.CheckConstraint("CK_returns_return_lines_approved_qty_bounds", "\"ApprovedQty\" IS NULL OR (\"ApprovedQty\" >= 0 AND \"ApprovedQty\" <= \"RequestedQty\")");
                    table.CheckConstraint("CK_returns_return_lines_inspection_qty_balance", "(\"SellableQty\" IS NULL AND \"DefectiveQty\" IS NULL) OR (\"ReceivedQty\" IS NOT NULL AND \"SellableQty\" IS NOT NULL AND \"DefectiveQty\" IS NOT NULL AND \"SellableQty\" + \"DefectiveQty\" = \"ReceivedQty\")");
                    table.CheckConstraint("CK_returns_return_lines_original_discount_non_negative", "\"OriginalDiscountMinor\" >= 0");
                    table.CheckConstraint("CK_returns_return_lines_original_qty_positive", "\"OriginalQty\" > 0");
                    table.CheckConstraint("CK_returns_return_lines_original_tax_non_negative", "\"OriginalTaxMinor\" >= 0");
                    table.CheckConstraint("CK_returns_return_lines_received_qty_bounds", "\"ReceivedQty\" IS NULL OR (\"ApprovedQty\" IS NOT NULL AND \"ReceivedQty\" >= 0 AND \"ReceivedQty\" <= \"ApprovedQty\")");
                    table.CheckConstraint("CK_returns_return_lines_requested_qty_positive", "\"RequestedQty\" > 0");
                    table.CheckConstraint("CK_returns_return_lines_tax_rate_bounds", "\"TaxRateBp\" >= 0 AND \"TaxRateBp\" <= 10000");
                    table.CheckConstraint("CK_returns_return_lines_unit_price_non_negative", "\"UnitPriceMinor\" >= 0");
                    table.ForeignKey(
                        name: "FK_return_lines_return_requests_ReturnRequestId",
                        column: x => x.ReturnRequestId,
                        principalSchema: "returns",
                        principalTable: "return_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "return_photos",
                schema: "returns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    MarketCode = table.Column<string>(type: "citext", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    BlobKey = table.Column<string>(type: "text", nullable: false),
                    Mime = table.Column<string>(type: "citext", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "text", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_return_photos", x => x.Id);
                    table.CheckConstraint("CK_returns_return_photos_size_positive", "\"SizeBytes\" > 0");
                    table.ForeignKey(
                        name: "FK_return_photos_return_requests_ReturnRequestId",
                        column: x => x.ReturnRequestId,
                        principalSchema: "returns",
                        principalTable: "return_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "return_state_transitions",
                schema: "returns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReturnRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketCode = table.Column<string>(type: "citext", nullable: false),
                    RefundId = table.Column<Guid>(type: "uuid", nullable: true),
                    InspectionId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_return_state_transitions", x => x.Id);
                    table.CheckConstraint("CK_returns_state_transitions_machine_enum", "\"Machine\" IN ('return','refund','inspection')");
                    table.ForeignKey(
                        name: "FK_return_state_transitions_return_requests_ReturnRequestId",
                        column: x => x.ReturnRequestId,
                        principalSchema: "returns",
                        principalTable: "return_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inspection_lines",
                schema: "returns",
                columns: table => new
                {
                    InspectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketCode = table.Column<string>(type: "citext", nullable: false),
                    SellableQty = table.Column<int>(type: "integer", nullable: false),
                    DefectiveQty = table.Column<int>(type: "integer", nullable: false),
                    PhotosJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inspection_lines", x => new { x.InspectionId, x.ReturnLineId });
                    table.CheckConstraint("CK_returns_inspection_lines_qty_non_negative", "\"SellableQty\" >= 0 AND \"DefectiveQty\" >= 0");
                    table.ForeignKey(
                        name: "FK_inspection_lines_inspections_InspectionId",
                        column: x => x.InspectionId,
                        principalSchema: "returns",
                        principalTable: "inspections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_inspection_lines_return_lines_ReturnLineId",
                        column: x => x.ReturnLineId,
                        principalSchema: "returns",
                        principalTable: "return_lines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "refund_lines",
                schema: "returns",
                columns: table => new
                {
                    RefundId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketCode = table.Column<string>(type: "citext", nullable: false),
                    Qty = table.Column<int>(type: "integer", nullable: false),
                    UnitPriceMinor = table.Column<long>(type: "bigint", nullable: false),
                    TaxRateBp = table.Column<int>(type: "integer", nullable: false),
                    LineSubtotalMinor = table.Column<long>(type: "bigint", nullable: false),
                    LineDiscountMinor = table.Column<long>(type: "bigint", nullable: false),
                    LineTaxMinor = table.Column<long>(type: "bigint", nullable: false),
                    LineAmountMinor = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refund_lines", x => new { x.RefundId, x.ReturnLineId });
                    table.CheckConstraint("CK_returns_refund_lines_amounts_non_negative", "\"LineSubtotalMinor\" >= 0 AND \"LineDiscountMinor\" >= 0 AND \"LineTaxMinor\" >= 0 AND \"LineAmountMinor\" >= 0");
                    table.CheckConstraint("CK_returns_refund_lines_qty_positive", "\"Qty\" > 0");
                    table.CheckConstraint("CK_returns_refund_lines_tax_rate_bounds", "\"TaxRateBp\" >= 0 AND \"TaxRateBp\" <= 10000");
                    table.ForeignKey(
                        name: "FK_refund_lines_refunds_RefundId",
                        column: x => x.RefundId,
                        principalSchema: "returns",
                        principalTable: "refunds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_refund_lines_return_lines_ReturnLineId",
                        column: x => x.ReturnLineId,
                        principalSchema: "returns",
                        principalTable: "return_lines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_inspection_lines_ReturnLineId",
                schema: "returns",
                table: "inspection_lines",
                column: "ReturnLineId");

            migrationBuilder.CreateIndex(
                name: "IX_inspections_ReturnRequestId",
                schema: "returns",
                table: "inspections",
                column: "ReturnRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_returns_inspections_market_started",
                schema: "returns",
                table: "inspections",
                columns: new[] { "MarketCode", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_refund_lines_ReturnLineId",
                schema: "returns",
                table: "refund_lines",
                column: "ReturnLineId");

            migrationBuilder.CreateIndex(
                name: "IX_returns_refunds_market_state",
                schema: "returns",
                table: "refunds",
                columns: new[] { "MarketCode", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_returns_refunds_request_active_unique",
                schema: "returns",
                table: "refunds",
                column: "ReturnRequestId",
                unique: true,
                filter: "\"State\" IN ('pending','in_progress','pending_manual_transfer','completed')");

            migrationBuilder.CreateIndex(
                name: "IX_returns_refunds_retry_pending",
                schema: "returns",
                table: "refunds",
                column: "NextRetryAt",
                filter: "\"State\" = 'failed' AND \"NextRetryAt\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_returns_refunds_state",
                schema: "returns",
                table: "refunds",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_return_lines_OrderLineId",
                schema: "returns",
                table: "return_lines",
                column: "OrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_return_lines_ReturnRequestId",
                schema: "returns",
                table: "return_lines",
                column: "ReturnRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_returns_return_lines_market_order_line",
                schema: "returns",
                table: "return_lines",
                columns: new[] { "MarketCode", "OrderLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_return_photos_AccountId",
                schema: "returns",
                table: "return_photos",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_return_photos_ReturnRequestId",
                schema: "returns",
                table: "return_photos",
                column: "ReturnRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_returns_return_photos_market_uploaded",
                schema: "returns",
                table: "return_photos",
                columns: new[] { "MarketCode", "UploadedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_return_requests_ReturnNumber",
                schema: "returns",
                table: "return_requests",
                column: "ReturnNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_returns_return_requests_account_submitted",
                schema: "returns",
                table: "return_requests",
                columns: new[] { "AccountId", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_returns_return_requests_market_state_submitted",
                schema: "returns",
                table: "return_requests",
                columns: new[] { "MarketCode", "State", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_returns_return_requests_order",
                schema: "returns",
                table: "return_requests",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_returns_state_transitions_admin_dedup",
                schema: "returns",
                table: "return_state_transitions",
                columns: new[] { "ReturnRequestId", "Machine", "Trigger", "Reason" },
                unique: true,
                filter: "\"Machine\" = 'return' AND \"Reason\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_returns_state_transitions_market_occurred",
                schema: "returns",
                table: "return_state_transitions",
                columns: new[] { "MarketCode", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_returns_state_transitions_request_occurred",
                schema: "returns",
                table: "return_state_transitions",
                columns: new[] { "ReturnRequestId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_returns_outbox_AggregateId",
                schema: "returns",
                table: "returns_outbox",
                column: "AggregateId");

            migrationBuilder.CreateIndex(
                name: "IX_returns_outbox_pending",
                schema: "returns",
                table: "returns_outbox",
                column: "CommittedAt",
                filter: "\"DispatchedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_returns_outbox_pending_per_market",
                schema: "returns",
                table: "returns_outbox",
                columns: new[] { "MarketCode", "CommittedAt" },
                filter: "\"DispatchedAt\" IS NULL");

            // B3 — seed launch return policies (FR-001 / FR-002). Idempotent via ON CONFLICT
            // DO NOTHING; mirrors spec 011's cancellation_policies seed pattern. Re-applied
            // migrations preserve admin overrides via PUT /v1/admin/return-policies/{market}.
            migrationBuilder.Sql(@"
                INSERT INTO returns.return_policies
                    (""MarketCode"", ""ReturnWindowDays"", ""AutoApproveUnderDays"", ""RestockingFeeBp"", ""ShippingRefundOnFullOnly"", ""UpdatedByAccountId"", ""UpdatedAt"")
                VALUES
                    ('KSA', 14, NULL, 0, TRUE, NULL, NOW()),
                    ('EG',   7, NULL, 0, TRUE, NULL, NOW())
                ON CONFLICT (""MarketCode"") DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inspection_lines",
                schema: "returns");

            migrationBuilder.DropTable(
                name: "refund_lines",
                schema: "returns");

            migrationBuilder.DropTable(
                name: "return_photos",
                schema: "returns");

            migrationBuilder.DropTable(
                name: "return_policies",
                schema: "returns");

            migrationBuilder.DropTable(
                name: "return_state_transitions",
                schema: "returns");

            migrationBuilder.DropTable(
                name: "returns_outbox",
                schema: "returns");

            migrationBuilder.DropTable(
                name: "inspections",
                schema: "returns");

            migrationBuilder.DropTable(
                name: "refunds",
                schema: "returns");

            migrationBuilder.DropTable(
                name: "return_lines",
                schema: "returns");

            migrationBuilder.DropTable(
                name: "return_requests",
                schema: "returns");
        }
    }
}

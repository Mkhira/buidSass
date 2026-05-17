using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Payments.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreatePaymentsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "payments");

            migrationBuilder.CreateTable(
                name: "bank_transfer_references",
                schema: "payments",
                columns: table => new
                {
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reference = table.Column<string>(type: "text", nullable: false),
                    MatchedBankStatementEntryJson = table.Column<string>(type: "jsonb", nullable: true),
                    MatchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MatchedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_transfer_references", x => x.PaymentId);
                    table.CheckConstraint("CK_bank_transfer_reference_format", "\"Reference\" ~ '^(SA|EG)-[0-9a-f]{8}-[A-Z0-9]{4}$'");
                });

            migrationBuilder.CreateTable(
                name: "chargebacks",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderChargebackId = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    ReasonCode = table.Column<string>(type: "text", nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ResolutionNotes = table.Column<string>(type: "text", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolvedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chargebacks", x => x.Id);
                    table.CheckConstraint("CK_chargebacks_amount_positive", "\"Amount\" > 0");
                    table.CheckConstraint("CK_chargebacks_status", "\"Status\" IN ('received','disputed','lost','won','accepted')");
                });

            migrationBuilder.CreateTable(
                name: "cod_collection_log",
                schema: "payments",
                columns: table => new
                {
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourierUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AmountCollected = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    CollectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OperatorConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OperatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Outcome = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cod_collection_log", x => x.PaymentId);
                    table.CheckConstraint("CK_cod_collection_log_amount_nonneg", "\"AmountCollected\" IS NULL OR \"AmountCollected\" >= 0");
                    table.CheckConstraint("CK_cod_collection_log_outcome", "\"Outcome\" IN ('collected','refused','address_not_found')");
                });

            migrationBuilder.CreateTable(
                name: "idempotency_keys",
                schema: "payments",
                columns: table => new
                {
                    Key = table.Column<string>(type: "text", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_keys", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "payment_methods_market_config",
                schema: "payments",
                columns: table => new
                {
                    MarketCode = table.Column<string>(type: "text", nullable: false),
                    Method = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    MinCartTotal = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    MaxCartTotal = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    EligibilityJson = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_methods_market_config", x => new { x.MarketCode, x.Method });
                    table.CheckConstraint("CK_payment_methods_cart_total_bounds", "\"MinCartTotal\" IS NULL OR \"MaxCartTotal\" IS NULL OR \"MinCartTotal\" <= \"MaxCartTotal\"");
                    table.CheckConstraint("CK_payment_methods_market", "\"MarketCode\" IN ('sa','eg')");
                });

            migrationBuilder.CreateTable(
                name: "payments",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketCode = table.Column<string>(type: "text", nullable: false),
                    Method = table.Column<string>(type: "text", nullable: false),
                    ProviderId = table.Column<string>(type: "text", nullable: true),
                    ProviderMessageId = table.Column<string>(type: "text", nullable: true),
                    State = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    FailedReason = table.Column<string>(type: "text", nullable: true),
                    ExpiredReason = table.Column<string>(type: "text", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientPayloadRedactedJson = table.Column<string>(type: "jsonb", nullable: false),
                    AuthorizedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CapturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payments", x => x.Id);
                    table.CheckConstraint("CK_payments_amount_positive", "\"Amount\" > 0");
                    table.CheckConstraint("CK_payments_currency", "\"Currency\" IN ('SAR','EGP')");
                    table.CheckConstraint("CK_payments_currency_matches_market", "(\"MarketCode\" = 'sa' AND \"Currency\" = 'SAR') OR (\"MarketCode\" = 'eg' AND \"Currency\" = 'EGP')");
                    table.CheckConstraint("CK_payments_market_code", "\"MarketCode\" IN ('sa','eg')");
                    table.CheckConstraint("CK_payments_method", "\"Method\" IN ('card','apple_pay','mada','stc_pay','meeza','bnpl_tabby','bnpl_tamara','bnpl_valu','cod','bank_transfer')");
                    table.CheckConstraint("CK_payments_state", "\"State\" IN ('pending_authorization','pending_external_redirect','pending_collection_on_delivery','pending_bank_transfer','authorized','capture_failed','captured','failed','expired','refunded','partially_refunded','chargeback_received')");
                });

            migrationBuilder.CreateTable(
                name: "pci_scope_events",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    EventKind = table.Column<string>(type: "text", nullable: false),
                    ChangedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangeSummary = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pci_scope_events", x => x.Id);
                    table.CheckConstraint("CK_pci_scope_events_kind", "\"EventKind\" IN ('kv_slot_added','kv_slot_removed','hosted_fields_domain_changed','provider_added','provider_removed')");
                });

            migrationBuilder.CreateTable(
                name: "provider_routing",
                schema: "payments",
                columns: table => new
                {
                    MarketCode = table.Column<string>(type: "text", nullable: false),
                    Method = table.Column<string>(type: "text", nullable: false),
                    PrimaryProviderId = table.Column<string>(type: "text", nullable: false),
                    BackupProviderId = table.Column<string>(type: "text", nullable: true),
                    AutoFailoverEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    FailoverThresholdPct = table.Column<int>(type: "integer", nullable: false, defaultValue: 50),
                    FailoverWindowMinutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 5),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provider_routing", x => new { x.MarketCode, x.Method });
                    table.CheckConstraint("CK_provider_routing_market", "\"MarketCode\" IN ('sa','eg')");
                    table.CheckConstraint("CK_provider_routing_primary_not_backup", "\"BackupProviderId\" IS NULL OR \"PrimaryProviderId\" <> \"BackupProviderId\"");
                    table.CheckConstraint("CK_provider_routing_threshold", "\"FailoverThresholdPct\" BETWEEN 1 AND 100");
                    table.CheckConstraint("CK_provider_routing_window", "\"FailoverWindowMinutes\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "reconciliation_exceptions",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    ProviderId = table.Column<string>(type: "text", nullable: true),
                    ProviderLedgerRowJson = table.Column<string>(type: "jsonb", nullable: true),
                    InternalPaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    InternalAmount = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    ProviderAmount = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    State = table.Column<string>(type: "text", nullable: false),
                    Resolution = table.Column<string>(type: "text", nullable: true),
                    ResolvedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolutionNotes = table.Column<string>(type: "text", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reconciliation_exceptions", x => x.Id);
                    table.CheckConstraint("CK_reconciliation_exceptions_reason", "\"Reason\" IN ('orphan_provider_row','missing_on_provider','amount_mismatch','currency_mismatch')");
                    table.CheckConstraint("CK_reconciliation_exceptions_resolution", "\"Resolution\" IS NULL OR \"Resolution\" IN ('refund_issued','internal_correction','provider_correction_requested','accepted_loss')");
                    table.CheckConstraint("CK_reconciliation_exceptions_resolved_consistency", "(\"State\" = 'open' AND \"Resolution\" IS NULL AND \"ResolvedAt\" IS NULL AND \"ResolvedBy\" IS NULL) OR (\"State\" = 'resolved' AND \"Resolution\" IS NOT NULL AND \"ResolvedAt\" IS NOT NULL AND \"ResolvedBy\" IS NOT NULL)");
                    table.CheckConstraint("CK_reconciliation_exceptions_state", "\"State\" IN ('open','resolved')");
                });

            migrationBuilder.CreateTable(
                name: "reconciliation_runs",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DateRangeStart = table.Column<DateOnly>(type: "date", nullable: false),
                    DateRangeEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    ProvidersProcessedJson = table.Column<string>(type: "jsonb", nullable: false),
                    InternalPaymentsCount = table.Column<int>(type: "integer", nullable: false),
                    ProviderLedgerRowsCount = table.Column<int>(type: "integer", nullable: false),
                    MatchedCount = table.Column<int>(type: "integer", nullable: false),
                    ExceptionsCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reconciliation_runs", x => x.Id);
                    table.CheckConstraint("CK_reconciliation_runs_date_range", "\"DateRangeStart\" <= \"DateRangeEnd\"");
                    table.CheckConstraint("CK_reconciliation_runs_status", "\"Status\" IN ('running','completed','failed')");
                });

            migrationBuilder.CreateTable(
                name: "refunds",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    State = table.Column<string>(type: "text", nullable: false),
                    ProviderRefundId = table.Column<string>(type: "text", nullable: true),
                    InitiatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    FailedReason = table.Column<string>(type: "text", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refunds", x => x.Id);
                    table.CheckConstraint("CK_refunds_amount_positive", "\"Amount\" > 0");
                    table.CheckConstraint("CK_refunds_currency", "\"Currency\" IN ('SAR','EGP')");
                    table.CheckConstraint("CK_refunds_state", "\"State\" IN ('pending','completed','failed')");
                });

            migrationBuilder.CreateTable(
                name: "webhooks_received",
                schema: "payments",
                columns: table => new
                {
                    ProviderId = table.Column<string>(type: "text", nullable: false),
                    ProviderMessageId = table.Column<string>(type: "text", nullable: false),
                    EventKind = table.Column<string>(type: "text", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SignatureValidated = table.Column<bool>(type: "boolean", nullable: false),
                    BodyHash = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhooks_received", x => new { x.ProviderId, x.ProviderMessageId, x.EventKind });
                });

            migrationBuilder.CreateIndex(
                name: "UX_bank_transfer_reference",
                schema: "payments",
                table: "bank_transfer_references",
                column: "Reference",
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_chargebacks_payment",
                schema: "payments",
                table: "chargebacks",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "UX_chargebacks_provider_id",
                schema: "payments",
                table: "chargebacks",
                column: "ProviderChargebackId",
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_keys_expires",
                schema: "payments",
                table: "idempotency_keys",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_keys_payment",
                schema: "payments",
                table: "idempotency_keys",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_payments_active_state",
                schema: "payments",
                table: "payments",
                column: "State",
                filter: "\"State\" IN ('pending_authorization','pending_external_redirect','pending_collection_on_delivery','pending_bank_transfer','authorized','capture_failed')");

            migrationBuilder.CreateIndex(
                name: "IX_payments_customer",
                schema: "payments",
                table: "payments",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_payments_order_created_desc",
                schema: "payments",
                table: "payments",
                columns: new[] { "OrderId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_payments_provider_message",
                schema: "payments",
                table: "payments",
                columns: new[] { "ProviderId", "ProviderMessageId" });

            migrationBuilder.CreateIndex(
                name: "UX_payments_idempotency_key_active",
                schema: "payments",
                table: "payments",
                column: "IdempotencyKey",
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_pci_scope_events_created_desc",
                schema: "payments",
                table: "pci_scope_events",
                column: "CreatedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_reconciliation_exceptions_run",
                schema: "payments",
                table: "reconciliation_exceptions",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_reconciliation_exceptions_state_created",
                schema: "payments",
                table: "reconciliation_exceptions",
                columns: new[] { "State", "CreatedAt" },
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_reconciliation_runs_started_desc",
                schema: "payments",
                table: "reconciliation_runs",
                column: "StartedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_refunds_payment",
                schema: "payments",
                table: "refunds",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_refunds_payment_state",
                schema: "payments",
                table: "refunds",
                columns: new[] { "PaymentId", "State" },
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_webhooks_received_at",
                schema: "payments",
                table: "webhooks_received",
                column: "ReceivedAt");

            // V-5 (BR-8) — DB-level trigger enforcing
            //   SUM(refunds.amount WHERE state IN ('pending','completed')) <= payments.amount
            // for every insert/update on refunds. App-layer pre-check is the first guard
            // (see Features/Refund/RefundHandler.cs); this trigger is the defense-in-depth
            // second guard so concurrent operator refunds cannot race past the cap.
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION payments.fn_check_refund_sum() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE
    captured_amount numeric(12,2);
    refund_sum numeric(12,2);
    target_payment_id uuid;
    target_amount numeric(12,2);
BEGIN
    target_payment_id := NEW.""PaymentId"";
    target_amount := NEW.""Amount"";
    SELECT ""Amount"" INTO captured_amount FROM payments.payments WHERE ""Id"" = target_payment_id;
    IF captured_amount IS NULL THEN
        RAISE EXCEPTION 'refund references unknown payment %', target_payment_id;
    END IF;
    SELECT COALESCE(SUM(""Amount""), 0) INTO refund_sum
    FROM payments.refunds
    WHERE ""PaymentId"" = target_payment_id
      AND ""State"" IN ('pending','completed')
      AND ""DeletedAt"" IS NULL
      AND (TG_OP = 'INSERT' OR ""Id"" <> NEW.""Id"");
    IF refund_sum + target_amount > captured_amount THEN
        RAISE EXCEPTION 'refund sum % + new % exceeds captured amount % (V-5)',
            refund_sum, target_amount, captured_amount
            USING ERRCODE = 'check_violation';
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_refunds_sum_check
BEFORE INSERT OR UPDATE ON payments.refunds
FOR EACH ROW
WHEN (NEW.""State"" IN ('pending','completed'))
EXECUTE FUNCTION payments.fn_check_refund_sum();
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_refunds_sum_check ON payments.refunds;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS payments.fn_check_refund_sum();");

            migrationBuilder.DropTable(
                name: "bank_transfer_references",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "chargebacks",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "cod_collection_log",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "idempotency_keys",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "payment_methods_market_config",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "payments",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "pci_scope_events",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "provider_routing",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "reconciliation_exceptions",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "reconciliation_runs",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "refunds",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "webhooks_received",
                schema: "payments");
        }
    }
}

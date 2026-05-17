using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Payments.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenPaymentsConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_pci_scope_events_kind",
                schema: "payments",
                table: "pci_scope_events");

            migrationBuilder.AddCheckConstraint(
                name: "CK_reconciliation_runs_counts_nonneg",
                schema: "payments",
                table: "reconciliation_runs",
                sql: "\"InternalPaymentsCount\" >= 0 AND \"ProviderLedgerRowsCount\" >= 0 AND \"MatchedCount\" >= 0 AND \"ExceptionsCount\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_provider_routing_auto_failover_requires_backup",
                schema: "payments",
                table: "provider_routing",
                sql: "\"AutoFailoverEnabled\" = FALSE OR \"BackupProviderId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_pci_scope_events_kind",
                schema: "payments",
                table: "pci_scope_events",
                sql: "\"EventKind\" IN ('kv_slot_added','kv_slot_removed','hosted_fields_domain_changed','provider_added','provider_removed','schema_drift_detected')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payments_idempotency_key_sha256",
                schema: "payments",
                table: "payments",
                sql: "\"IdempotencyKey\" ~ '^[0-9a-fA-F]{64}$'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payment_methods_max_cart_nonneg",
                schema: "payments",
                table: "payment_methods_market_config",
                sql: "\"MaxCartTotal\" IS NULL OR \"MaxCartTotal\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payment_methods_min_cart_nonneg",
                schema: "payments",
                table: "payment_methods_market_config",
                sql: "\"MinCartTotal\" IS NULL OR \"MinCartTotal\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_idempotency_keys_sha256",
                schema: "payments",
                table: "idempotency_keys",
                sql: "\"Key\" ~ '^[0-9a-fA-F]{64}$'");

            migrationBuilder.AddForeignKey(
                name: "FK_refunds_payments_PaymentId",
                schema: "payments",
                table: "refunds",
                column: "PaymentId",
                principalSchema: "payments",
                principalTable: "payments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_refunds_payments_PaymentId",
                schema: "payments",
                table: "refunds");

            migrationBuilder.DropCheckConstraint(
                name: "CK_reconciliation_runs_counts_nonneg",
                schema: "payments",
                table: "reconciliation_runs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_provider_routing_auto_failover_requires_backup",
                schema: "payments",
                table: "provider_routing");

            migrationBuilder.DropCheckConstraint(
                name: "CK_pci_scope_events_kind",
                schema: "payments",
                table: "pci_scope_events");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payments_idempotency_key_sha256",
                schema: "payments",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payment_methods_max_cart_nonneg",
                schema: "payments",
                table: "payment_methods_market_config");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payment_methods_min_cart_nonneg",
                schema: "payments",
                table: "payment_methods_market_config");

            migrationBuilder.DropCheckConstraint(
                name: "CK_idempotency_keys_sha256",
                schema: "payments",
                table: "idempotency_keys");

            migrationBuilder.AddCheckConstraint(
                name: "CK_pci_scope_events_kind",
                schema: "payments",
                table: "pci_scope_events",
                sql: "\"EventKind\" IN ('kv_slot_added','kv_slot_removed','hosted_fields_domain_changed','provider_added','provider_removed')");
        }
    }
}

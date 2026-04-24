using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Checkout.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Checkout_Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "checkout");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "idempotency_results",
                schema: "checkout",
                columns: table => new
                {
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestFingerprint = table.Column<byte[]>(type: "bytea", nullable: false),
                    ResponseStatus = table.Column<int>(type: "integer", nullable: false),
                    ResponseJson = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_results", x => x.IdempotencyKey);
                });

            migrationBuilder.CreateTable(
                name: "payment_attempts",
                schema: "checkout",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderId = table.Column<string>(type: "citext", nullable: false),
                    Method = table.Column<string>(type: "citext", nullable: false),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "citext", nullable: false),
                    State = table.Column<string>(type: "citext", nullable: false, defaultValue: "initiated"),
                    ProviderTxnId = table.Column<string>(type: "text", nullable: true),
                    ErrorCode = table.Column<string>(type: "text", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_attempts", x => x.Id);
                    table.CheckConstraint("CK_checkout_payment_attempts_amount_non_negative", "\"AmountMinor\" >= 0");
                    table.CheckConstraint("CK_checkout_payment_attempts_state_enum", "\"State\" IN ('initiated','authorized','captured','declined','voided','failed','pending_webhook')");
                });

            migrationBuilder.CreateTable(
                name: "payment_webhook_events",
                schema: "checkout",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderId = table.Column<string>(type: "citext", nullable: false),
                    ProviderEventId = table.Column<string>(type: "text", nullable: false),
                    EventType = table.Column<string>(type: "citext", nullable: false),
                    SignatureVerified = table.Column<bool>(type: "boolean", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    HandledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RawPayload = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_webhook_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                schema: "checkout",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CartId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    CartTokenHash = table.Column<byte[]>(type: "bytea", nullable: true),
                    MarketCode = table.Column<string>(type: "citext", nullable: false),
                    State = table.Column<string>(type: "citext", nullable: false, defaultValue: "init"),
                    ShippingAddressJson = table.Column<string>(type: "jsonb", nullable: true),
                    BillingAddressJson = table.Column<string>(type: "jsonb", nullable: true),
                    ShippingProviderId = table.Column<string>(type: "citext", nullable: true),
                    ShippingMethodCode = table.Column<string>(type: "citext", nullable: true),
                    ShippingFeeMinor = table.Column<long>(type: "bigint", nullable: true),
                    PaymentMethod = table.Column<string>(type: "citext", nullable: true),
                    CouponCode = table.Column<string>(type: "citext", nullable: true),
                    IssuedExplanationId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastPreviewHash = table.Column<byte[]>(type: "bytea", nullable: true),
                    AcceptedDriftAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastTouchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    FailureReasonCode = table.Column<string>(type: "citext", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sessions", x => x.Id);
                    table.CheckConstraint("CK_checkout_sessions_identity_present", "\"AccountId\" IS NOT NULL OR \"CartTokenHash\" IS NOT NULL");
                    table.CheckConstraint("CK_checkout_sessions_state_enum", "\"State\" IN ('init','addressed','shipping_selected','payment_selected','submitted','confirmed','failed','expired')");
                });

            migrationBuilder.CreateTable(
                name: "shipping_quotes",
                schema: "checkout",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderId = table.Column<string>(type: "citext", nullable: false),
                    MethodCode = table.Column<string>(type: "citext", nullable: false),
                    EtaMinDays = table.Column<int>(type: "integer", nullable: false),
                    EtaMaxDays = table.Column<int>(type: "integer", nullable: false),
                    FeeMinor = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "citext", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipping_quotes", x => x.Id);
                    table.CheckConstraint("CK_checkout_shipping_quotes_eta_max_ge_min", "\"EtaMaxDays\" >= \"EtaMinDays\"");
                    table.CheckConstraint("CK_checkout_shipping_quotes_eta_min_non_negative", "\"EtaMinDays\" >= 0");
                    table.CheckConstraint("CK_checkout_shipping_quotes_fee_non_negative", "\"FeeMinor\" >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_results_ExpiresAt",
                schema: "checkout",
                table: "idempotency_results",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_payment_attempts_ProviderId_ProviderTxnId",
                schema: "checkout",
                table: "payment_attempts",
                columns: new[] { "ProviderId", "ProviderTxnId" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_attempts_SessionId_CreatedAt",
                schema: "checkout",
                table: "payment_attempts",
                columns: new[] { "SessionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_webhook_events_ProviderId_ProviderEventId",
                schema: "checkout",
                table: "payment_webhook_events",
                columns: new[] { "ProviderId", "ProviderEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_webhook_events_ReceivedAt",
                schema: "checkout",
                table: "payment_webhook_events",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_checkout_sessions_account_state_touched",
                schema: "checkout",
                table: "sessions",
                columns: new[] { "AccountId", "State", "LastTouchedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_checkout_sessions_state_expires",
                schema: "checkout",
                table: "sessions",
                columns: new[] { "State", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_sessions_CartId",
                schema: "checkout",
                table: "sessions",
                column: "CartId");

            migrationBuilder.CreateIndex(
                name: "IX_sessions_MarketCode",
                schema: "checkout",
                table: "sessions",
                column: "MarketCode");

            migrationBuilder.CreateIndex(
                name: "IX_shipping_quotes_SessionId_ExpiresAt",
                schema: "checkout",
                table: "shipping_quotes",
                columns: new[] { "SessionId", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotency_results",
                schema: "checkout");

            migrationBuilder.DropTable(
                name: "payment_attempts",
                schema: "checkout");

            migrationBuilder.DropTable(
                name: "payment_webhook_events",
                schema: "checkout");

            migrationBuilder.DropTable(
                name: "sessions",
                schema: "checkout");

            migrationBuilder.DropTable(
                name: "shipping_quotes",
                schema: "checkout");
        }
    }
}

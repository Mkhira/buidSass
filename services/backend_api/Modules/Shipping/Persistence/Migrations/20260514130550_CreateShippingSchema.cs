using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Shipping.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateShippingSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "shipping");

            migrationBuilder.CreateTable(
                name: "dead_letter_labels",
                schema: "shipping",
                columns: table => new
                {
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastErrorMessageRedacted = table.Column<string>(type: "text", nullable: true),
                    LastErrorCode = table.Column<string>(type: "text", nullable: true),
                    EnteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Resolution = table.Column<string>(type: "text", nullable: true),
                    ResolvedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dead_letter_labels", x => x.ShipmentId);
                    table.CheckConstraint("CK_dead_letter_resolution", "\"Resolution\" IS NULL OR \"Resolution\" IN ('retry','discard','manual_label')");
                    table.CheckConstraint("CK_dead_letter_resolved_consistency", "(\"ResolvedAt\" IS NULL AND \"Resolution\" IS NULL) OR (\"ResolvedAt\" IS NOT NULL AND \"Resolution\" IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "fee_tables",
                schema: "shipping",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    MethodVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ZoneId = table.Column<Guid>(type: "uuid", nullable: false),
                    WeightMinKg = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    WeightMaxKg = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    FeeAmount = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    EffectiveAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fee_tables", x => x.Id);
                    table.CheckConstraint("CK_fee_tables_currency", "\"Currency\" IN ('SAR','EGP')");
                    table.CheckConstraint("CK_fee_tables_fee_amount_nonneg", "\"FeeAmount\" >= 0");
                    table.CheckConstraint("CK_fee_tables_weight_range", "\"WeightMinKg\" >= 0 AND \"WeightMaxKg\" > \"WeightMinKg\"");
                });

            migrationBuilder.CreateTable(
                name: "market_schemas",
                schema: "shipping",
                columns: table => new
                {
                    MarketCode = table.Column<string>(type: "text", nullable: false),
                    PostalCodeRegex = table.Column<string>(type: "text", nullable: true),
                    DefaultCurrency = table.Column<string>(type: "text", nullable: false),
                    DefaultEtaDaysMin = table.Column<int>(type: "integer", nullable: false),
                    DefaultEtaDaysMax = table.Column<int>(type: "integer", nullable: false),
                    SlaBreachThresholdHours = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_market_schemas", x => x.MarketCode);
                    table.CheckConstraint("CK_market_schemas_currency", "\"DefaultCurrency\" IN ('SAR','EGP')");
                    table.CheckConstraint("CK_market_schemas_eta_days", "\"DefaultEtaDaysMin\" > 0 AND \"DefaultEtaDaysMax\" >= \"DefaultEtaDaysMin\"");
                    table.CheckConstraint("CK_market_schemas_market", "\"MarketCode\" IN ('SA','EG')");
                    table.CheckConstraint("CK_market_schemas_sla_hours", "\"SlaBreachThresholdHours\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "provider_routing",
                schema: "shipping",
                columns: table => new
                {
                    MarketCode = table.Column<string>(type: "text", nullable: false),
                    MethodId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrimaryProviderId = table.Column<string>(type: "text", nullable: false),
                    BackupProviderId = table.Column<string>(type: "text", nullable: true),
                    AutoFailoverEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    FailoverThresholdPct = table.Column<int>(type: "integer", nullable: false),
                    FailoverWindowMinutes = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provider_routing", x => new { x.MarketCode, x.MethodId });
                    table.CheckConstraint("CK_provider_routing_market", "\"MarketCode\" IN ('SA','EG')");
                    table.CheckConstraint("CK_provider_routing_primary_neq_backup", "\"BackupProviderId\" IS NULL OR \"BackupProviderId\" <> \"PrimaryProviderId\"");
                    table.CheckConstraint("CK_provider_routing_threshold_pct", "\"FailoverThresholdPct\" BETWEEN 10 AND 90");
                    table.CheckConstraint("CK_provider_routing_window_minutes", "\"FailoverWindowMinutes\" BETWEEN 1 AND 60");
                });

            migrationBuilder.CreateTable(
                name: "shipment_disputes",
                schema: "shipping",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReportedBy = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ResolutionNotes = table.Column<string>(type: "text", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolvedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipment_disputes", x => x.Id);
                    table.CheckConstraint("CK_shipment_disputes_reported_by", "\"ReportedBy\" IN ('customer','support_agent')");
                    table.CheckConstraint("CK_shipment_disputes_status", "\"Status\" IN ('open','re_delivered','closed_with_refund','closed_no_action')");
                });

            migrationBuilder.CreateTable(
                name: "shipment_events",
                schema: "shipping",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderEventKind = table.Column<string>(type: "text", nullable: false),
                    InternalStateAtEvent = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RawPayloadRedactedJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipment_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "shipments",
                schema: "shipping",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketCode = table.Column<string>(type: "text", nullable: false),
                    MethodVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderId = table.Column<string>(type: "text", nullable: false),
                    ProviderTrackingId = table.Column<string>(type: "text", nullable: true),
                    LabelPdfBlobUrl = table.Column<string>(type: "text", nullable: true),
                    State = table.Column<string>(type: "text", nullable: false),
                    ShipToAddressRedactedJson = table.Column<string>(type: "jsonb", nullable: false),
                    ParentShipmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    EtaMin = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EtaMax = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LabelPurchasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    HandedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    InTransitAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailedReason = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipments", x => x.Id);
                    table.CheckConstraint("CK_shipments_attempts_nonneg", "\"Attempts\" >= 0");
                    table.CheckConstraint("CK_shipments_market_code", "\"MarketCode\" IN ('SA','EG')");
                    table.CheckConstraint("CK_shipments_state", "\"State\" IN ('pending','label_purchased','handed_to_carrier','in_transit','out_for_delivery','delivered','delivery_attempted','return_to_sender_initiated','returned_to_sender','delivery_disputed','re_delivered_pending','closed_with_refund','failed_to_create_label','pending_label_provider_failure','dead_letter_label','label_voided')");
                });

            migrationBuilder.CreateTable(
                name: "shipping_method_versions",
                schema: "shipping",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    MethodId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    EligibilityJson = table.Column<string>(type: "jsonb", nullable: false),
                    EtaMinHours = table.Column<int>(type: "integer", nullable: false),
                    EtaMaxHours = table.Column<int>(type: "integer", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewerId = table.Column<Guid>(type: "uuid", nullable: true),
                    EffectiveAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipping_method_versions", x => x.Id);
                    table.CheckConstraint("CK_method_versions_eta", "\"EtaMinHours\" >= 0 AND \"EtaMaxHours\" >= \"EtaMinHours\"");
                    table.CheckConstraint("CK_method_versions_reviewer_not_author", "\"PublishedAt\" IS NULL OR \"ReviewerId\" IS NULL OR \"ReviewerId\" <> \"AuthorId\"");
                    table.CheckConstraint("CK_method_versions_state", "\"State\" IN ('draft','in_review','published','archived')");
                    table.CheckConstraint("CK_method_versions_version_no", "\"VersionNo\" >= 1");
                });

            migrationBuilder.CreateTable(
                name: "shipping_methods",
                schema: "shipping",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    MarketCode = table.Column<string>(type: "text", nullable: false),
                    NameAr = table.Column<string>(type: "text", nullable: false),
                    NameEn = table.Column<string>(type: "text", nullable: false),
                    DescriptionAr = table.Column<string>(type: "text", nullable: true),
                    DescriptionEn = table.Column<string>(type: "text", nullable: true),
                    CurrentVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipping_methods", x => x.Id);
                    table.CheckConstraint("CK_shipping_methods_market_code", "\"MarketCode\" IN ('SA','EG')");
                    table.CheckConstraint("CK_shipping_methods_name_lengths", "char_length(\"NameAr\") BETWEEN 1 AND 120 AND char_length(\"NameEn\") BETWEEN 1 AND 120");
                });

            migrationBuilder.CreateTable(
                name: "shipping_zones",
                schema: "shipping",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    MarketCode = table.Column<string>(type: "text", nullable: false),
                    NameAr = table.Column<string>(type: "text", nullable: false),
                    NameEn = table.Column<string>(type: "text", nullable: false),
                    PostalCodePrefixesJson = table.Column<string>(type: "jsonb", nullable: false),
                    CityListJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipping_zones", x => x.Id);
                    table.CheckConstraint("CK_shipping_zones_market_code", "\"MarketCode\" IN ('SA','EG')");
                    table.CheckConstraint("CK_shipping_zones_name_lengths", "char_length(\"NameAr\") BETWEEN 1 AND 120 AND char_length(\"NameEn\") BETWEEN 1 AND 120");
                });

            migrationBuilder.CreateTable(
                name: "webhooks_received",
                schema: "shipping",
                columns: table => new
                {
                    ProviderId = table.Column<string>(type: "text", nullable: false),
                    ProviderTrackingId = table.Column<string>(type: "text", nullable: false),
                    EventKind = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SignatureValidated = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhooks_received", x => new { x.ProviderId, x.ProviderTrackingId, x.EventKind, x.OccurredAt });
                });

            migrationBuilder.CreateIndex(
                name: "IX_fee_tables_lookup",
                schema: "shipping",
                table: "fee_tables",
                columns: new[] { "MethodVersionId", "ZoneId" });

            migrationBuilder.CreateIndex(
                name: "IX_shipment_disputes_shipment_status",
                schema: "shipping",
                table: "shipment_disputes",
                columns: new[] { "ShipmentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_shipment_events_shipment_occurred",
                schema: "shipping",
                table: "shipment_events",
                columns: new[] { "ShipmentId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_shipments_active_state",
                schema: "shipping",
                table: "shipments",
                columns: new[] { "State", "MarketCode" },
                filter: "\"State\" IN ('pending','label_purchased','handed_to_carrier','in_transit','out_for_delivery','delivery_attempted','re_delivered_pending','pending_label_provider_failure')");

            migrationBuilder.CreateIndex(
                name: "IX_shipments_parent",
                schema: "shipping",
                table: "shipments",
                column: "ParentShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_shipments_provider_tracking",
                schema: "shipping",
                table: "shipments",
                columns: new[] { "ProviderId", "ProviderTrackingId" });

            migrationBuilder.CreateIndex(
                name: "UX_shipments_order_id_active",
                schema: "shipping",
                table: "shipments",
                column: "OrderId",
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_method_versions_state",
                schema: "shipping",
                table: "shipping_method_versions",
                columns: new[] { "MethodId", "State" });

            migrationBuilder.CreateIndex(
                name: "UX_method_versions_method_version_no",
                schema: "shipping",
                table: "shipping_method_versions",
                columns: new[] { "MethodId", "VersionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shipping_methods_market",
                schema: "shipping",
                table: "shipping_methods",
                columns: new[] { "MarketCode", "DeletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_shipping_zones_market",
                schema: "shipping",
                table: "shipping_zones",
                columns: new[] { "MarketCode", "DeletedAt" });

            // T005 — Postgres EXCLUDE constraint preventing overlapping weight tiers
            // on the same (method_version_id, zone_id). EF Core does not emit
            // exclusion constraints natively, so the migration adds it via raw SQL
            // (research §4 — race-proof at the DB layer).
            //
            // The btree_gist extension is required for `=` operator support inside
            // an EXCLUDE expression alongside the range operator. It ships with
            // Postgres contrib and is already enabled on the project's Postgres
            // images for similar use elsewhere in the codebase.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");
            migrationBuilder.Sql(@"
                ALTER TABLE shipping.fee_tables
                ADD CONSTRAINT EX_fee_tables_no_overlapping_tiers
                EXCLUDE USING gist (
                    ""MethodVersionId"" WITH =,
                    ""ZoneId"" WITH =,
                    numrange(""WeightMinKg""::numeric, ""WeightMaxKg""::numeric, '[)') WITH &&
                )
                WHERE (""DeletedAt"" IS NULL);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE IF EXISTS shipping.fee_tables DROP CONSTRAINT IF EXISTS \"EX_fee_tables_no_overlapping_tiers\";");

            migrationBuilder.DropTable(
                name: "dead_letter_labels",
                schema: "shipping");

            migrationBuilder.DropTable(
                name: "fee_tables",
                schema: "shipping");

            migrationBuilder.DropTable(
                name: "market_schemas",
                schema: "shipping");

            migrationBuilder.DropTable(
                name: "provider_routing",
                schema: "shipping");

            migrationBuilder.DropTable(
                name: "shipment_disputes",
                schema: "shipping");

            migrationBuilder.DropTable(
                name: "shipment_events",
                schema: "shipping");

            migrationBuilder.DropTable(
                name: "shipments",
                schema: "shipping");

            migrationBuilder.DropTable(
                name: "shipping_method_versions",
                schema: "shipping");

            migrationBuilder.DropTable(
                name: "shipping_methods",
                schema: "shipping");

            migrationBuilder.DropTable(
                name: "shipping_zones",
                schema: "shipping");

            migrationBuilder.DropTable(
                name: "webhooks_received",
                schema: "shipping");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Pricing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Pricing_007b_CommercialAuthoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AppliesToBroken",
                schema: "pricing",
                table: "promotions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AppliesToBrokenAtUtc",
                schema: "pricing",
                table: "promotions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "BannerEligible",
                schema: "pricing",
                table: "promotions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "State",
                schema: "pricing",
                table: "promotions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StateChangedAtUtc",
                schema: "pricing",
                table: "promotions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<Guid>(
                name: "StateChangedByActorId",
                schema: "pricing",
                table: "promotions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "StateChangedReasonNote",
                schema: "pricing",
                table: "promotions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "pricing",
                table: "promotions",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                schema: "pricing",
                table: "product_tier_prices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CompanyLinkBroken",
                schema: "pricing",
                table: "product_tier_prices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompanyLinkBrokenAtUtc",
                schema: "pricing",
                table: "product_tier_prices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CopiedFromTierId",
                schema: "pricing",
                table: "product_tier_prices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "State",
                schema: "pricing",
                table: "product_tier_prices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StateChangedAtUtc",
                schema: "pricing",
                table: "product_tier_prices",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<Guid>(
                name: "StateChangedByActorId",
                schema: "pricing",
                table: "product_tier_prices",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "StateChangedReasonNote",
                schema: "pricing",
                table: "product_tier_prices",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VendorId",
                schema: "pricing",
                table: "product_tier_prices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "pricing",
                table: "product_tier_prices",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<bool>(
                name: "AppliesToBroken",
                schema: "pricing",
                table: "coupons",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AppliesToBrokenAtUtc",
                schema: "pricing",
                table: "coupons",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DisplayInBanners",
                schema: "pricing",
                table: "coupons",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "State",
                schema: "pricing",
                table: "coupons",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StateChangedAtUtc",
                schema: "pricing",
                table: "coupons",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<Guid>(
                name: "StateChangedByActorId",
                schema: "pricing",
                table: "coupons",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "StateChangedReasonNote",
                schema: "pricing",
                table: "coupons",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "pricing",
                table: "coupons",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "campaigns",
                schema: "pricing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NameAr = table.Column<string>(type: "text", maxLength: 300, nullable: false),
                    NameEn = table.Column<string>(type: "text", maxLength: 300, nullable: false),
                    ValidFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ValidTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Markets = table.Column<string[]>(type: "citext[]", nullable: false),
                    LandingQuery = table.Column<string>(type: "text", maxLength: 1024, nullable: true),
                    NotesInternal = table.Column<string>(type: "text", maxLength: 4000, nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    StateChangedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StateChangedByActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    StateChangedReasonNote = table.Column<string>(type: "text", nullable: true),
                    LinkBroken = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_campaigns", x => x.Id);
                    table.CheckConstraint("chk_campaign_markets_non_empty", "array_length(\"Markets\", 1) >= 1");
                    table.CheckConstraint("chk_campaign_valid_window", "\"ValidTo\" > \"ValidFrom\"");
                });

            migrationBuilder.CreateTable(
                name: "commercial_approvals",
                schema: "pricing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetEntityKind = table.Column<string>(type: "citext", nullable: false),
                    TargetEntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApproverActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CosignNote = table.Column<string>(type: "text", nullable: false),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_commercial_approvals", x => x.Id);
                    table.CheckConstraint("chk_approval_note_min_length", "char_length(\"CosignNote\") >= 10");
                    table.CheckConstraint("chk_approval_separation_of_duties", "\"ApproverActorId\" <> \"AuthorActorId\"");
                    table.CheckConstraint("chk_approval_target_kind", "\"TargetEntityKind\"::text IN ('coupon','promotion')");
                });

            migrationBuilder.CreateTable(
                name: "commercial_audit_events",
                schema: "pricing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetEntityKind = table.Column<string>(type: "citext", nullable: false),
                    TargetEntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "citext", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorRole = table.Column<string>(type: "citext", nullable: false),
                    BeforeJson = table.Column<string>(type: "jsonb", nullable: true),
                    AfterJson = table.Column<string>(type: "jsonb", nullable: true),
                    DiffJson = table.Column<string>(type: "jsonb", nullable: true),
                    ReasonNote = table.Column<string>(type: "text", nullable: true),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_commercial_audit_events", x => x.Id);
                    table.CheckConstraint("chk_cae_target_kind", "\"TargetEntityKind\"::text IN ('coupon','promotion','campaign','business_pricing','preview_profile','commercial_threshold','commercial_approval')");
                });

            migrationBuilder.CreateTable(
                name: "commercial_thresholds",
                schema: "pricing",
                columns: table => new
                {
                    MarketCode = table.Column<string>(type: "citext", nullable: false),
                    GateEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    ThresholdPercentOff = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    ThresholdAmountOffMinor = table.Column<long>(type: "bigint", nullable: true),
                    ThresholdDurationDays = table.Column<int>(type: "integer", nullable: true),
                    CouponInFlightGraceSeconds = table.Column<int>(type: "integer", nullable: false, defaultValue: 1800),
                    PromotionInFlightGraceSeconds = table.Column<int>(type: "integer", nullable: false, defaultValue: 1800),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_commercial_thresholds", x => x.MarketCode);
                    table.CheckConstraint("chk_thr_amount_off", "\"ThresholdAmountOffMinor\" IS NULL OR \"ThresholdAmountOffMinor\" >= 0");
                    table.CheckConstraint("chk_thr_coupon_grace", "\"CouponInFlightGraceSeconds\" >= 300 AND \"CouponInFlightGraceSeconds\" <= 7200");
                    table.CheckConstraint("chk_thr_duration", "\"ThresholdDurationDays\" IS NULL OR \"ThresholdDurationDays\" >= 0");
                    table.CheckConstraint("chk_thr_market", "\"MarketCode\"::text IN ('SA','EG')");
                    table.CheckConstraint("chk_thr_pct_off", "\"ThresholdPercentOff\" IS NULL OR (\"ThresholdPercentOff\" >= 0 AND \"ThresholdPercentOff\" <= 100)");
                    table.CheckConstraint("chk_thr_promo_grace", "\"PromotionInFlightGraceSeconds\" >= 300 AND \"PromotionInFlightGraceSeconds\" <= 7200");
                });

            migrationBuilder.CreateTable(
                name: "preview_profiles",
                schema: "pricing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", maxLength: 200, nullable: false),
                    MarketCode = table.Column<string>(type: "citext", nullable: false),
                    Locale = table.Column<string>(type: "citext", nullable: false),
                    AccountKind = table.Column<string>(type: "citext", nullable: false),
                    TierId = table.Column<Guid>(type: "uuid", nullable: true),
                    VerificationState = table.Column<string>(type: "citext", nullable: false),
                    CartLinesJson = table.Column<string>(type: "jsonb", nullable: false),
                    Visibility = table.Column<string>(type: "citext", nullable: false, defaultValue: "personal"),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_preview_profiles", x => x.Id);
                    table.CheckConstraint("chk_preview_profile_account_kind", "\"AccountKind\"::text IN ('consumer','b2b')");
                    table.CheckConstraint("chk_preview_profile_locale", "\"Locale\"::text IN ('ar','en')");
                    table.CheckConstraint("chk_preview_profile_market", "\"MarketCode\"::text IN ('SA','EG')");
                    table.CheckConstraint("chk_preview_profile_verification_state", "\"VerificationState\"::text IN ('none','submitted','approved','rejected','expired')");
                    table.CheckConstraint("chk_preview_profile_visibility", "\"Visibility\"::text IN ('personal','shared')");
                });

            migrationBuilder.CreateTable(
                name: "campaign_links",
                schema: "pricing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "citext", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: true),
                    LinkBrokenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_campaign_links", x => x.Id);
                    table.CheckConstraint("chk_campaign_link_kind", "\"Kind\"::text IN ('promotion','coupon','landing_only')");
                    table.CheckConstraint("chk_campaign_link_target_required", "(\"Kind\"::text = 'landing_only' AND \"TargetId\" IS NULL) OR (\"Kind\"::text <> 'landing_only' AND \"TargetId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_campaign_links_campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalSchema: "pricing",
                        principalTable: "campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_promotions_applies_to_broken",
                schema: "pricing",
                table: "promotions",
                column: "AppliesToBroken",
                filter: "\"AppliesToBroken\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_promotions_state",
                schema: "pricing",
                table: "promotions",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_promotions_vendor_id",
                schema: "pricing",
                table: "promotions",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_product_tier_prices_company_id",
                schema: "pricing",
                table: "product_tier_prices",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_product_tier_prices_company_link_broken",
                schema: "pricing",
                table: "product_tier_prices",
                column: "CompanyLinkBroken",
                filter: "\"CompanyLinkBroken\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_product_tier_prices_vendor_id",
                schema: "pricing",
                table: "product_tier_prices",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_coupons_applies_to_broken",
                schema: "pricing",
                table: "coupons",
                column: "AppliesToBroken",
                filter: "\"AppliesToBroken\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_coupons_state",
                schema: "pricing",
                table: "coupons",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_coupons_vendor_id",
                schema: "pricing",
                table: "coupons",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_campaign_links_kind_target",
                schema: "pricing",
                table: "campaign_links",
                columns: new[] { "Kind", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "UX_campaign_links_active_per_campaign",
                schema: "pricing",
                table: "campaign_links",
                column: "CampaignId",
                unique: true,
                filter: "\"LinkBrokenAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_campaigns_state_active_or_scheduled",
                schema: "pricing",
                table: "campaigns",
                column: "State",
                filter: "\"State\" IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "IX_campaigns_valid_from",
                schema: "pricing",
                table: "campaigns",
                column: "ValidFrom");

            migrationBuilder.CreateIndex(
                name: "IX_campaigns_vendor_id",
                schema: "pricing",
                table: "campaigns",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "UX_commercial_approvals_target",
                schema: "pricing",
                table: "commercial_approvals",
                columns: new[] { "TargetEntityKind", "TargetEntityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cae_actor",
                schema: "pricing",
                table: "commercial_audit_events",
                columns: new[] { "ActorId", "RecordedAtUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_cae_target",
                schema: "pricing",
                table: "commercial_audit_events",
                columns: new[] { "TargetEntityKind", "TargetEntityId", "RecordedAtUtc" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_preview_profiles_personal_by_creator",
                schema: "pricing",
                table: "preview_profiles",
                column: "CreatedBy",
                filter: "\"Visibility\"::text = 'personal'");

            migrationBuilder.CreateIndex(
                name: "IX_preview_profiles_shared",
                schema: "pricing",
                table: "preview_profiles",
                column: "Visibility",
                filter: "\"Visibility\"::text = 'shared'");

            // ----- spec 007-b raw-SQL augmentations -----

            // GIN index on Campaign.markets[] for efficient market-overlap queries.
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_campaigns_markets_gin""
                ON pricing.campaigns USING GIN (""Markets"");
            ");

            // Append-only trigger function for commercial_audit_events.
            // Fires on UPDATE or DELETE; raises an exception so audit rows are immutable.
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION pricing.raise_immutable_audit_violation()
                RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'pricing.commercial_audit_events is append-only; % blocked', TG_OP
                        USING ERRCODE = 'P0001';
                END;
                $$ LANGUAGE plpgsql;
            ");

            migrationBuilder.Sql(@"
                CREATE TRIGGER trg_commercial_audit_events_immutable
                BEFORE UPDATE OR DELETE ON pricing.commercial_audit_events
                FOR EACH ROW EXECUTE FUNCTION pricing.raise_immutable_audit_violation();
            ");

            // Seed conservative per-market thresholds (gate ON, FR-025 defaults).
            migrationBuilder.Sql(@"
                INSERT INTO pricing.commercial_thresholds
                    (""MarketCode"", ""GateEnabled"",
                     ""ThresholdPercentOff"", ""ThresholdAmountOffMinor"", ""ThresholdDurationDays"",
                     ""CouponInFlightGraceSeconds"", ""PromotionInFlightGraceSeconds"",
                     ""UpdatedAtUtc"", ""UpdatedByActorId"")
                VALUES
                    ('SA', TRUE, 30.00, 5000000, 14, 1800, 1800, NOW(), '00000000-0000-0000-0000-000000000000'),
                    ('EG', TRUE, 30.00, 25000000, 14, 1800, 1800, NOW(), '00000000-0000-0000-0000-000000000000')
                ON CONFLICT (""MarketCode"") DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS trg_commercial_audit_events_immutable ON pricing.commercial_audit_events;");
            migrationBuilder.Sql(@"DROP FUNCTION IF EXISTS pricing.raise_immutable_audit_violation();");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS pricing.""IX_campaigns_markets_gin"";");

            migrationBuilder.DropTable(
                name: "campaign_links",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "commercial_approvals",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "commercial_audit_events",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "commercial_thresholds",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "preview_profiles",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "campaigns",
                schema: "pricing");

            migrationBuilder.DropIndex(
                name: "IX_promotions_applies_to_broken",
                schema: "pricing",
                table: "promotions");

            migrationBuilder.DropIndex(
                name: "IX_promotions_state",
                schema: "pricing",
                table: "promotions");

            migrationBuilder.DropIndex(
                name: "IX_promotions_vendor_id",
                schema: "pricing",
                table: "promotions");

            migrationBuilder.DropIndex(
                name: "IX_product_tier_prices_company_id",
                schema: "pricing",
                table: "product_tier_prices");

            migrationBuilder.DropIndex(
                name: "IX_product_tier_prices_company_link_broken",
                schema: "pricing",
                table: "product_tier_prices");

            migrationBuilder.DropIndex(
                name: "IX_product_tier_prices_vendor_id",
                schema: "pricing",
                table: "product_tier_prices");

            migrationBuilder.DropIndex(
                name: "IX_coupons_applies_to_broken",
                schema: "pricing",
                table: "coupons");

            migrationBuilder.DropIndex(
                name: "IX_coupons_state",
                schema: "pricing",
                table: "coupons");

            migrationBuilder.DropIndex(
                name: "IX_coupons_vendor_id",
                schema: "pricing",
                table: "coupons");

            migrationBuilder.DropColumn(
                name: "AppliesToBroken",
                schema: "pricing",
                table: "promotions");

            migrationBuilder.DropColumn(
                name: "AppliesToBrokenAtUtc",
                schema: "pricing",
                table: "promotions");

            migrationBuilder.DropColumn(
                name: "BannerEligible",
                schema: "pricing",
                table: "promotions");

            migrationBuilder.DropColumn(
                name: "State",
                schema: "pricing",
                table: "promotions");

            migrationBuilder.DropColumn(
                name: "StateChangedAtUtc",
                schema: "pricing",
                table: "promotions");

            migrationBuilder.DropColumn(
                name: "StateChangedByActorId",
                schema: "pricing",
                table: "promotions");

            migrationBuilder.DropColumn(
                name: "StateChangedReasonNote",
                schema: "pricing",
                table: "promotions");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "pricing",
                table: "promotions");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                schema: "pricing",
                table: "product_tier_prices");

            migrationBuilder.DropColumn(
                name: "CompanyLinkBroken",
                schema: "pricing",
                table: "product_tier_prices");

            migrationBuilder.DropColumn(
                name: "CompanyLinkBrokenAtUtc",
                schema: "pricing",
                table: "product_tier_prices");

            migrationBuilder.DropColumn(
                name: "CopiedFromTierId",
                schema: "pricing",
                table: "product_tier_prices");

            migrationBuilder.DropColumn(
                name: "State",
                schema: "pricing",
                table: "product_tier_prices");

            migrationBuilder.DropColumn(
                name: "StateChangedAtUtc",
                schema: "pricing",
                table: "product_tier_prices");

            migrationBuilder.DropColumn(
                name: "StateChangedByActorId",
                schema: "pricing",
                table: "product_tier_prices");

            migrationBuilder.DropColumn(
                name: "StateChangedReasonNote",
                schema: "pricing",
                table: "product_tier_prices");

            migrationBuilder.DropColumn(
                name: "VendorId",
                schema: "pricing",
                table: "product_tier_prices");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "pricing",
                table: "product_tier_prices");

            migrationBuilder.DropColumn(
                name: "AppliesToBroken",
                schema: "pricing",
                table: "coupons");

            migrationBuilder.DropColumn(
                name: "AppliesToBrokenAtUtc",
                schema: "pricing",
                table: "coupons");

            migrationBuilder.DropColumn(
                name: "DisplayInBanners",
                schema: "pricing",
                table: "coupons");

            migrationBuilder.DropColumn(
                name: "State",
                schema: "pricing",
                table: "coupons");

            migrationBuilder.DropColumn(
                name: "StateChangedAtUtc",
                schema: "pricing",
                table: "coupons");

            migrationBuilder.DropColumn(
                name: "StateChangedByActorId",
                schema: "pricing",
                table: "coupons");

            migrationBuilder.DropColumn(
                name: "StateChangedReasonNote",
                schema: "pricing",
                table: "coupons");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "pricing",
                table: "coupons");
        }
    }
}

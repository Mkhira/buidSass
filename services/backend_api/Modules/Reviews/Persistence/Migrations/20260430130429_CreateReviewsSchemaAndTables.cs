using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Reviews.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateReviewsSchemaAndTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "reviews");

            migrationBuilder.CreateTable(
                name: "product_rating_aggregates",
                schema: "reviews",
                columns: table => new
                {
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketCode = table.Column<string>(type: "text", nullable: false),
                    AvgRating = table.Column<decimal>(type: "numeric(3,2)", nullable: true),
                    ReviewCount = table.Column<int>(type: "integer", nullable: false),
                    Distribution1 = table.Column<int>(type: "integer", nullable: false),
                    Distribution2 = table.Column<int>(type: "integer", nullable: false),
                    Distribution3 = table.Column<int>(type: "integer", nullable: false),
                    Distribution4 = table.Column<int>(type: "integer", nullable: false),
                    Distribution5 = table.Column<int>(type: "integer", nullable: false),
                    LastUpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_rating_aggregates", x => new { x.ProductId, x.MarketCode });
                    table.CheckConstraint("CK_pra_market_code", "\"MarketCode\" IN ('SA','EG')");
                });

            migrationBuilder.CreateTable(
                name: "reviews",
                schema: "reviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketCode = table.Column<string>(type: "text", nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    Headline = table.Column<string>(type: "text", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    Locale = table.Column<string>(type: "text", nullable: false),
                    MediaUrls = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    State = table.Column<string>(type: "text", nullable: false),
                    StateChangedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StateChangedByActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    StateChangedReasonNote = table.Column<string>(type: "text", nullable: true),
                    StateChangedAdminNote = table.Column<string>(type: "text", nullable: true),
                    TriggeredBy = table.Column<string>(type: "text", nullable: false),
                    PendingModerationStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FilterTripTerms = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    MediaAttachmentReviewRequired = table.Column<bool>(type: "boolean", nullable: false),
                    EditCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeliveredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reviews", x => x.Id);
                    table.CheckConstraint("CK_reviews_body_len", "char_length(\"Body\") BETWEEN 1 AND 4000");
                    table.CheckConstraint("CK_reviews_headline_len", "char_length(\"Headline\") BETWEEN 1 AND 100");
                    table.CheckConstraint("CK_reviews_locale", "\"Locale\" IN ('ar','en')");
                    table.CheckConstraint("CK_reviews_market_code", "\"MarketCode\" IN ('SA','EG')");
                    table.CheckConstraint("CK_reviews_media_max", "jsonb_array_length(\"MediaUrls\") <= 4");
                    table.CheckConstraint("CK_reviews_rating_range", "\"Rating\" BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_reviews_state", "\"State\" IN ('pending_moderation','visible','flagged','hidden','deleted')");
                    table.CheckConstraint("CK_reviews_triggered_by", "\"TriggeredBy\" IN ('customer_submission','customer_edit','community_report_threshold','refund_event','account_locked','moderator_action','manual_super_admin')");
                });

            migrationBuilder.CreateTable(
                name: "reviews_filter_wordlists",
                schema: "reviews",
                columns: table => new
                {
                    MarketCode = table.Column<string>(type: "text", nullable: false),
                    Term = table.Column<string>(type: "text", nullable: false),
                    Severity = table.Column<string>(type: "text", nullable: true),
                    CreatedByActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reviews_filter_wordlists", x => new { x.MarketCode, x.Term });
                    table.CheckConstraint("CK_rfw_market_code", "\"MarketCode\" IN ('SA','EG')");
                    table.CheckConstraint("CK_rfw_severity", "\"Severity\" IS NULL OR \"Severity\" IN ('block','warn')");
                    table.CheckConstraint("CK_rfw_term_len", "char_length(\"Term\") BETWEEN 1 AND 200");
                });

            migrationBuilder.CreateTable(
                name: "reviews_market_schemas",
                schema: "reviews",
                columns: table => new
                {
                    MarketCode = table.Column<string>(type: "text", nullable: false),
                    EligibilityWindowDays = table.Column<int>(type: "integer", nullable: false),
                    EditWindowDays = table.Column<int>(type: "integer", nullable: false),
                    CommunityReportThreshold = table.Column<int>(type: "integer", nullable: false),
                    CommunityReportWindowDays = table.Column<int>(type: "integer", nullable: false),
                    ReportQualifyingAccountAgeDays = table.Column<int>(type: "integer", nullable: false),
                    ReportQualifyingRequiresVerifiedBuyer = table.Column<bool>(type: "boolean", nullable: false),
                    PendingModerationSlaHours = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reviews_market_schemas", x => x.MarketCode);
                    table.CheckConstraint("CK_rms_community_threshold", "\"CommunityReportThreshold\" BETWEEN 1 AND 10");
                    table.CheckConstraint("CK_rms_edit_window", "\"EditWindowDays\" BETWEEN 7 AND 90");
                    table.CheckConstraint("CK_rms_eligibility_window", "\"EligibilityWindowDays\" BETWEEN 30 AND 730");
                    table.CheckConstraint("CK_rms_market_code", "\"MarketCode\" IN ('SA','EG')");
                    table.CheckConstraint("CK_rms_qualifying_age", "\"ReportQualifyingAccountAgeDays\" BETWEEN 0 AND 90");
                });

            migrationBuilder.CreateTable(
                name: "review_admin_notes",
                schema: "reviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ReviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_review_admin_notes", x => x.Id);
                    table.CheckConstraint("CK_review_admin_notes_len", "char_length(\"Note\") BETWEEN 1 AND 4000");
                    table.ForeignKey(
                        name: "FK_review_admin_notes_reviews_ReviewId",
                        column: x => x.ReviewId,
                        principalSchema: "reviews",
                        principalTable: "reviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "review_flags",
                schema: "reviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ReviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReporterActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    IsQualified = table.Column<bool>(type: "boolean", nullable: false),
                    QualifyingEvaluation = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_review_flags", x => x.Id);
                    table.CheckConstraint("CK_review_flags_other_note_required", "\"Reason\" <> 'other_with_required_note' OR (\"Note\" IS NOT NULL AND char_length(\"Note\") >= 10)");
                    table.CheckConstraint("CK_review_flags_reason", "\"Reason\" IN ('inappropriate_language','spam_or_irrelevant','personal_attack','false_or_misleading','other_with_required_note')");
                    table.ForeignKey(
                        name: "FK_review_flags_reviews_ReviewId",
                        column: x => x.ReviewId,
                        principalSchema: "reviews",
                        principalTable: "reviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "review_moderation_decisions",
                schema: "reviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ReviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorRole = table.Column<string>(type: "text", nullable: false),
                    FromState = table.Column<string>(type: "text", nullable: false),
                    ToState = table.Column<string>(type: "text", nullable: false),
                    TriggeredBy = table.Column<string>(type: "text", nullable: false),
                    ReasonNote = table.Column<string>(type: "text", nullable: true),
                    AdminNote = table.Column<string>(type: "text", nullable: true),
                    BeforeJsonb = table.Column<string>(type: "jsonb", nullable: true),
                    AfterJsonb = table.Column<string>(type: "jsonb", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_review_moderation_decisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_review_moderation_decisions_reviews_ReviewId",
                        column: x => x.ReviewId,
                        principalSchema: "reviews",
                        principalTable: "reviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pra_last_updated",
                schema: "reviews",
                table: "product_rating_aggregates",
                column: "LastUpdatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_pra_vendor",
                schema: "reviews",
                table: "product_rating_aggregates",
                column: "VendorId",
                filter: "\"VendorId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_review_admin_notes_review_created",
                schema: "reviews",
                table: "review_admin_notes",
                columns: new[] { "ReviewId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_review_flags_reporter_created",
                schema: "reviews",
                table: "review_flags",
                columns: new[] { "ReporterActorId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_review_flags_review_qualified_recent",
                schema: "reviews",
                table: "review_flags",
                columns: new[] { "ReviewId", "IsQualified", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_review_flags_review_reporter",
                schema: "reviews",
                table: "review_flags",
                columns: new[] { "ReviewId", "ReporterActorId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rmd_actor_created",
                schema: "reviews",
                table: "review_moderation_decisions",
                columns: new[] { "ActorId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_rmd_review_created",
                schema: "reviews",
                table: "review_moderation_decisions",
                columns: new[] { "ReviewId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_reviews_customer_state",
                schema: "reviews",
                table: "reviews",
                columns: new[] { "CustomerId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_reviews_market_pending_age",
                schema: "reviews",
                table: "reviews",
                columns: new[] { "MarketCode", "PendingModerationStartedAt" },
                filter: "\"State\" = 'pending_moderation'");

            migrationBuilder.CreateIndex(
                name: "IX_reviews_product_market_state",
                schema: "reviews",
                table: "reviews",
                columns: new[] { "ProductId", "MarketCode", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_reviews_state",
                schema: "reviews",
                table: "reviews",
                column: "State",
                filter: "\"State\" IN ('pending_moderation','flagged')");

            migrationBuilder.CreateIndex(
                name: "IX_reviews_vendor",
                schema: "reviews",
                table: "reviews",
                column: "VendorId",
                filter: "\"VendorId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_rfw_market",
                schema: "reviews",
                table: "reviews_filter_wordlists",
                column: "MarketCode");

            // FR-008 — one live review per (customer, product). Partial unique index
            // because EF cannot model a unique index whose filter references the
            // text-typed State discriminator portably.
            migrationBuilder.Sql(@"
CREATE UNIQUE INDEX ""UX_reviews_customer_product_active""
    ON reviews.reviews (""CustomerId"", ""ProductId"")
    WHERE ""State"" <> 'deleted';");

            // Append-only enforcement on the 3 audit-detail tables. Same pattern
            // as Verification/Persistence/Migrations/...VerificationInit.cs.
            // Function namespaced to the reviews schema so it never collides
            // with similarly-named functions from other modules.
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION reviews.raise_immutable_audit_violation()
    RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION '% is append-only (TG_OP=%)', TG_TABLE_NAME, TG_OP
        USING ERRCODE = '23000';
    RETURN NULL;
END;
$$;");

            migrationBuilder.Sql(@"
CREATE TRIGGER review_moderation_decisions_append_only_trg
    BEFORE UPDATE OR DELETE ON reviews.review_moderation_decisions
    FOR EACH ROW EXECUTE FUNCTION reviews.raise_immutable_audit_violation();");

            migrationBuilder.Sql(@"
CREATE TRIGGER review_admin_notes_append_only_trg
    BEFORE UPDATE OR DELETE ON reviews.review_admin_notes
    FOR EACH ROW EXECUTE FUNCTION reviews.raise_immutable_audit_violation();");

            migrationBuilder.Sql(@"
CREATE TRIGGER review_flags_append_only_trg
    BEFORE UPDATE OR DELETE ON reviews.review_flags
    FOR EACH ROW EXECUTE FUNCTION reviews.raise_immutable_audit_violation();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS review_flags_append_only_trg ON reviews.review_flags;");
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS review_admin_notes_append_only_trg ON reviews.review_admin_notes;");
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS review_moderation_decisions_append_only_trg ON reviews.review_moderation_decisions;");
            migrationBuilder.Sql(@"DROP FUNCTION IF EXISTS reviews.raise_immutable_audit_violation();");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS reviews.""UX_reviews_customer_product_active"";");

            migrationBuilder.DropTable(
                name: "product_rating_aggregates",
                schema: "reviews");

            migrationBuilder.DropTable(
                name: "review_admin_notes",
                schema: "reviews");

            migrationBuilder.DropTable(
                name: "review_flags",
                schema: "reviews");

            migrationBuilder.DropTable(
                name: "review_moderation_decisions",
                schema: "reviews");

            migrationBuilder.DropTable(
                name: "reviews_filter_wordlists",
                schema: "reviews");

            migrationBuilder.DropTable(
                name: "reviews_market_schemas",
                schema: "reviews");

            migrationBuilder.DropTable(
                name: "reviews",
                schema: "reviews");
        }
    }
}

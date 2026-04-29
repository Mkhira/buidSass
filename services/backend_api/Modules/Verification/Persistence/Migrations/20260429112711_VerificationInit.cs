using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Verification.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VerificationInit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "verification");

            migrationBuilder.CreateTable(
                name: "verification_eligibility_cache",
                schema: "verification",
                columns: table => new
                {
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketCode = table.Column<string>(type: "text", nullable: false),
                    EligibilityClass = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReasonCode = table.Column<string>(type: "text", nullable: true),
                    Professions = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    ComputedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_verification_eligibility_cache", x => new { x.CustomerId, x.MarketCode });
                    table.CheckConstraint("CK_verification_eligibility_cache_class_enum", "\"EligibilityClass\" IN ('eligible','ineligible','unrestricted_only')");
                    table.CheckConstraint("CK_verification_eligibility_cache_market_code_enum", "\"MarketCode\" IN ('eg','ksa')");
                });

            migrationBuilder.CreateTable(
                name: "verification_market_schemas",
                schema: "verification",
                columns: table => new
                {
                    MarketCode = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RequiredFields = table.Column<string>(type: "jsonb", nullable: false),
                    AllowedDocumentTypes = table.Column<string>(type: "jsonb", nullable: false),
                    RetentionMonths = table.Column<int>(type: "integer", nullable: false),
                    CooldownDays = table.Column<int>(type: "integer", nullable: false),
                    ExpiryDays = table.Column<int>(type: "integer", nullable: false),
                    ReminderWindowsDays = table.Column<string>(type: "jsonb", nullable: false),
                    SlaDecisionBusinessDays = table.Column<int>(type: "integer", nullable: false, defaultValue: 2),
                    SlaWarningBusinessDays = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    HolidaysList = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_verification_market_schemas", x => new { x.MarketCode, x.Version });
                    table.CheckConstraint("CK_verification_market_schemas_cooldown_non_negative", "\"CooldownDays\" >= 0");
                    table.CheckConstraint("CK_verification_market_schemas_expiry_positive", "\"ExpiryDays\" > 0");
                    table.CheckConstraint("CK_verification_market_schemas_market_code_enum", "\"MarketCode\" IN ('eg','ksa')");
                    table.CheckConstraint("CK_verification_market_schemas_retention_non_negative", "\"RetentionMonths\" >= 0");
                    table.CheckConstraint("CK_verification_market_schemas_sla_warning_le_decision", "\"SlaWarningBusinessDays\" <= \"SlaDecisionBusinessDays\"");
                });

            migrationBuilder.CreateTable(
                name: "verifications",
                schema: "verification",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketCode = table.Column<string>(type: "text", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    Profession = table.Column<string>(type: "text", nullable: false),
                    RegulatorIdentifier = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecidedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SupersedesId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupersededById = table.Column<Guid>(type: "uuid", nullable: true),
                    VoidReason = table.Column<string>(type: "text", nullable: true),
                    RestrictionPolicySnapshot = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_verifications", x => x.Id);
                    table.CheckConstraint("CK_verifications_market_code_enum", "\"MarketCode\" IN ('eg','ksa')");
                    table.CheckConstraint("CK_verifications_state_enum", "\"State\" IN ('submitted','in-review','info-requested','approved','rejected','expired','revoked','superseded','void')");
                    table.ForeignKey(
                        name: "FK_verifications_verification_market_schemas_MarketCode_Schema~",
                        columns: x => new { x.MarketCode, x.SchemaVersion },
                        principalSchema: "verification",
                        principalTable: "verification_market_schemas",
                        principalColumns: new[] { "MarketCode", "Version" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_verifications_verifications_SupersededById",
                        column: x => x.SupersededById,
                        principalSchema: "verification",
                        principalTable: "verifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_verifications_verifications_SupersedesId",
                        column: x => x.SupersedesId,
                        principalSchema: "verification",
                        principalTable: "verifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "verification_documents",
                schema: "verification",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VerificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketCode = table.Column<string>(type: "text", nullable: false),
                    StorageKey = table.Column<string>(type: "text", nullable: true),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ScanStatus = table.Column<string>(type: "text", nullable: false, defaultValue: "pending"),
                    UploadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PurgeAfter = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PurgedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_verification_documents", x => x.Id);
                    table.CheckConstraint("CK_verification_documents_content_type_allowlist", "\"ContentType\" IN ('application/pdf','image/jpeg','image/png','image/heic')");
                    table.CheckConstraint("CK_verification_documents_market_code_enum", "\"MarketCode\" IN ('eg','ksa')");
                    table.CheckConstraint("CK_verification_documents_scan_status_enum", "\"ScanStatus\" IN ('pending','clean','infected','error')");
                    table.CheckConstraint("CK_verification_documents_size_bytes_limit", "\"SizeBytes\" > 0 AND \"SizeBytes\" <= 10485760");
                    table.ForeignKey(
                        name: "FK_verification_documents_verifications_VerificationId",
                        column: x => x.VerificationId,
                        principalSchema: "verification",
                        principalTable: "verifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "verification_reminders",
                schema: "verification",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VerificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketCode = table.Column<string>(type: "text", nullable: false),
                    WindowDays = table.Column<int>(type: "integer", nullable: false),
                    EmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Skipped = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    SkipReason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_verification_reminders", x => x.Id);
                    table.CheckConstraint("CK_verification_reminders_market_code_enum", "\"MarketCode\" IN ('eg','ksa')");
                    table.CheckConstraint("CK_verification_reminders_skip_reason_when_skipped", "\"Skipped\" = false OR (\"Skipped\" = true AND \"SkipReason\" IS NOT NULL)");
                    table.CheckConstraint("CK_verification_reminders_window_positive", "\"WindowDays\" > 0");
                    table.ForeignKey(
                        name: "FK_verification_reminders_verifications_VerificationId",
                        column: x => x.VerificationId,
                        principalSchema: "verification",
                        principalTable: "verifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "verification_state_transitions",
                schema: "verification",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VerificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketCode = table.Column<string>(type: "text", nullable: false),
                    PriorState = table.Column<string>(type: "text", nullable: false),
                    NewState = table.Column<string>(type: "text", nullable: false),
                    ActorKind = table.Column<string>(type: "text", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    Metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_verification_state_transitions", x => x.Id);
                    table.CheckConstraint("CK_verification_state_transitions_actor_id_by_kind", "(\"ActorKind\" = 'system' AND \"ActorId\" IS NULL) OR (\"ActorKind\" IN ('customer','reviewer') AND \"ActorId\" IS NOT NULL)");
                    table.CheckConstraint("CK_verification_state_transitions_actor_kind_enum", "\"ActorKind\" IN ('customer','reviewer','system')");
                    table.CheckConstraint("CK_verification_state_transitions_market_code_enum", "\"MarketCode\" IN ('eg','ksa')");
                    table.CheckConstraint("CK_verification_state_transitions_new_state_enum", "\"NewState\" IN ('submitted','in-review','info-requested','approved','rejected','expired','revoked','superseded','void')");
                    table.CheckConstraint("CK_verification_state_transitions_prior_state_enum", "\"PriorState\" IN ('__none__','submitted','in-review','info-requested','approved','rejected','expired','revoked','superseded','void')");
                    table.ForeignKey(
                        name: "FK_verification_state_transitions_verifications_VerificationId",
                        column: x => x.VerificationId,
                        principalSchema: "verification",
                        principalTable: "verifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_verification_documents_market_verification",
                schema: "verification",
                table: "verification_documents",
                columns: new[] { "MarketCode", "VerificationId" });

            migrationBuilder.CreateIndex(
                name: "IX_verification_documents_purge_after",
                schema: "verification",
                table: "verification_documents",
                column: "PurgeAfter",
                filter: "\"PurgedAt\" IS NULL AND \"PurgeAfter\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_verification_documents_verification",
                schema: "verification",
                table: "verification_documents",
                column: "VerificationId");

            migrationBuilder.CreateIndex(
                name: "IX_verification_eligibility_cache_market_class",
                schema: "verification",
                table: "verification_eligibility_cache",
                columns: new[] { "MarketCode", "EligibilityClass" });

            migrationBuilder.CreateIndex(
                name: "UX_verification_market_schemas_active_per_market",
                schema: "verification",
                table: "verification_market_schemas",
                column: "MarketCode",
                unique: true,
                filter: "\"EffectiveTo\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_verification_reminders_market_verification",
                schema: "verification",
                table: "verification_reminders",
                columns: new[] { "MarketCode", "VerificationId" });

            migrationBuilder.CreateIndex(
                name: "UX_verification_reminders_verification_window",
                schema: "verification",
                table: "verification_reminders",
                columns: new[] { "VerificationId", "WindowDays" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_verification_state_transitions_market_occurred",
                schema: "verification",
                table: "verification_state_transitions",
                columns: new[] { "MarketCode", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_verification_state_transitions_verification_occurred",
                schema: "verification",
                table: "verification_state_transitions",
                columns: new[] { "VerificationId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_verifications_customer_state_market",
                schema: "verification",
                table: "verifications",
                columns: new[] { "CustomerId", "State", "MarketCode" });

            migrationBuilder.CreateIndex(
                name: "IX_verifications_expires_at",
                schema: "verification",
                table: "verifications",
                column: "ExpiresAt",
                filter: "\"State\" = 'approved'");

            migrationBuilder.CreateIndex(
                name: "IX_verifications_MarketCode_SchemaVersion",
                schema: "verification",
                table: "verifications",
                columns: new[] { "MarketCode", "SchemaVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_verifications_state_market_submitted",
                schema: "verification",
                table: "verifications",
                columns: new[] { "State", "MarketCode", "SubmittedAt" },
                filter: "\"State\" IN ('submitted','in-review','info-requested')");

            migrationBuilder.CreateIndex(
                name: "IX_verifications_SupersededById",
                schema: "verification",
                table: "verifications",
                column: "SupersededById");

            migrationBuilder.CreateIndex(
                name: "IX_verifications_supersedes",
                schema: "verification",
                table: "verifications",
                column: "SupersedesId",
                filter: "\"SupersedesId\" IS NOT NULL");

            // Concurrency guard on renewals: at most one non-terminal row may
            // point at a given prior approval at any time. Without this,
            // RequestRenewalHandler's AnyAsync pre-check is racy — two
            // concurrent requests can both observe "no pending renewal" and
            // both INSERT a 'submitted' row for the same SupersedesId. Raw SQL
            // because EF cannot model two distinct indexes on the same column
            // expression (the non-unique IX_verifications_supersedes above
            // serves general supersession lookups).
            migrationBuilder.Sql(@"
CREATE UNIQUE INDEX ""UX_verifications_one_pending_renewal_per_approval""
    ON verification.verifications (""SupersedesId"")
    WHERE ""SupersedesId"" IS NOT NULL
      AND ""State"" IN ('submitted','in-review','info-requested');");

            // Append-only enforcement on verification_state_transitions per spec 020
            // data-model §2.3. Postgres trigger blocks UPDATE/DELETE so the audit-faithful
            // history can never be silently rewritten — paired with the EF-side rule that
            // handlers only ever INSERT into this table.
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION verification.verification_state_transitions_append_only()
    RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'verification_state_transitions is append-only (TG_OP=%)', TG_OP
        USING ERRCODE = '23000';
    RETURN NULL;
END;
$$;");

            migrationBuilder.Sql(@"
CREATE TRIGGER verification_state_transitions_append_only_trg
    BEFORE UPDATE OR DELETE ON verification.verification_state_transitions
    FOR EACH ROW EXECUTE FUNCTION verification.verification_state_transitions_append_only();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS verification_state_transitions_append_only_trg ON verification.verification_state_transitions;");
            migrationBuilder.Sql(@"DROP FUNCTION IF EXISTS verification.verification_state_transitions_append_only();");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS verification.""UX_verifications_one_pending_renewal_per_approval"";");

            migrationBuilder.DropTable(
                name: "verification_documents",
                schema: "verification");

            migrationBuilder.DropTable(
                name: "verification_eligibility_cache",
                schema: "verification");

            migrationBuilder.DropTable(
                name: "verification_reminders",
                schema: "verification");

            migrationBuilder.DropTable(
                name: "verification_state_transitions",
                schema: "verification");

            migrationBuilder.DropTable(
                name: "verifications",
                schema: "verification");

            migrationBuilder.DropTable(
                name: "verification_market_schemas",
                schema: "verification");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.B2B.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class B2BInit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "b2b");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "companies",
                schema: "b2b",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "jsonb", nullable: false),
                    tax_id = table.Column<string>(type: "text", maxLength: 64, nullable: false),
                    market_code = table.Column<string>(type: "citext", nullable: false),
                    primary_address = table.Column<string>(type: "jsonb", nullable: false),
                    billing_address = table.Column<string>(type: "jsonb", nullable: true),
                    approver_required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    po_required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    unique_po_required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    invoice_billing_eligible = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    state = table.Column<string>(type: "citext", nullable: false, defaultValue: "active"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_companies", x => x.Id);
                    table.CheckConstraint("chk_companies_market", "market_code::text IN ('eg','ksa')");
                    table.CheckConstraint("chk_companies_state", "state::text IN ('active','pending-verification','suspended','closed')");
                });

            migrationBuilder.CreateTable(
                name: "quote_market_schemas",
                schema: "b2b",
                columns: table => new
                {
                    market_code = table.Column<string>(type: "citext", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    validity_days = table.Column<int>(type: "integer", nullable: false, defaultValue: 14),
                    rate_limit_per_customer_per_hour = table.Column<int>(type: "integer", nullable: false, defaultValue: 10),
                    rate_limit_per_company_per_hour = table.Column<int>(type: "integer", nullable: false, defaultValue: 50),
                    company_verification_required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    tax_preview_drift_threshold_pct = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 5.00m),
                    sla_decision_business_days = table.Column<int>(type: "integer", nullable: false, defaultValue: 2),
                    sla_warning_business_days = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    invitation_ttl_days = table.Column<int>(type: "integer", nullable: false, defaultValue: 14),
                    holidays_list = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quote_market_schemas", x => new { x.market_code, x.version });
                    table.CheckConstraint("chk_qms_drift_in_range", "tax_preview_drift_threshold_pct >= 0 AND tax_preview_drift_threshold_pct <= 100");
                    table.CheckConstraint("chk_qms_invitation_ttl_positive", "invitation_ttl_days > 0");
                    table.CheckConstraint("chk_qms_market", "market_code::text IN ('eg','ksa')");
                    table.CheckConstraint("chk_qms_rate_limits_positive", "rate_limit_per_customer_per_hour > 0 AND rate_limit_per_company_per_hour > 0");
                    table.CheckConstraint("chk_qms_sla_nonneg", "sla_decision_business_days >= 0 AND sla_warning_business_days >= 0");
                    table.CheckConstraint("chk_qms_validity_positive", "validity_days > 0");
                });

            migrationBuilder.CreateTable(
                name: "company_branches",
                schema: "b2b",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    market_code = table.Column<string>(type: "citext", maxLength: 8, nullable: false),
                    name = table.Column<string>(type: "jsonb", nullable: false),
                    address = table.Column<string>(type: "jsonb", nullable: false),
                    contact_phone = table.Column<string>(type: "text", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_branches", x => x.Id);
                    table.CheckConstraint("chk_company_branches_market", "market_code::text IN ('eg','ksa')");
                    table.ForeignKey(
                        name: "FK_company_branches_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "b2b",
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "company_invitations",
                schema: "b2b",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    market_code = table.Column<string>(type: "citext", maxLength: 8, nullable: false),
                    invited_by = table.Column<Guid>(type: "uuid", nullable: false),
                    invited_email = table.Column<string>(type: "citext", maxLength: 320, nullable: false),
                    target_role = table.Column<string>(type: "citext", nullable: false),
                    token_hash = table.Column<string>(type: "text", maxLength: 128, nullable: false),
                    state = table.Column<string>(type: "citext", nullable: false, defaultValue: "pending"),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_invitations", x => x.Id);
                    table.CheckConstraint("chk_company_invitations_market", "market_code::text IN ('eg','ksa')");
                    table.CheckConstraint("chk_company_invitations_role", "target_role::text IN ('companies.admin','buyer','approver')");
                    table.CheckConstraint("chk_company_invitations_state", "state::text IN ('pending','accepted','declined','expired')");
                    table.ForeignKey(
                        name: "FK_company_invitations_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "b2b",
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "company_memberships",
                schema: "b2b",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    market_code = table.Column<string>(type: "citext", maxLength: 8, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "citext", nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_memberships", x => x.Id);
                    table.CheckConstraint("chk_company_memberships_market", "market_code::text IN ('eg','ksa')");
                    table.CheckConstraint("chk_company_memberships_role", "role::text IN ('companies.admin','buyer','approver')");
                    table.ForeignKey(
                        name: "FK_company_memberships_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "b2b",
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "quote_state_transitions",
                schema: "b2b",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    quote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    market_code = table.Column<string>(type: "citext", maxLength: 8, nullable: false),
                    prior_state = table.Column<string>(type: "citext", nullable: false),
                    new_state = table.Column<string>(type: "citext", nullable: false),
                    actor_kind = table.Column<string>(type: "citext", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "jsonb", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quote_state_transitions", x => x.Id);
                    table.CheckConstraint("chk_qst_actor_kind", "actor_kind::text IN ('customer','buyer','approver','admin_operator','system')");
                    table.CheckConstraint("chk_qst_market", "market_code::text IN ('eg','ksa')");
                    table.CheckConstraint("chk_qst_new_state", "new_state::text IN ('requested','drafted','revised','pending-approver','accepted','rejected','expired','withdrawn')");
                    table.CheckConstraint("chk_qst_prior_state", "prior_state::text IN ('__none__','requested','drafted','revised','pending-approver','accepted','rejected','expired','withdrawn')");
                });

            migrationBuilder.CreateTable(
                name: "quote_version_documents",
                schema: "b2b",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    quote_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    market_code = table.Column<string>(type: "citext", maxLength: 8, nullable: false),
                    locale = table.Column<string>(type: "citext", nullable: false),
                    storage_key = table.Column<string>(type: "text", maxLength: 1024, nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false, defaultValue: "application/pdf"),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quote_version_documents", x => x.Id);
                    table.CheckConstraint("chk_qvd_locale", "locale::text IN ('en','ar')");
                    table.CheckConstraint("chk_qvd_market", "market_code::text IN ('eg','ksa')");
                });

            migrationBuilder.CreateTable(
                name: "quote_versions",
                schema: "b2b",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    quote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    market_code = table.Column<string>(type: "citext", maxLength: 8, nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    authored_by = table.Column<Guid>(type: "uuid", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    line_items = table.Column<string>(type: "jsonb", nullable: false),
                    terms_text = table.Column<string>(type: "jsonb", nullable: false),
                    terms_days = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    validity_extends = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    totals_summary = table.Column<string>(type: "jsonb", nullable: false),
                    customer_revision_comment = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quote_versions", x => x.Id);
                    table.CheckConstraint("chk_quote_versions_market", "market_code::text IN ('eg','ksa')");
                    table.CheckConstraint("chk_quote_versions_terms_days_nonneg", "terms_days >= 0");
                    table.CheckConstraint("chk_quote_versions_version_positive", "version_number > 0");
                });

            migrationBuilder.CreateTable(
                name: "quotes",
                schema: "b2b",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    market_code = table.Column<string>(type: "citext", nullable: false),
                    state = table.Column<string>(type: "citext", nullable: false, defaultValue: "requested"),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    current_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    terminal_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    terminal_reason = table.Column<string>(type: "citext", nullable: true),
                    po_number = table.Column<string>(type: "text", maxLength: 128, nullable: true),
                    invoice_billing = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    customer_supplied_message = table.Column<string>(type: "jsonb", nullable: true),
                    internal_note = table.Column<string>(type: "text", nullable: true),
                    approver_rejection_note = table.Column<string>(type: "text", nullable: true),
                    originating_cart_snapshot = table.Column<string>(type: "jsonb", nullable: true),
                    originating_product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    restriction_policy_snapshot = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quotes", x => x.Id);
                    table.CheckConstraint("chk_quotes_branch_requires_company", "branch_id IS NULL OR company_id IS NOT NULL");
                    table.CheckConstraint("chk_quotes_market", "market_code::text IN ('eg','ksa')");
                    table.CheckConstraint("chk_quotes_state", "state::text IN ('requested','drafted','revised','pending-approver','accepted','rejected','expired','withdrawn')");
                    table.ForeignKey(
                        name: "FK_quotes_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "b2b",
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_quotes_company_branches_branch_id",
                        column: x => x.branch_id,
                        principalSchema: "b2b",
                        principalTable: "company_branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_quotes_quote_versions_current_version_id",
                        column: x => x.current_version_id,
                        principalSchema: "b2b",
                        principalTable: "quote_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "repeat_order_templates",
                schema: "b2b",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    market_code = table.Column<string>(type: "citext", maxLength: 8, nullable: false),
                    source_quote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_repeat_order_templates", x => x.Id);
                    table.CheckConstraint("chk_repeat_order_templates_market", "market_code::text IN ('eg','ksa')");
                    table.ForeignKey(
                        name: "FK_repeat_order_templates_quotes_source_quote_id",
                        column: x => x.source_quote_id,
                        principalSchema: "b2b",
                        principalTable: "quotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_companies_state",
                schema: "b2b",
                table: "companies",
                column: "state",
                filter: "state::text <> 'closed'");

            migrationBuilder.CreateIndex(
                name: "UX_companies_market_tax_id",
                schema: "b2b",
                table: "companies",
                columns: new[] { "market_code", "tax_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_company_branches_company",
                schema: "b2b",
                table: "company_branches",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_company_invitations_state_expires",
                schema: "b2b",
                table: "company_invitations",
                columns: new[] { "state", "expires_at" },
                filter: "state::text = 'pending'");

            migrationBuilder.CreateIndex(
                name: "UX_company_invitations_open_per_company_email_role",
                schema: "b2b",
                table: "company_invitations",
                columns: new[] { "company_id", "invited_email", "target_role" },
                unique: true,
                filter: "state::text = 'pending'");

            migrationBuilder.CreateIndex(
                name: "UX_company_invitations_token_hash",
                schema: "b2b",
                table: "company_invitations",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_company_memberships_company_role",
                schema: "b2b",
                table: "company_memberships",
                columns: new[] { "company_id", "role" });

            migrationBuilder.CreateIndex(
                name: "IX_company_memberships_user",
                schema: "b2b",
                table: "company_memberships",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "UX_company_memberships_company_user_role",
                schema: "b2b",
                table: "company_memberships",
                columns: new[] { "company_id", "user_id", "role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_quote_market_schemas_active_per_market",
                schema: "b2b",
                table: "quote_market_schemas",
                column: "market_code",
                unique: true,
                filter: "effective_to IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_quote_state_transitions_quote_occurred",
                schema: "b2b",
                table: "quote_state_transitions",
                columns: new[] { "quote_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "UX_quote_version_documents_version_locale",
                schema: "b2b",
                table: "quote_version_documents",
                columns: new[] { "quote_version_id", "locale" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_quote_versions_quote_version_market",
                schema: "b2b",
                table: "quote_versions",
                columns: new[] { "quote_id", "version_number", "market_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_quotes_branch_id",
                schema: "b2b",
                table: "quotes",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "IX_quotes_company_state_market",
                schema: "b2b",
                table: "quotes",
                columns: new[] { "company_id", "state", "market_code" },
                filter: "company_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_quotes_current_version_id",
                schema: "b2b",
                table: "quotes",
                column: "current_version_id");

            migrationBuilder.CreateIndex(
                name: "IX_quotes_customer_state",
                schema: "b2b",
                table: "quotes",
                columns: new[] { "customer_id", "state" });

            migrationBuilder.CreateIndex(
                name: "IX_quotes_expires_at",
                schema: "b2b",
                table: "quotes",
                column: "expires_at",
                filter: "state::text IN ('revised','pending-approver')");

            migrationBuilder.CreateIndex(
                name: "IX_quotes_state_market_requested",
                schema: "b2b",
                table: "quotes",
                columns: new[] { "state", "market_code", "requested_at" },
                filter: "state::text IN ('requested','drafted','revised','pending-approver')");

            migrationBuilder.CreateIndex(
                name: "UX_quotes_company_po",
                schema: "b2b",
                table: "quotes",
                columns: new[] { "company_id", "po_number" },
                unique: true,
                filter: "company_id IS NOT NULL AND po_number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_repeat_order_templates_source_quote_id",
                schema: "b2b",
                table: "repeat_order_templates",
                column: "source_quote_id");

            migrationBuilder.CreateIndex(
                name: "UX_repeat_order_templates_company_name",
                schema: "b2b",
                table: "repeat_order_templates",
                columns: new[] { "company_id", "name" },
                unique: true,
                filter: "company_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_repeat_order_templates_user_name",
                schema: "b2b",
                table: "repeat_order_templates",
                columns: new[] { "user_id", "name" },
                unique: true,
                filter: "company_id IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_quote_state_transitions_quotes_quote_id",
                schema: "b2b",
                table: "quote_state_transitions",
                column: "quote_id",
                principalSchema: "b2b",
                principalTable: "quotes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_quote_version_documents_quote_versions_quote_version_id",
                schema: "b2b",
                table: "quote_version_documents",
                column: "quote_version_id",
                principalSchema: "b2b",
                principalTable: "quote_versions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_quote_versions_quotes_quote_id",
                schema: "b2b",
                table: "quote_versions",
                column: "quote_id",
                principalSchema: "b2b",
                principalTable: "quotes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // Append-only guard for the quote_state_transitions ledger (data-model §2.8).
            // UPDATE / DELETE on this table is forbidden — the audit trail must be
            // monotonic. Asserted by Tests/B2B.Tests/Integration/StateTransitionAppendOnlyTriggerTests.
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION b2b.raise_immutable_state_transition_violation()
RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'b2b.quote_state_transitions is append-only (operation: %)', TG_OP
        USING ERRCODE = '23514';
END;
$$;");
            migrationBuilder.Sql(@"
CREATE TRIGGER quote_state_transitions_append_only
BEFORE UPDATE OR DELETE ON b2b.quote_state_transitions
FOR EACH ROW EXECUTE FUNCTION b2b.raise_immutable_state_transition_violation();");

            // Row-level immutability for quote_versions (data-model §2.6). Verified by
            // QuoteVersionImmutabilityTests. DELETE is permitted because cascade-delete
            // from `quotes` is the only way a version goes away.
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION b2b.raise_immutable_quote_version_violation()
RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'b2b.quote_versions is immutable per row (operation: %)', TG_OP
        USING ERRCODE = '23514';
END;
$$;");
            migrationBuilder.Sql(@"
CREATE TRIGGER quote_versions_immutable
BEFORE UPDATE ON b2b.quote_versions
FOR EACH ROW EXECUTE FUNCTION b2b.raise_immutable_quote_version_violation();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop triggers + functions first so the table drops below succeed.
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS quote_versions_immutable ON b2b.quote_versions;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS b2b.raise_immutable_quote_version_violation();");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS quote_state_transitions_append_only ON b2b.quote_state_transitions;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS b2b.raise_immutable_state_transition_violation();");

            migrationBuilder.DropForeignKey(
                name: "FK_company_branches_companies_company_id",
                schema: "b2b",
                table: "company_branches");

            migrationBuilder.DropForeignKey(
                name: "FK_quotes_companies_company_id",
                schema: "b2b",
                table: "quotes");

            migrationBuilder.DropForeignKey(
                name: "FK_quote_versions_quotes_quote_id",
                schema: "b2b",
                table: "quote_versions");

            migrationBuilder.DropTable(
                name: "company_invitations",
                schema: "b2b");

            migrationBuilder.DropTable(
                name: "company_memberships",
                schema: "b2b");

            migrationBuilder.DropTable(
                name: "quote_market_schemas",
                schema: "b2b");

            migrationBuilder.DropTable(
                name: "quote_state_transitions",
                schema: "b2b");

            migrationBuilder.DropTable(
                name: "quote_version_documents",
                schema: "b2b");

            migrationBuilder.DropTable(
                name: "repeat_order_templates",
                schema: "b2b");

            migrationBuilder.DropTable(
                name: "companies",
                schema: "b2b");

            migrationBuilder.DropTable(
                name: "quotes",
                schema: "b2b");

            migrationBuilder.DropTable(
                name: "company_branches",
                schema: "b2b");

            migrationBuilder.DropTable(
                name: "quote_versions",
                schema: "b2b");
        }
    }
}

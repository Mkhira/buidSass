using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Support.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "support");

            migrationBuilder.CreateTable(
                name: "agent_availability",
                schema: "support",
                columns: table => new
                {
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketCode = table.Column<string>(type: "text", nullable: false),
                    IsOnCall = table.Column<bool>(type: "boolean", nullable: false),
                    LastToggledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastToggledByActorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_availability", x => new { x.AgentId, x.MarketCode });
                    table.CheckConstraint("CK_agent_availability_market_code", "\"MarketCode\" IN ('SA','EG')");
                });

            migrationBuilder.CreateTable(
                name: "sla_policies",
                schema: "support",
                columns: table => new
                {
                    MarketCode = table.Column<string>(type: "text", nullable: false),
                    Priority = table.Column<string>(type: "text", nullable: false),
                    FirstResponseTargetMinutes = table.Column<int>(type: "integer", nullable: false),
                    ResolutionTargetMinutes = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByActorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sla_policies", x => new { x.MarketCode, x.Priority });
                    table.CheckConstraint("CK_sla_policies_market_code", "\"MarketCode\" IN ('SA','EG')");
                    table.CheckConstraint("CK_sla_policies_priority", "\"Priority\" IN ('low','normal','high','urgent')");
                    table.CheckConstraint("CK_sla_policies_resolution_gt_first", "\"ResolutionTargetMinutes\" > \"FirstResponseTargetMinutes\"");
                    table.CheckConstraint("CK_sla_policies_targets_positive", "\"FirstResponseTargetMinutes\" > 0 AND \"ResolutionTargetMinutes\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "support_market_schemas",
                schema: "support",
                columns: table => new
                {
                    MarketCode = table.Column<string>(type: "text", nullable: false),
                    AutoAssignmentEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ReopenWindowDays = table.Column<int>(type: "integer", nullable: false),
                    MaxReopenCount = table.Column<int>(type: "integer", nullable: false),
                    AutoCloseAfterResolvedDays = table.Column<int>(type: "integer", nullable: false),
                    AttachmentMaxPerTicket = table.Column<int>(type: "integer", nullable: false),
                    AttachmentMaxSizeMb = table.Column<int>(type: "integer", nullable: false),
                    AttachmentCumulativeMaxMb = table.Column<int>(type: "integer", nullable: false),
                    AllowedMimeTypes = table.Column<string[]>(type: "text[]", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByActorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_market_schemas", x => x.MarketCode);
                    table.CheckConstraint("CK_market_schemas_attachment_caps", "\"AttachmentMaxPerTicket\" > 0 AND \"AttachmentMaxSizeMb\" > 0 AND \"AttachmentCumulativeMaxMb\" > 0");
                    table.CheckConstraint("CK_market_schemas_auto_close", "\"AutoCloseAfterResolvedDays\" BETWEEN 0 AND 30");
                    table.CheckConstraint("CK_market_schemas_market_code", "\"MarketCode\" IN ('SA','EG')");
                    table.CheckConstraint("CK_market_schemas_max_reopen", "\"MaxReopenCount\" BETWEEN 1 AND 10");
                    table.CheckConstraint("CK_market_schemas_reopen_window", "\"ReopenWindowDays\" BETWEEN 0 AND 60");
                });

            migrationBuilder.CreateTable(
                name: "tickets",
                schema: "support",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: true),
                    MarketCode = table.Column<string>(type: "text", nullable: false),
                    Locale = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    Priority = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    Subject = table.Column<string>(type: "text", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    LinkedEntityKind = table.Column<string>(type: "text", nullable: true),
                    LinkedEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedAgentId = table.Column<Guid>(type: "uuid", nullable: true),
                    FirstResponseTargetMinutesSnapshot = table.Column<int>(type: "integer", nullable: false),
                    ResolutionTargetMinutesSnapshot = table.Column<int>(type: "integer", nullable: false),
                    FirstResponseDueUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolutionDueUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    BreachAcknowledgedAtFirstResponse = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    BreachAcknowledgedAtResolution = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReopenCount = table.Column<int>(type: "integer", nullable: false),
                    ReopenedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tickets", x => x.Id);
                    table.CheckConstraint("CK_tickets_body_len", "char_length(\"Body\") BETWEEN 1 AND 8000");
                    table.CheckConstraint("CK_tickets_category", "\"Category\" IN ('order_issue','payment_issue','shipping_issue','product_defect','return_refund_request','quote_question','account_verification','general_question','review_dispute','verification_query','redaction_request')");
                    table.CheckConstraint("CK_tickets_linked_entity_kind", "\"LinkedEntityKind\" IS NULL OR \"LinkedEntityKind\" IN ('order','order_line','return_request','quote','review','verification')");
                    table.CheckConstraint("CK_tickets_linked_kind_consistency", "(\"LinkedEntityKind\" IS NULL AND \"LinkedEntityId\" IS NULL) OR (\"LinkedEntityKind\" IS NOT NULL AND \"LinkedEntityId\" IS NOT NULL)");
                    table.CheckConstraint("CK_tickets_locale", "\"Locale\" IN ('ar','en')");
                    table.CheckConstraint("CK_tickets_market_code", "\"MarketCode\" IN ('SA','EG')");
                    table.CheckConstraint("CK_tickets_priority", "\"Priority\" IN ('low','normal','high','urgent')");
                    table.CheckConstraint("CK_tickets_reopen_count_nonneg", "\"ReopenCount\" >= 0");
                    table.CheckConstraint("CK_tickets_sla_targets_positive", "\"FirstResponseTargetMinutesSnapshot\" > 0 AND \"ResolutionTargetMinutesSnapshot\" > 0");
                    table.CheckConstraint("CK_tickets_state", "\"State\" IN ('open','in_progress','waiting_customer','resolved','closed')");
                    table.CheckConstraint("CK_tickets_subject_len", "char_length(\"Subject\") BETWEEN 1 AND 150");
                });

            migrationBuilder.CreateTable(
                name: "ticket_assignments",
                schema: "support",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignmentKind = table.Column<string>(type: "text", nullable: false),
                    AssignedByActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    JustificationNote = table.Column<string>(type: "text", nullable: true),
                    AssignedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SupersededAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SupersededReason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_assignments", x => x.Id);
                    table.CheckConstraint("CK_ticket_assignments_kind", "\"AssignmentKind\" IN ('self_claim','auto_assignment','lead_reassignment','reclaim_after_offboard')");
                    table.CheckConstraint("CK_ticket_assignments_supersede_consistency", "(\"SupersededAtUtc\" IS NULL AND \"SupersededReason\" IS NULL) OR (\"SupersededAtUtc\" IS NOT NULL AND \"SupersededReason\" IS NOT NULL)");
                    table.CheckConstraint("CK_ticket_assignments_superseded_reason", "\"SupersededReason\" IS NULL OR \"SupersededReason\" IN ('reassigned','reclaimed_offboard','reopened_back_to_queue')");
                    table.ForeignKey(
                        name: "FK_ticket_assignments_tickets_TicketId",
                        column: x => x.TicketId,
                        principalSchema: "support",
                        principalTable: "tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ticket_links",
                schema: "support",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    LinkedEntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedVia = table.Column<string>(type: "text", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_links", x => x.Id);
                    table.CheckConstraint("CK_ticket_links_created_via", "\"CreatedVia\" IN ('submission','conversion','lead_link')");
                    table.CheckConstraint("CK_ticket_links_kind", "\"Kind\" IN ('order','order_line','return_request','quote','review','verification')");
                    table.ForeignKey(
                        name: "FK_ticket_links_tickets_TicketId",
                        column: x => x.TicketId,
                        principalSchema: "support",
                        principalTable: "tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ticket_messages",
                schema: "support",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorRole = table.Column<string>(type: "text", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: true),
                    BodyLocale = table.Column<string>(type: "text", nullable: true),
                    LeadIntervention = table.Column<bool>(type: "boolean", nullable: false),
                    RedactedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RedactedByRole = table.Column<string>(type: "text", nullable: true),
                    OriginatingRedactionRequestTicketId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_messages", x => x.Id);
                    table.CheckConstraint("CK_ticket_messages_body_len", "\"Body\" IS NULL OR char_length(\"Body\") BETWEEN 1 AND 8000");
                    table.CheckConstraint("CK_ticket_messages_body_locale", "\"BodyLocale\" IS NULL OR \"BodyLocale\" IN ('ar','en')");
                    table.CheckConstraint("CK_ticket_messages_kind", "\"Kind\" IN ('customer_reply','agent_reply','internal_note','system_event')");
                    table.ForeignKey(
                        name: "FK_ticket_messages_tickets_TicketId",
                        column: x => x.TicketId,
                        principalSchema: "support",
                        principalTable: "tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ticket_sla_breach_events",
                schema: "support",
                columns: table => new
                {
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    BreachKind = table.Column<string>(type: "text", nullable: false),
                    DetectedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TargetDueUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EventEmitted = table.Column<bool>(type: "boolean", nullable: false),
                    SupersededByEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_sla_breach_events", x => new { x.TicketId, x.BreachKind, x.DetectedAtUtc });
                    table.CheckConstraint("CK_breach_events_kind", "\"BreachKind\" IN ('first_response','resolution')");
                    table.ForeignKey(
                        name: "FK_ticket_sla_breach_events_tickets_TicketId",
                        column: x => x.TicketId,
                        principalSchema: "support",
                        principalTable: "tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ticket_attachments",
                schema: "support",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageObjectId = table.Column<string>(type: "text", nullable: true),
                    MimeType = table.Column<string>(type: "text", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    OriginalFilename = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false, defaultValue: "active"),
                    RedactedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RedactedByRole = table.Column<string>(type: "text", nullable: true),
                    RedactionReasonNote = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_attachments", x => x.Id);
                    table.CheckConstraint("CK_ticket_attachments_redaction_consistency", "(\"State\" = 'active' AND \"StorageObjectId\" IS NOT NULL AND \"RedactedAtUtc\" IS NULL) OR (\"State\" = 'redacted' AND \"RedactedAtUtc\" IS NOT NULL)");
                    table.CheckConstraint("CK_ticket_attachments_size_bytes", "\"SizeBytes\" >= 0");
                    table.CheckConstraint("CK_ticket_attachments_state", "\"State\" IN ('active','redacted')");
                    table.ForeignKey(
                        name: "FK_ticket_attachments_ticket_messages_MessageId",
                        column: x => x.MessageId,
                        principalSchema: "support",
                        principalTable: "ticket_messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ticket_assignments_ticket",
                schema: "support",
                table: "ticket_assignments",
                columns: new[] { "TicketId", "AssignedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_ticket_assignments_active_per_ticket",
                schema: "support",
                table: "ticket_assignments",
                column: "TicketId",
                unique: true,
                filter: "\"SupersededAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_attachments_message",
                schema: "support",
                table: "ticket_attachments",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_attachments_ticket",
                schema: "support",
                table: "ticket_attachments",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_links_lookup",
                schema: "support",
                table: "ticket_links",
                columns: new[] { "Kind", "LinkedEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_ticket_links_ticket",
                schema: "support",
                table: "ticket_links",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "UX_ticket_links_idempotency_key",
                schema: "support",
                table: "ticket_links",
                columns: new[] { "TicketId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_messages_ticket",
                schema: "support",
                table: "ticket_messages",
                columns: new[] { "TicketId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ticket_messages_ticket_kind",
                schema: "support",
                table: "ticket_messages",
                columns: new[] { "TicketId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_breach_events_ticket_kind",
                schema: "support",
                table: "ticket_sla_breach_events",
                columns: new[] { "TicketId", "BreachKind" });

            migrationBuilder.CreateIndex(
                name: "UX_breach_events_surrogate_id",
                schema: "support",
                table: "ticket_sla_breach_events",
                column: "Id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tickets_assigned_agent",
                schema: "support",
                table: "tickets",
                columns: new[] { "AssignedAgentId", "State" },
                filter: "\"State\" IN ('in_progress','waiting_customer')");

            migrationBuilder.CreateIndex(
                name: "IX_tickets_breach_scan",
                schema: "support",
                table: "tickets",
                columns: new[] { "State", "FirstResponseDueUtc", "ResolutionDueUtc" },
                filter: "\"State\" NOT IN ('closed')");

            migrationBuilder.CreateIndex(
                name: "IX_tickets_company",
                schema: "support",
                table: "tickets",
                columns: new[] { "CompanyId", "State" },
                filter: "\"CompanyId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_tickets_customer",
                schema: "support",
                table: "tickets",
                columns: new[] { "CustomerId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_tickets_linked_entity",
                schema: "support",
                table: "tickets",
                columns: new[] { "LinkedEntityKind", "LinkedEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_tickets_queue",
                schema: "support",
                table: "tickets",
                columns: new[] { "MarketCode", "State", "FirstResponseDueUtc", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_tickets_vendor",
                schema: "support",
                table: "tickets",
                column: "VendorId",
                filter: "\"VendorId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_availability",
                schema: "support");

            migrationBuilder.DropTable(
                name: "sla_policies",
                schema: "support");

            migrationBuilder.DropTable(
                name: "support_market_schemas",
                schema: "support");

            migrationBuilder.DropTable(
                name: "ticket_assignments",
                schema: "support");

            migrationBuilder.DropTable(
                name: "ticket_attachments",
                schema: "support");

            migrationBuilder.DropTable(
                name: "ticket_links",
                schema: "support");

            migrationBuilder.DropTable(
                name: "ticket_sla_breach_events",
                schema: "support");

            migrationBuilder.DropTable(
                name: "ticket_messages",
                schema: "support");

            migrationBuilder.DropTable(
                name: "tickets",
                schema: "support");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BackendApi.Modules.Notifications.Persistence.Migrations;

/// <inheritdoc />
public partial class CreateNotificationsSchema : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "notifications");

        // ---------- templates ----------
        migrationBuilder.CreateTable(
            name: "templates",
            schema: "notifications",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                EventKind = table.Column<string>(type: "text", nullable: false),
                CurrentVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                State = table.Column<string>(type: "text", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_templates", x => x.Id);
            });

        // ---------- template_versions ----------
        migrationBuilder.CreateTable(
            name: "template_versions",
            schema: "notifications",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                VersionNo = table.Column<int>(type: "integer", nullable: false),
                State = table.Column<string>(type: "text", nullable: false),
                BodyAr = table.Column<string>(type: "text", nullable: false),
                BodyEn = table.Column<string>(type: "text", nullable: false),
                SubjectAr = table.Column<string>(type: "text", nullable: true),
                SubjectEn = table.Column<string>(type: "text", nullable: true),
                PlaceholdersJson = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                ArEditorialReviewed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                ReviewerId = table.Column<Guid>(type: "uuid", nullable: true),
                ReviewerComment = table.Column<string>(type: "text", nullable: true),
                SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_template_versions", x => x.Id);
                table.CheckConstraint("CK_template_versions_state",
                    "\"State\" IN ('draft','in_review','published','archived')");
                table.CheckConstraint("CK_template_versions_publish_gate",
                    "(\"State\" NOT IN ('published','archived')) OR (\"ArEditorialReviewed\" = true AND \"ReviewerId\" IS NOT NULL AND \"ReviewerId\" <> \"AuthorId\" AND \"PublishedAt\" IS NOT NULL)");
                table.CheckConstraint("CK_template_versions_locale_complete",
                    "(\"State\" = 'draft') OR (length(\"BodyAr\") > 0 AND length(\"BodyEn\") > 0)");
                table.ForeignKey(
                    name: "FK_template_versions_templates",
                    column: x => x.TemplateId,
                    principalSchema: "notifications",
                    principalTable: "templates",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        // FK on templates.CurrentVersionId → template_versions(Id). Added after both tables exist.
        migrationBuilder.AddForeignKey(
            name: "FK_templates_current_version",
            schema: "notifications",
            table: "templates",
            column: "CurrentVersionId",
            principalSchema: "notifications",
            principalTable: "template_versions",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        // ---------- notifications ----------
        migrationBuilder.CreateTable(
            name: "notifications",
            schema: "notifications",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                RecipientId = table.Column<Guid>(type: "uuid", nullable: true),
                RecipientKind = table.Column<string>(type: "text", nullable: false),
                Channel = table.Column<string>(type: "text", nullable: false),
                EventKind = table.Column<string>(type: "text", nullable: false),
                TemplateVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                MarketCode = table.Column<string>(type: "text", nullable: false),
                Locale = table.Column<string>(type: "text", nullable: false),
                State = table.Column<string>(type: "text", nullable: false),
                SkippedReason = table.Column<string>(type: "text", nullable: true),
                FailedReason = table.Column<string>(type: "text", nullable: true),
                ProviderId = table.Column<string>(type: "text", nullable: true),
                ProviderMessageId = table.Column<string>(type: "text", nullable: true),
                Attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                IdempotencyKey = table.Column<string>(type: "text", nullable: false),
                PayloadRedactedJson = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                CampaignId = table.Column<Guid>(type: "uuid", nullable: true),
                NotBefore = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                FailedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_notifications", x => x.Id);
                table.CheckConstraint("CK_notifications_market_code", "\"MarketCode\" IN ('sa','eg')");
                table.CheckConstraint("CK_notifications_locale", "\"Locale\" IN ('ar','en')");
                table.CheckConstraint("CK_notifications_channel", "\"Channel\" IN ('sms','email','push')");
                table.CheckConstraint("CK_notifications_recipient_kind", "\"RecipientKind\" IN ('customer','admin','anonymous')");
                table.CheckConstraint("CK_notifications_state",
                    "\"State\" IN ('pending','queued','sending','delivered','failed','retrying','dead_letter','skipped')");
                table.CheckConstraint("CK_notifications_idempotency_key_sha256",
                    "\"IdempotencyKey\" ~ '^[0-9a-fA-F]{64}$'");
                table.ForeignKey(
                    name: "FK_notifications_template_version",
                    column: x => x.TemplateVersionId,
                    principalSchema: "notifications",
                    principalTable: "template_versions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        // ---------- deliveries ----------
        migrationBuilder.CreateTable(
            name: "deliveries",
            schema: "notifications",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                AttemptNo = table.Column<int>(type: "integer", nullable: false),
                ProviderId = table.Column<string>(type: "text", nullable: false),
                ProviderMessageId = table.Column<string>(type: "text", nullable: true),
                Status = table.Column<string>(type: "text", nullable: false),
                ErrorCode = table.Column<string>(type: "text", nullable: true),
                ErrorMessageRedacted = table.Column<string>(type: "text", nullable: true),
                RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                RespondedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_deliveries", x => x.Id);
                table.CheckConstraint("CK_deliveries_status",
                    "\"Status\" IN ('accepted','delivered','bounced','failed','timeout','unregistered','soft_bounce')");
                table.CheckConstraint("CK_deliveries_attempt_positive", "\"AttemptNo\" > 0");
                table.ForeignKey(
                    name: "FK_deliveries_notification",
                    column: x => x.NotificationId,
                    principalSchema: "notifications",
                    principalTable: "notifications",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        // ---------- webhooks_received ----------
        migrationBuilder.CreateTable(
            name: "webhooks_received",
            schema: "notifications",
            columns: table => new
            {
                ProviderId = table.Column<string>(type: "text", nullable: false),
                ProviderMessageId = table.Column<string>(type: "text", nullable: false),
                EventKind = table.Column<string>(type: "text", nullable: false),
                ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                SignatureValidated = table.Column<bool>(type: "boolean", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_webhooks_received", x => new { x.ProviderId, x.ProviderMessageId, x.EventKind });
            });

        // ---------- campaigns ----------
        migrationBuilder.CreateTable(
            name: "campaigns",
            schema: "notifications",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                Name = table.Column<string>(type: "text", nullable: false),
                State = table.Column<string>(type: "text", nullable: false),
                TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                TemplateVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                Channel = table.Column<string>(type: "text", nullable: false),
                MarketCode = table.Column<string>(type: "text", nullable: false),
                TargetCriteriaJson = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                SendAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                RecipientCountSnapshot = table.Column<int>(type: "integer", nullable: true),
                StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                PausedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_campaigns", x => x.Id);
                table.CheckConstraint("CK_campaigns_state",
                    "\"State\" IN ('draft','scheduled','sending','paused','completed','cancelled')");
                table.CheckConstraint("CK_campaigns_channel_not_otp",
                    "\"Channel\" IN ('sms','email','push')");
                table.CheckConstraint("CK_campaigns_market_code", "\"MarketCode\" IN ('sa','eg')");
                table.ForeignKey(
                    name: "FK_campaigns_template",
                    column: x => x.TemplateId,
                    principalSchema: "notifications",
                    principalTable: "templates",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_campaigns_template_version",
                    column: x => x.TemplateVersionId,
                    principalSchema: "notifications",
                    principalTable: "template_versions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        // notifications.CampaignId FK after campaigns exists.
        migrationBuilder.AddForeignKey(
            name: "FK_notifications_campaign",
            schema: "notifications",
            table: "notifications",
            column: "CampaignId",
            principalSchema: "notifications",
            principalTable: "campaigns",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        // ---------- campaign_recipients ----------
        migrationBuilder.CreateTable(
            name: "campaign_recipients",
            schema: "notifications",
            columns: table => new
            {
                CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                RecipientId = table.Column<Guid>(type: "uuid", nullable: false),
                NotificationId = table.Column<Guid>(type: "uuid", nullable: true),
                SkippedReason = table.Column<string>(type: "text", nullable: true),
                MaterializedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_campaign_recipients", x => new { x.CampaignId, x.RecipientId });
                table.CheckConstraint("CK_campaign_recipients_skipped_reason",
                    "\"SkippedReason\" IS NULL OR \"SkippedReason\" IN ('channel_disabled_by_customer','rate_limited','recipient_deactivated','quiet_hours','opted_out')");
                table.ForeignKey(
                    name: "FK_campaign_recipients_campaign",
                    column: x => x.CampaignId,
                    principalSchema: "notifications",
                    principalTable: "campaigns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_campaign_recipients_notification",
                    column: x => x.NotificationId,
                    principalSchema: "notifications",
                    principalTable: "notifications",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        // ---------- preferences ----------
        migrationBuilder.CreateTable(
            name: "preferences",
            schema: "notifications",
            columns: table => new
            {
                CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                Channel = table.Column<string>(type: "text", nullable: false),
                Category = table.Column<string>(type: "text", nullable: false),
                Enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_preferences", x => new { x.CustomerId, x.Channel, x.Category });
                table.CheckConstraint("CK_preferences_channel", "\"Channel\" IN ('sms','email','push')");
                table.CheckConstraint("CK_preferences_category", "\"Category\" IN ('transactional','marketing')");
                table.CheckConstraint("CK_preferences_transactional_always_on",
                    "NOT (\"Category\" = 'transactional' AND \"Enabled\" = false)");
            });

        // ---------- unsubscribe_tokens ----------
        migrationBuilder.CreateTable(
            name: "unsubscribe_tokens",
            schema: "notifications",
            columns: table => new
            {
                TokenHash = table.Column<byte[]>(type: "bytea", nullable: false),
                CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                Channel = table.Column<string>(type: "text", nullable: false),
                Category = table.Column<string>(type: "text", nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_unsubscribe_tokens", x => x.TokenHash);
                table.CheckConstraint("CK_unsubscribe_tokens_channel", "\"Channel\" IN ('sms','email','push')");
                table.CheckConstraint("CK_unsubscribe_tokens_category_marketing", "\"Category\" = 'marketing'");
            });

        // ---------- provider_routing ----------
        migrationBuilder.CreateTable(
            name: "provider_routing",
            schema: "notifications",
            columns: table => new
            {
                MarketCode = table.Column<string>(type: "text", nullable: false),
                Channel = table.Column<string>(type: "text", nullable: false),
                PrimaryProviderId = table.Column<string>(type: "text", nullable: false),
                BackupProviderId = table.Column<string>(type: "text", nullable: true),
                AutoFailoverEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                FailoverThresholdPct = table.Column<int>(type: "integer", nullable: false, defaultValue: 50),
                FailoverWindowMinutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 5),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_provider_routing", x => new { x.MarketCode, x.Channel });
                table.CheckConstraint("CK_provider_routing_market_code", "\"MarketCode\" IN ('sa','eg')");
                table.CheckConstraint("CK_provider_routing_channel", "\"Channel\" IN ('sms','email','push')");
                table.CheckConstraint("CK_provider_routing_distinct_providers",
                    "\"BackupProviderId\" IS NULL OR \"PrimaryProviderId\" <> \"BackupProviderId\"");
                table.CheckConstraint("CK_provider_routing_threshold_range",
                    "\"FailoverThresholdPct\" BETWEEN 10 AND 90");
                table.CheckConstraint("CK_provider_routing_window_positive",
                    "\"FailoverWindowMinutes\" > 0");
            });

        // ---------- dead_letter_queue ----------
        migrationBuilder.CreateTable(
            name: "dead_letter_queue",
            schema: "notifications",
            columns: table => new
            {
                NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                LastErrorMessageRedacted = table.Column<string>(type: "text", nullable: true),
                EnteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                Resolution = table.Column<string>(type: "text", nullable: true),
                ResolvedBy = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_dead_letter_queue", x => x.NotificationId);
                table.CheckConstraint("CK_dead_letter_resolution",
                    "\"Resolution\" IS NULL OR \"Resolution\" IN ('retry','discard')");
                table.ForeignKey(
                    name: "FK_dead_letter_notification",
                    column: x => x.NotificationId,
                    principalSchema: "notifications",
                    principalTable: "notifications",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        // ---------- dead_letter_queue_archive ----------
        migrationBuilder.CreateTable(
            name: "dead_letter_queue_archive",
            schema: "notifications",
            columns: table => new
            {
                NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                LastErrorMessageRedacted = table.Column<string>(type: "text", nullable: true),
                EnteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                Resolution = table.Column<string>(type: "text", nullable: true),
                ResolvedBy = table.Column<Guid>(type: "uuid", nullable: true),
                ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_dead_letter_queue_archive", x => x.NotificationId);
            });

        // ---------- market_schemas ----------
        migrationBuilder.CreateTable(
            name: "market_schemas",
            schema: "notifications",
            columns: table => new
            {
                MarketCode = table.Column<string>(type: "text", nullable: false),
                QuietHoursMarketingLocalStart = table.Column<TimeOnly>(type: "time", nullable: false),
                QuietHoursMarketingLocalEnd = table.Column<TimeOnly>(type: "time", nullable: false),
                QuietHoursTimezone = table.Column<string>(type: "text", nullable: false),
                UnsubscribeFooterAr = table.Column<string>(type: "text", nullable: false),
                UnsubscribeFooterEn = table.Column<string>(type: "text", nullable: false),
                RateLimitMarketingPer24h = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                RateLimitTransactionalPer24h = table.Column<int>(type: "integer", nullable: false, defaultValue: 5),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_market_schemas", x => x.MarketCode);
                table.CheckConstraint("CK_market_schemas_market_code", "\"MarketCode\" IN ('sa','eg')");
                table.CheckConstraint("CK_market_schemas_rate_limits_positive",
                    "\"RateLimitMarketingPer24h\" >= 0 AND \"RateLimitTransactionalPer24h\" >= 0");
            });

        // ===== indexes =====
        migrationBuilder.CreateIndex(
            name: "IX_templates_event_kind_active",
            schema: "notifications", table: "templates", column: "EventKind",
            filter: "\"DeletedAt\" IS NULL");

        migrationBuilder.CreateIndex(
            name: "UX_template_versions_template_version",
            schema: "notifications", table: "template_versions",
            columns: new[] { "TemplateId", "VersionNo" }, unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_template_versions_template_state",
            schema: "notifications", table: "template_versions",
            columns: new[] { "TemplateId", "State" });

        migrationBuilder.CreateIndex(
            name: "IX_notifications_active_work",
            schema: "notifications", table: "notifications",
            columns: new[] { "State", "Channel" },
            filter: "\"State\" IN ('pending','queued','retrying')");

        migrationBuilder.CreateIndex(
            name: "UX_notifications_idempotency_key_active",
            schema: "notifications", table: "notifications", column: "IdempotencyKey",
            unique: true, filter: "\"DeletedAt\" IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_notifications_recipient_created_desc",
            schema: "notifications", table: "notifications",
            columns: new[] { "RecipientId", "CreatedAt" })
            .Annotation("Npgsql:IndexSortOrder", new[] { SortOrder.Ascending, SortOrder.Descending });

        migrationBuilder.CreateIndex(
            name: "IX_notifications_campaign_state",
            schema: "notifications", table: "notifications",
            columns: new[] { "CampaignId", "State" });

        migrationBuilder.CreateIndex(
            name: "IX_notifications_provider_message",
            schema: "notifications", table: "notifications",
            columns: new[] { "ProviderId", "ProviderMessageId" });

        migrationBuilder.CreateIndex(
            name: "UX_deliveries_notification_attempt",
            schema: "notifications", table: "deliveries",
            columns: new[] { "NotificationId", "AttemptNo" }, unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_deliveries_created_at",
            schema: "notifications", table: "deliveries", column: "CreatedAt");

        migrationBuilder.CreateIndex(
            name: "IX_webhooks_received_at",
            schema: "notifications", table: "webhooks_received", column: "ReceivedAt");

        migrationBuilder.CreateIndex(
            name: "IX_campaigns_state_send_at",
            schema: "notifications", table: "campaigns",
            columns: new[] { "State", "SendAt" });

        migrationBuilder.CreateIndex(
            name: "IX_campaigns_created_by",
            schema: "notifications", table: "campaigns", column: "CreatedBy");

        migrationBuilder.CreateIndex(
            name: "IX_campaign_recipients_notification",
            schema: "notifications", table: "campaign_recipients", column: "NotificationId");

        migrationBuilder.CreateIndex(
            name: "IX_unsubscribe_tokens_customer",
            schema: "notifications", table: "unsubscribe_tokens", column: "CustomerId");

        migrationBuilder.CreateIndex(
            name: "IX_unsubscribe_tokens_expires",
            schema: "notifications", table: "unsubscribe_tokens", column: "ExpiresAt");

        migrationBuilder.CreateIndex(
            name: "IX_dead_letter_entered_at",
            schema: "notifications", table: "dead_letter_queue", column: "EnteredAt");

        migrationBuilder.CreateIndex(
            name: "IX_dead_letter_unresolved",
            schema: "notifications", table: "dead_letter_queue", column: "ResolvedAt",
            filter: "\"ResolvedAt\" IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_dead_letter_archive_archived_at",
            schema: "notifications", table: "dead_letter_queue_archive", column: "ArchivedAt");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_notifications_campaign",
            schema: "notifications", table: "notifications");
        migrationBuilder.DropForeignKey(
            name: "FK_templates_current_version",
            schema: "notifications", table: "templates");

        migrationBuilder.DropTable(name: "market_schemas", schema: "notifications");
        migrationBuilder.DropTable(name: "dead_letter_queue_archive", schema: "notifications");
        migrationBuilder.DropTable(name: "dead_letter_queue", schema: "notifications");
        migrationBuilder.DropTable(name: "provider_routing", schema: "notifications");
        migrationBuilder.DropTable(name: "unsubscribe_tokens", schema: "notifications");
        migrationBuilder.DropTable(name: "preferences", schema: "notifications");
        migrationBuilder.DropTable(name: "campaign_recipients", schema: "notifications");
        migrationBuilder.DropTable(name: "webhooks_received", schema: "notifications");
        migrationBuilder.DropTable(name: "deliveries", schema: "notifications");
        migrationBuilder.DropTable(name: "notifications", schema: "notifications");
        migrationBuilder.DropTable(name: "campaigns", schema: "notifications");
        migrationBuilder.DropTable(name: "template_versions", schema: "notifications");
        migrationBuilder.DropTable(name: "templates", schema: "notifications");

        migrationBuilder.Sql("DROP SCHEMA IF EXISTS notifications CASCADE;");
    }
}

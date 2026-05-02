using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Modules.Cms.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateCmsSchemaAndTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "cms");

            migrationBuilder.CreateTable(
                name: "assets",
                schema: "cms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    StorageObjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Mime = table.Column<string>(type: "text", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    IntendedLocale = table.Column<string>(type: "text", nullable: true),
                    OriginalFilename = table.Column<string>(type: "text", nullable: false),
                    StorageObjectState = table.Column<string>(type: "text", nullable: false, defaultValue: "active"),
                    DereferencedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SweptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UploadedByActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assets", x => x.Id);
                    table.CheckConstraint("CK_cms_asset_intended_locale", "\"IntendedLocale\" IS NULL OR \"IntendedLocale\" IN ('ar','en','*')");
                    table.CheckConstraint("CK_cms_asset_size_positive", "\"SizeBytes\" >= 0");
                    table.CheckConstraint("CK_cms_asset_storage_state", "\"StorageObjectState\" IN ('active','swept')");
                });

            migrationBuilder.CreateTable(
                name: "banner_campaign_bindings",
                schema: "cms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    BannerId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    BoundAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    ReleasedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    BindingState = table.Column<string>(type: "text", nullable: false, defaultValue: "active"),
                    ReleaseActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReleaseReasonNote = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_banner_campaign_bindings", x => x.Id);
                    table.CheckConstraint("CK_cms_banner_binding_release_consistency", "(\"BindingState\" = 'active' AND \"ReleasedAtUtc\" IS NULL) OR (\"BindingState\" <> 'active' AND \"ReleasedAtUtc\" IS NOT NULL)");
                    table.CheckConstraint("CK_cms_banner_binding_state", "\"BindingState\" IN ('active','released_due_to_campaign_deactivation','released_by_editor')");
                });

            migrationBuilder.CreateTable(
                name: "banner_slots",
                schema: "cms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    SlotKind = table.Column<string>(type: "text", nullable: false),
                    HeadlineAr = table.Column<string>(type: "text", nullable: true),
                    HeadlineEn = table.Column<string>(type: "text", nullable: true),
                    SubheadAr = table.Column<string>(type: "text", nullable: true),
                    SubheadEn = table.Column<string>(type: "text", nullable: true),
                    AssetIdAr = table.Column<Guid>(type: "uuid", nullable: true),
                    AssetIdEn = table.Column<Guid>(type: "uuid", nullable: true),
                    CtaKind = table.Column<string>(type: "text", nullable: false),
                    CtaTarget = table.Column<string>(type: "text", nullable: true),
                    CtaHealth = table.Column<string>(type: "text", nullable: false, defaultValue: "not_applicable"),
                    ScheduledStartUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ScheduledEndUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MarketCode = table.Column<string>(type: "text", nullable: false),
                    PriorityWithinSlot = table.Column<int>(type: "integer", nullable: false, defaultValue: 100),
                    State = table.Column<string>(type: "text", nullable: false, defaultValue: "draft"),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnershipOrphaned = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    LastStaleAlertAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastStaleAlertDismissedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    EditorSaveAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchiveReasonNote = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_banner_slots", x => x.Id);
                    table.CheckConstraint("CK_cms_banners_cta_health", "\"CtaHealth\" IN ('verified','broken','transient_unverified','not_applicable')");
                    table.CheckConstraint("CK_cms_banners_cta_kind", "\"CtaKind\" IN ('link','category','product','bundle','external_url','none')");
                    table.CheckConstraint("CK_cms_banners_headline_ar_len", "\"HeadlineAr\" IS NULL OR char_length(\"HeadlineAr\") <= 120");
                    table.CheckConstraint("CK_cms_banners_headline_en_len", "\"HeadlineEn\" IS NULL OR char_length(\"HeadlineEn\") <= 120");
                    table.CheckConstraint("CK_cms_banners_market_code", "\"MarketCode\" IN ('EG','KSA','*')");
                    table.CheckConstraint("CK_cms_banners_schedule_window", "\"ScheduledStartUtc\" IS NULL OR \"ScheduledEndUtc\" IS NULL OR \"ScheduledEndUtc\" > \"ScheduledStartUtc\"");
                    table.CheckConstraint("CK_cms_banners_slot_kind", "\"SlotKind\" IN ('hero_top','category_strip','footer_strip','home_secondary')");
                    table.CheckConstraint("CK_cms_banners_state", "\"State\" IN ('draft','scheduled','live','archived')");
                    table.CheckConstraint("CK_cms_banners_subhead_ar_len", "\"SubheadAr\" IS NULL OR char_length(\"SubheadAr\") <= 240");
                    table.CheckConstraint("CK_cms_banners_subhead_en_len", "\"SubheadEn\" IS NULL OR char_length(\"SubheadEn\") <= 240");
                });

            migrationBuilder.CreateTable(
                name: "blog_articles",
                schema: "cms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Category = table.Column<string>(type: "text", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: false),
                    AuthoredLocale = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    Body = table.Column<string>(type: "text", nullable: true),
                    CoverAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    SeoMetaTitle = table.Column<string>(type: "text", nullable: true),
                    SeoMetaDescription = table.Column<string>(type: "text", nullable: true),
                    SeoOgImageId = table.Column<Guid>(type: "uuid", nullable: true),
                    SeoSchemaOrgKind = table.Column<string>(type: "text", nullable: false, defaultValue: "BlogPosting"),
                    ScheduledPublishAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MarketCode = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false, defaultValue: "draft"),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnershipOrphaned = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    LastStaleAlertAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastStaleAlertDismissedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    EditorSaveAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchiveReasonNote = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_blog_articles", x => x.Id);
                    table.CheckConstraint("CK_cms_blog_authored_locale", "\"AuthoredLocale\" IN ('ar','en')");
                    table.CheckConstraint("CK_cms_blog_body_len", "\"Body\" IS NULL OR char_length(\"Body\") <= 60000");
                    table.CheckConstraint("CK_cms_blog_category", "\"Category\" IN ('tips','news','guides','case_studies','clinical','other')");
                    table.CheckConstraint("CK_cms_blog_market_code", "\"MarketCode\" IN ('EG','KSA','*')");
                    table.CheckConstraint("CK_cms_blog_seo_kind", "\"SeoSchemaOrgKind\" IN ('Article','BlogPosting','NewsArticle','FAQPage')");
                    table.CheckConstraint("CK_cms_blog_seo_meta_description_len", "\"SeoMetaDescription\" IS NULL OR char_length(\"SeoMetaDescription\") <= 160");
                    table.CheckConstraint("CK_cms_blog_seo_meta_title_len", "\"SeoMetaTitle\" IS NULL OR char_length(\"SeoMetaTitle\") <= 70");
                    table.CheckConstraint("CK_cms_blog_slug_pattern", "\"Slug\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
                    table.CheckConstraint("CK_cms_blog_state", "\"State\" IN ('draft','scheduled','live','archived')");
                });

            migrationBuilder.CreateTable(
                name: "faq_entries",
                schema: "cms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Category = table.Column<string>(type: "text", nullable: false),
                    QuestionAr = table.Column<string>(type: "text", nullable: true),
                    QuestionEn = table.Column<string>(type: "text", nullable: true),
                    AnswerAr = table.Column<string>(type: "text", nullable: true),
                    AnswerEn = table.Column<string>(type: "text", nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 100),
                    MarketCode = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false, defaultValue: "draft"),
                    ScheduledPublishAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnershipOrphaned = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    LastStaleAlertAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastStaleAlertDismissedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    EditorSaveAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchiveReasonNote = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_faq_entries", x => x.Id);
                    table.CheckConstraint("CK_cms_faq_answer_ar_len", "\"AnswerAr\" IS NULL OR char_length(\"AnswerAr\") <= 4000");
                    table.CheckConstraint("CK_cms_faq_answer_en_len", "\"AnswerEn\" IS NULL OR char_length(\"AnswerEn\") <= 4000");
                    table.CheckConstraint("CK_cms_faq_category", "\"Category\" IN ('ordering','payment','shipping','returns','account','verification','b2b','other')");
                    table.CheckConstraint("CK_cms_faq_market_code", "\"MarketCode\" IN ('EG','KSA','*')");
                    table.CheckConstraint("CK_cms_faq_question_ar_len", "\"QuestionAr\" IS NULL OR char_length(\"QuestionAr\") <= 250");
                    table.CheckConstraint("CK_cms_faq_question_en_len", "\"QuestionEn\" IS NULL OR char_length(\"QuestionEn\") <= 250");
                    table.CheckConstraint("CK_cms_faq_state", "\"State\" IN ('draft','scheduled','live','archived')");
                });

            migrationBuilder.CreateTable(
                name: "featured_sections",
                schema: "cms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    SectionKind = table.Column<string>(type: "text", nullable: false),
                    TitleAr = table.Column<string>(type: "text", nullable: true),
                    TitleEn = table.Column<string>(type: "text", nullable: true),
                    SubtitleAr = table.Column<string>(type: "text", nullable: true),
                    SubtitleEn = table.Column<string>(type: "text", nullable: true),
                    References = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    DisplayPriority = table.Column<int>(type: "integer", nullable: false, defaultValue: 100),
                    MarketCode = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false, defaultValue: "draft"),
                    ScheduledPublishAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnershipOrphaned = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    LastStaleAlertAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastStaleAlertDismissedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastPartialBrokenAlertAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    EditorSaveAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchiveReasonNote = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_featured_sections", x => x.Id);
                    table.CheckConstraint("CK_cms_featured_market_code", "\"MarketCode\" IN ('EG','KSA','*')");
                    table.CheckConstraint("CK_cms_featured_refs_size", "jsonb_array_length(\"References\") BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_cms_featured_section_kind", "\"SectionKind\" IN ('home_top','home_mid','category_landing','b2b_landing')");
                    table.CheckConstraint("CK_cms_featured_state", "\"State\" IN ('draft','scheduled','live','archived')");
                });

            migrationBuilder.CreateTable(
                name: "legal_page_versions",
                schema: "cms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    LegalPageKind = table.Column<string>(type: "text", nullable: false),
                    VersionLabel = table.Column<string>(type: "text", nullable: false),
                    BodyAr = table.Column<string>(type: "text", nullable: true),
                    BodyEn = table.Column<string>(type: "text", nullable: true),
                    EffectiveAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MarketCode = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false, defaultValue: "draft"),
                    SupersededAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SupersededByVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnershipOrphaned = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    LastStaleAlertAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastStaleAlertDismissedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    EditorSaveAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_legal_page_versions", x => x.Id);
                    table.CheckConstraint("CK_cms_legal_kind", "\"LegalPageKind\" IN ('terms','privacy','returns','cookies')");
                    table.CheckConstraint("CK_cms_legal_market_code", "\"MarketCode\" IN ('EG','KSA','*')");
                    table.CheckConstraint("CK_cms_legal_state", "\"State\" IN ('draft','scheduled','live','superseded')");
                    table.CheckConstraint("CK_cms_legal_supersede_consistency", "(\"State\" = 'superseded' AND \"SupersededAtUtc\" IS NOT NULL AND \"SupersededByVersionId\" IS NOT NULL) OR (\"State\" <> 'superseded' AND \"SupersededAtUtc\" IS NULL AND \"SupersededByVersionId\" IS NULL)");
                });

            migrationBuilder.CreateTable(
                name: "market_schemas",
                schema: "cms",
                columns: table => new
                {
                    MarketCode = table.Column<string>(type: "text", nullable: false),
                    BannerMaxLivePerSlot = table.Column<int>(type: "integer", nullable: false, defaultValue: 5),
                    FeaturedSectionMaxReferences = table.Column<int>(type: "integer", nullable: false, defaultValue: 24),
                    PreviewTokenDefaultTtlHours = table.Column<int>(type: "integer", nullable: false, defaultValue: 24),
                    DraftStalenessAlertDays = table.Column<int>(type: "integer", nullable: false, defaultValue: 30),
                    AssetGracePeriodDays = table.Column<int>(type: "integer", nullable: false, defaultValue: 7),
                    LastEditedByActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastEditedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_market_schemas", x => x.MarketCode);
                    table.CheckConstraint("CK_cms_asset_grace", "\"AssetGracePeriodDays\" BETWEEN 0 AND 30");
                    table.CheckConstraint("CK_cms_banner_max_live", "\"BannerMaxLivePerSlot\" BETWEEN 1 AND 10");
                    table.CheckConstraint("CK_cms_featured_max_refs", "\"FeaturedSectionMaxReferences\" BETWEEN 1 AND 100");
                    table.CheckConstraint("CK_cms_market_code", "\"MarketCode\" IN ('EG','KSA','*')");
                    table.CheckConstraint("CK_cms_preview_ttl", "\"PreviewTokenDefaultTtlHours\" BETWEEN 1 AND 168");
                    table.CheckConstraint("CK_cms_stale_alert", "\"DraftStalenessAlertDays\" BETWEEN 7 AND 365");
                });

            migrationBuilder.CreateTable(
                name: "preview_tokens",
                schema: "cms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TokenHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    EntityKind = table.Column<string>(type: "text", nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorRoleAtMint = table.Column<string>(type: "text", nullable: false),
                    MintedByActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    MintedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedByActorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_preview_tokens", x => x.Id);
                    table.CheckConstraint("CK_cms_preview_entity_kind", "\"EntityKind\" IN ('banner_slot','featured_section','faq_entry','blog_article','legal_page_version')");
                    table.CheckConstraint("CK_cms_preview_expires_after_mint", "\"ExpiresAtUtc\" > \"MintedAtUtc\"");
                });

            migrationBuilder.CreateIndex(
                name: "IX_cms_assets_gc_scan",
                schema: "cms",
                table: "assets",
                columns: new[] { "StorageObjectState", "DereferencedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_cms_banner_binding_banner",
                schema: "cms",
                table: "banner_campaign_bindings",
                columns: new[] { "BannerId", "BindingState" });

            migrationBuilder.CreateIndex(
                name: "IX_cms_banner_binding_campaign",
                schema: "cms",
                table: "banner_campaign_bindings",
                columns: new[] { "CampaignId", "BindingState" });

            migrationBuilder.CreateIndex(
                name: "IX_cms_banners_owner_state",
                schema: "cms",
                table: "banner_slots",
                columns: new[] { "OwnerActorId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_cms_banners_storefront_read",
                schema: "cms",
                table: "banner_slots",
                columns: new[] { "State", "MarketCode", "SlotKind", "PriorityWithinSlot", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_cms_banners_vendor",
                schema: "cms",
                table: "banner_slots",
                column: "VendorId",
                filter: "\"VendorId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_cms_banners_worker_end_scan",
                schema: "cms",
                table: "banner_slots",
                columns: new[] { "State", "ScheduledEndUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_cms_banners_worker_start_scan",
                schema: "cms",
                table: "banner_slots",
                columns: new[] { "State", "ScheduledStartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_cms_blog_storefront_read",
                schema: "cms",
                table: "blog_articles",
                columns: new[] { "State", "MarketCode", "Category", "PublishedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_cms_blog_worker_scan",
                schema: "cms",
                table: "blog_articles",
                columns: new[] { "State", "ScheduledPublishAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_cms_blog_slug_market_locale",
                schema: "cms",
                table: "blog_articles",
                columns: new[] { "MarketCode", "AuthoredLocale", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cms_faq_admin_grouping",
                schema: "cms",
                table: "faq_entries",
                columns: new[] { "Category", "MarketCode" });

            migrationBuilder.CreateIndex(
                name: "IX_cms_faq_storefront_read",
                schema: "cms",
                table: "faq_entries",
                columns: new[] { "State", "MarketCode", "Category", "DisplayOrder", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_cms_featured_storefront_read",
                schema: "cms",
                table: "featured_sections",
                columns: new[] { "State", "MarketCode", "SectionKind", "DisplayPriority", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_cms_featured_worker_scan",
                schema: "cms",
                table: "featured_sections",
                columns: new[] { "State", "ScheduledPublishAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_cms_legal_history",
                schema: "cms",
                table: "legal_page_versions",
                columns: new[] { "LegalPageKind", "MarketCode", "State", "EffectiveAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_cms_legal_worker_scan",
                schema: "cms",
                table: "legal_page_versions",
                columns: new[] { "State", "EffectiveAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_cms_preview_cleanup_scan",
                schema: "cms",
                table: "preview_tokens",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "UX_cms_preview_token_hash",
                schema: "cms",
                table: "preview_tokens",
                column: "TokenHash",
                unique: true);

            // §2.5 — exactly one live legal-page version per (kind, market).
            // Partial unique index, expressed via raw SQL because EF cannot
            // model a unique index whose filter references the text-typed
            // State discriminator portably.
            migrationBuilder.Sql(@"
CREATE UNIQUE INDEX ""UX_cms_legal_one_live_per_kind_market""
    ON cms.legal_page_versions (""LegalPageKind"", ""MarketCode"")
    WHERE ""State"" = 'live';");

            // GIN index over the polymorphic featured-section references jsonb,
            // for "which sections reference product X" admin lookups (data-model §2.2).
            migrationBuilder.Sql(@"
CREATE INDEX ""IX_cms_featured_refs_gin""
    ON cms.featured_sections USING GIN (""References"");");

            // Generic raise-violation function used by trigger guards below.
            // Trigger functions cannot have declared arguments; the reason
            // text is passed at trigger creation and read via TG_ARGV[0].
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION cms.raise_immutable_violation()
    RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION '%', TG_ARGV[0] USING ERRCODE = '23000';
    RETURN NULL;
END;
$$;");

            // §2.5 triggers — legal-page versions: hard-delete forbidden; UPDATEs
            // allowed only for the supersede transition (live → superseded) and the
            // publish transitions on draft rows (draft → scheduled, draft → live).
            // Other body updates on a non-draft row are rejected by the API layer
            // (PATCH on non-draft) and the trigger.
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION cms.legal_page_versions_guard()
    RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
    IF (TG_OP = 'DELETE') THEN
        RAISE EXCEPTION 'cms.legal_page.version.delete_forbidden' USING ERRCODE = '23000';
    ELSIF (TG_OP = 'UPDATE') THEN
        IF OLD.""State"" = 'draft' AND NEW.""State"" IN ('draft','scheduled','live') THEN
            RETURN NEW;
        ELSIF OLD.""State"" = 'scheduled' AND NEW.""State"" IN ('scheduled','live') THEN
            RETURN NEW;
        ELSIF OLD.""State"" = 'live' AND NEW.""State"" IN ('live','superseded') THEN
            RETURN NEW;
        ELSIF OLD.""State"" = 'superseded' AND NEW.""State"" = 'superseded' THEN
            -- forbid mutation of a superseded row
            RAISE EXCEPTION 'cms.legal_page.version.superseded_immutable' USING ERRCODE = '23000';
        ELSE
            RAISE EXCEPTION 'cms.legal_page.illegal_transition' USING ERRCODE = '23000';
        END IF;
    END IF;
    RETURN NEW;
END;
$$;
CREATE TRIGGER cms_legal_page_versions_guard_trg
    BEFORE UPDATE OR DELETE ON cms.legal_page_versions
    FOR EACH ROW EXECUTE FUNCTION cms.legal_page_versions_guard();");

            // §2.6 trigger — assets: hard-delete forbidden; the GC worker uses
            // an EF Update flipping StorageObjectState to 'swept' (audited).
            migrationBuilder.Sql(@"
CREATE TRIGGER cms_assets_no_hard_delete_trg
    BEFORE DELETE ON cms.assets
    FOR EACH ROW EXECUTE PROCEDURE cms.raise_immutable_violation('cms.asset.hard_delete_forbidden');");

            // §2.7 trigger — preview tokens: deletes only allowed once a row
            // is ≥ 30 days past expiry (cleanup-worker path).
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION cms.preview_tokens_delete_guard()
    RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
    IF OLD.""ExpiresAtUtc"" + interval '30 days' < now() THEN
        RETURN OLD;
    END IF;
    RAISE EXCEPTION 'cms.preview_token.delete_forbidden' USING ERRCODE = '23000';
END;
$$;
CREATE TRIGGER cms_preview_tokens_delete_guard_trg
    BEFORE DELETE ON cms.preview_tokens
    FOR EACH ROW EXECUTE FUNCTION cms.preview_tokens_delete_guard();");

            // §2.8 trigger — banner_campaign_bindings: hard-delete forbidden;
            // release is a state flip recorded by the editor / subscriber.
            migrationBuilder.Sql(@"
CREATE TRIGGER cms_banner_campaign_bindings_no_hard_delete_trg
    BEFORE DELETE ON cms.banner_campaign_bindings
    FOR EACH ROW EXECUTE PROCEDURE cms.raise_immutable_violation('cms.banner_campaign_binding.delete_forbidden');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS cms_banner_campaign_bindings_no_hard_delete_trg ON cms.banner_campaign_bindings;");
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS cms_preview_tokens_delete_guard_trg ON cms.preview_tokens;");
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS cms_assets_no_hard_delete_trg ON cms.assets;");
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS cms_legal_page_versions_guard_trg ON cms.legal_page_versions;");
            migrationBuilder.Sql(@"DROP FUNCTION IF EXISTS cms.preview_tokens_delete_guard();");
            migrationBuilder.Sql(@"DROP FUNCTION IF EXISTS cms.legal_page_versions_guard();");
            migrationBuilder.Sql(@"DROP FUNCTION IF EXISTS cms.raise_immutable_violation();");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS cms.""IX_cms_featured_refs_gin"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS cms.""UX_cms_legal_one_live_per_kind_market"";");

            migrationBuilder.DropTable(
                name: "assets",
                schema: "cms");

            migrationBuilder.DropTable(
                name: "banner_campaign_bindings",
                schema: "cms");

            migrationBuilder.DropTable(
                name: "banner_slots",
                schema: "cms");

            migrationBuilder.DropTable(
                name: "blog_articles",
                schema: "cms");

            migrationBuilder.DropTable(
                name: "faq_entries",
                schema: "cms");

            migrationBuilder.DropTable(
                name: "featured_sections",
                schema: "cms");

            migrationBuilder.DropTable(
                name: "legal_page_versions",
                schema: "cms");

            migrationBuilder.DropTable(
                name: "market_schemas",
                schema: "cms");

            migrationBuilder.DropTable(
                name: "preview_tokens",
                schema: "cms");
        }
    }
}

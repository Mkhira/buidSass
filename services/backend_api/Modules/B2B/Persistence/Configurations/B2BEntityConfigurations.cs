using BackendApi.Modules.B2B.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.B2B.Persistence.Configurations;

public sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.ToTable("companies", "b2b");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.NameJson).HasColumnName("name").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.TaxId).HasColumnName("tax_id").HasColumnType("text").IsRequired().HasMaxLength(64);
        builder.Property(x => x.MarketCode).HasColumnName("market_code").HasColumnType("citext").IsRequired();
        builder.Property(x => x.PrimaryAddressJson).HasColumnName("primary_address").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.BillingAddressJson).HasColumnName("billing_address").HasColumnType("jsonb");
        builder.Property(x => x.ApproverRequired).HasColumnName("approver_required").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.PoRequired).HasColumnName("po_required").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.UniquePoRequired).HasColumnName("unique_po_required").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.InvoiceBillingEligible).HasColumnName("invoice_billing_eligible").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.State).HasColumnName("state").HasColumnType("citext").HasDefaultValue("active").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.XminRowVersion)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsRowVersion();

        builder.HasIndex(x => new { x.MarketCode, x.TaxId })
            .IsUnique()
            .HasDatabaseName("UX_companies_market_tax_id");
        builder.HasIndex(x => x.State)
            .HasDatabaseName("IX_companies_state")
            .HasFilter("state::text <> 'closed'");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("chk_companies_market", "market_code::text IN ('eg','ksa')");
            t.HasCheckConstraint("chk_companies_state",
                "state::text IN ('active','pending-verification','suspended','closed')");
        });
    }
}

public sealed class CompanyMembershipConfiguration : IEntityTypeConfiguration<CompanyMembership>
{
    public void Configure(EntityTypeBuilder<CompanyMembership> builder)
    {
        builder.ToTable("company_memberships", "b2b");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.MarketCode).HasColumnName("market_code").HasColumnType("citext").IsRequired().HasMaxLength(8);
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.Role).HasColumnName("role").HasColumnType("citext").IsRequired();
        builder.Property(x => x.JoinedAt).HasColumnName("joined_at").IsRequired();

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.CompanyId, x.UserId, x.Role })
            .IsUnique()
            .HasDatabaseName("UX_company_memberships_company_user_role");
        builder.HasIndex(x => x.UserId).HasDatabaseName("IX_company_memberships_user");
        builder.HasIndex(x => new { x.CompanyId, x.Role }).HasDatabaseName("IX_company_memberships_company_role");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("chk_company_memberships_role",
                "role::text IN ('companies.admin','buyer','approver')");
            t.HasCheckConstraint("chk_company_memberships_market",
                "market_code::text IN ('eg','ksa')");
        });
    }
}

public sealed class CompanyBranchConfiguration : IEntityTypeConfiguration<CompanyBranch>
{
    public void Configure(EntityTypeBuilder<CompanyBranch> builder)
    {
        builder.ToTable("company_branches", "b2b");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.MarketCode).HasColumnName("market_code").HasColumnType("citext").IsRequired().HasMaxLength(8);
        builder.Property(x => x.NameJson).HasColumnName("name").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.AddressJson).HasColumnName("address").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ContactPhone).HasColumnName("contact_phone").HasColumnType("text").HasMaxLength(64);

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.CompanyId).HasDatabaseName("IX_company_branches_company");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("chk_company_branches_market",
                "market_code::text IN ('eg','ksa')");
        });
    }
}

public sealed class CompanyInvitationConfiguration : IEntityTypeConfiguration<CompanyInvitation>
{
    public void Configure(EntityTypeBuilder<CompanyInvitation> builder)
    {
        builder.ToTable("company_invitations", "b2b");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.MarketCode).HasColumnName("market_code").HasColumnType("citext").IsRequired().HasMaxLength(8);
        builder.Property(x => x.InvitedBy).HasColumnName("invited_by").IsRequired();
        builder.Property(x => x.InvitedEmail).HasColumnName("invited_email").HasColumnType("citext").IsRequired().HasMaxLength(320);
        builder.Property(x => x.TargetRole).HasColumnName("target_role").HasColumnType("citext").IsRequired();
        // Plaintext is never persisted — only the hex-encoded HMAC-SHA256 digest. Length
        // is fixed at 64 hex chars but we leave headroom for a future digest swap.
        builder.Property(x => x.TokenHash).HasColumnName("token_hash").HasColumnType("text").IsRequired().HasMaxLength(128);
        builder.Property(x => x.State).HasColumnName("state").HasColumnType("citext").HasDefaultValue("pending").IsRequired();
        builder.Property(x => x.SentAt).HasColumnName("sent_at").IsRequired();
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();

        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.TokenHash)
            .IsUnique()
            .HasDatabaseName("UX_company_invitations_token_hash");
        builder.HasIndex(x => new { x.State, x.ExpiresAt })
            .HasDatabaseName("IX_company_invitations_state_expires")
            .HasFilter("state::text = 'pending'");
        builder.HasIndex(x => new { x.CompanyId, x.InvitedEmail, x.TargetRole })
            .IsUnique()
            .HasFilter("state::text = 'pending'")
            .HasDatabaseName("UX_company_invitations_open_per_company_email_role");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("chk_company_invitations_role",
                "target_role::text IN ('companies.admin','buyer','approver')");
            t.HasCheckConstraint("chk_company_invitations_state",
                "state::text IN ('pending','accepted','declined','expired')");
            t.HasCheckConstraint("chk_company_invitations_market",
                "market_code::text IN ('eg','ksa')");
        });
    }
}

public sealed class QuoteConfiguration : IEntityTypeConfiguration<Quote>
{
    public void Configure(EntityTypeBuilder<Quote> builder)
    {
        builder.ToTable("quotes", "b2b");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(x => x.CompanyId).HasColumnName("company_id");
        builder.Property(x => x.BranchId).HasColumnName("branch_id");
        builder.Property(x => x.MarketCode).HasColumnName("market_code").HasColumnType("citext").IsRequired();
        builder.Property(x => x.State).HasColumnName("state").HasColumnType("citext").HasDefaultValue("requested").IsRequired();
        builder.Property(x => x.RequestedAt).HasColumnName("requested_at").IsRequired();
        builder.Property(x => x.CurrentVersionId).HasColumnName("current_version_id");
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        builder.Property(x => x.DecidedAt).HasColumnName("decided_at");
        builder.Property(x => x.DecidedBy).HasColumnName("decided_by");
        builder.Property(x => x.TerminalAt).HasColumnName("terminal_at");
        builder.Property(x => x.TerminalReason).HasColumnName("terminal_reason").HasColumnType("citext");
        builder.Property(x => x.PoNumber).HasColumnName("po_number").HasColumnType("text").HasMaxLength(128);
        builder.Property(x => x.InvoiceBilling).HasColumnName("invoice_billing").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.CustomerSuppliedMessageJson).HasColumnName("customer_supplied_message").HasColumnType("jsonb");
        builder.Property(x => x.InternalNote).HasColumnName("internal_note").HasColumnType("text");
        builder.Property(x => x.ApproverRejectionNote).HasColumnName("approver_rejection_note").HasColumnType("text");
        builder.Property(x => x.OriginatingCartSnapshotJson).HasColumnName("originating_cart_snapshot").HasColumnType("jsonb");
        builder.Property(x => x.OriginatingProductId).HasColumnName("originating_product_id");
        builder.Property(x => x.RestrictionPolicySnapshotJson).HasColumnName("restriction_policy_snapshot").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb").IsRequired();
        builder.Property(x => x.SchemaVersion).HasColumnName("schema_version").IsRequired();
        builder.Property(x => x.DraftBodyJson).HasColumnName("draft_body").HasColumnType("jsonb");
        builder.Property(x => x.XminRowVersion)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsRowVersion();

        // Navigation FKs (CodeRabbit Round 1) — explicit referential integrity for the
        // logical FKs already named in the schema. CurrentVersionId points to a row that
        // is also a child of this quote; Restrict prevents deleting the head version
        // out from under quotes that still point to it.
        builder.HasOne(q => q.CurrentVersion)
            .WithMany()
            .HasForeignKey(q => q.CurrentVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(q => q.Company)
            .WithMany()
            .HasForeignKey(q => q.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(q => q.Branch)
            .WithMany()
            .HasForeignKey(q => q.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.CustomerId, x.State })
            .HasDatabaseName("IX_quotes_customer_state");
        builder.HasIndex(x => new { x.CompanyId, x.State, x.MarketCode })
            .HasDatabaseName("IX_quotes_company_state_market")
            .HasFilter("company_id IS NOT NULL");
        builder.HasIndex(x => new { x.State, x.MarketCode, x.RequestedAt })
            .HasDatabaseName("IX_quotes_state_market_requested")
            .HasFilter("state::text IN ('requested','drafted','revised','pending-approver')");
        builder.HasIndex(x => x.ExpiresAt)
            .HasDatabaseName("IX_quotes_expires_at")
            .HasFilter("state::text IN ('revised','pending-approver')");
        // FR-019 deterministic guard for unique_po_required companies.
        // The handler validates the flag; this partial unique index catches concurrent races.
        builder.HasIndex(x => new { x.CompanyId, x.PoNumber })
            .IsUnique()
            .HasFilter("company_id IS NOT NULL AND po_number IS NOT NULL")
            .HasDatabaseName("UX_quotes_company_po");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("chk_quotes_market", "market_code::text IN ('eg','ksa')");
            t.HasCheckConstraint("chk_quotes_state",
                "state::text IN ('requested','drafted','revised','pending-approver','accepted','rejected','expired','withdrawn')");
            t.HasCheckConstraint("chk_quotes_branch_requires_company",
                "branch_id IS NULL OR company_id IS NOT NULL");
        });
    }
}

public sealed class QuoteVersionConfiguration : IEntityTypeConfiguration<QuoteVersion>
{
    public void Configure(EntityTypeBuilder<QuoteVersion> builder)
    {
        builder.ToTable("quote_versions", "b2b");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.QuoteId).HasColumnName("quote_id").IsRequired();
        builder.Property(x => x.MarketCode).HasColumnName("market_code").HasColumnType("citext").IsRequired().HasMaxLength(8);
        builder.Property(x => x.VersionNumber).HasColumnName("version_number").IsRequired();
        builder.Property(x => x.AuthoredBy).HasColumnName("authored_by").IsRequired();
        builder.Property(x => x.PublishedAt).HasColumnName("published_at").IsRequired();
        builder.Property(x => x.LineItemsJson).HasColumnName("line_items").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.TermsTextJson).HasColumnName("terms_text").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.TermsDays).HasColumnName("terms_days").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.ValidityExtends).HasColumnName("validity_extends").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.TotalsSummaryJson).HasColumnName("totals_summary").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.CustomerRevisionCommentJson).HasColumnName("customer_revision_comment").HasColumnType("jsonb");

        builder.HasOne<Quote>()
            .WithMany()
            .HasForeignKey(x => x.QuoteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.QuoteId, x.VersionNumber, x.MarketCode })
            .IsUnique()
            .HasDatabaseName("UX_quote_versions_quote_version_market");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("chk_quote_versions_version_positive", "version_number > 0");
            t.HasCheckConstraint("chk_quote_versions_terms_days_nonneg", "terms_days >= 0");
            t.HasCheckConstraint("chk_quote_versions_market", "market_code::text IN ('eg','ksa')");
        });
        // Row-level immutability is enforced by a Postgres BEFORE-UPDATE trigger on
        // b2b.quote_versions (see migration). Validated by QuoteVersionImmutabilityTests
        // and StateTransitionAppendOnlyTriggerTests.
    }
}

public sealed class QuoteVersionDocumentConfiguration : IEntityTypeConfiguration<QuoteVersionDocument>
{
    public void Configure(EntityTypeBuilder<QuoteVersionDocument> builder)
    {
        builder.ToTable("quote_version_documents", "b2b");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.QuoteVersionId).HasColumnName("quote_version_id").IsRequired();
        builder.Property(x => x.MarketCode).HasColumnName("market_code").HasColumnType("citext").IsRequired().HasMaxLength(8);
        builder.Property(x => x.Locale).HasColumnName("locale").HasColumnType("citext").IsRequired();
        builder.Property(x => x.StorageKey).HasColumnName("storage_key").HasColumnType("text").IsRequired().HasMaxLength(1024);
        builder.Property(x => x.ContentType).HasColumnName("content_type").HasColumnType("text").HasDefaultValue("application/pdf").IsRequired();
        builder.Property(x => x.GeneratedAt).HasColumnName("generated_at").IsRequired();

        builder.HasOne<QuoteVersion>()
            .WithMany()
            .HasForeignKey(x => x.QuoteVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.QuoteVersionId, x.Locale })
            .IsUnique()
            .HasDatabaseName("UX_quote_version_documents_version_locale");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("chk_qvd_locale", "locale::text IN ('en','ar')");
            t.HasCheckConstraint("chk_qvd_market", "market_code::text IN ('eg','ksa')");
        });
    }
}

public sealed class QuoteStateTransitionConfiguration : IEntityTypeConfiguration<QuoteStateTransition>
{
    public void Configure(EntityTypeBuilder<QuoteStateTransition> builder)
    {
        builder.ToTable("quote_state_transitions", "b2b");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.QuoteId).HasColumnName("quote_id").IsRequired();
        builder.Property(x => x.MarketCode).HasColumnName("market_code").HasColumnType("citext").IsRequired().HasMaxLength(8);
        builder.Property(x => x.PriorState).HasColumnName("prior_state").HasColumnType("citext").IsRequired();
        builder.Property(x => x.NewState).HasColumnName("new_state").HasColumnType("citext").IsRequired();
        builder.Property(x => x.ActorKind).HasColumnName("actor_kind").HasColumnType("citext").IsRequired();
        builder.Property(x => x.ActorId).HasColumnName("actor_id");
        builder.Property(x => x.ReasonJson).HasColumnName("reason").HasColumnType("jsonb");
        builder.Property(x => x.MetadataJson).HasColumnName("metadata").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb").IsRequired();
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired();

        builder.HasOne<Quote>()
            .WithMany()
            .HasForeignKey(x => x.QuoteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.QuoteId, x.OccurredAt })
            .HasDatabaseName("IX_quote_state_transitions_quote_occurred");

        builder.ToTable(t =>
        {
            // `__none__` is the sentinel prior-state for the initial insert; otherwise
            // both prior and new MUST be a valid QuoteState token.
            t.HasCheckConstraint("chk_qst_prior_state",
                "prior_state::text IN ('__none__','requested','drafted','revised','pending-approver','accepted','rejected','expired','withdrawn')");
            t.HasCheckConstraint("chk_qst_new_state",
                "new_state::text IN ('requested','drafted','revised','pending-approver','accepted','rejected','expired','withdrawn')");
            t.HasCheckConstraint("chk_qst_actor_kind",
                "actor_kind::text IN ('customer','buyer','approver','admin_operator','system')");
            t.HasCheckConstraint("chk_qst_market", "market_code::text IN ('eg','ksa')");
        });
        // Append-only Postgres trigger added in raw SQL inside the migration.
    }
}

public sealed class QuoteMarketSchemaConfiguration : IEntityTypeConfiguration<QuoteMarketSchema>
{
    public void Configure(EntityTypeBuilder<QuoteMarketSchema> builder)
    {
        builder.ToTable("quote_market_schemas", "b2b");
        builder.HasKey(x => new { x.MarketCode, x.Version });

        builder.Property(x => x.MarketCode).HasColumnName("market_code").HasColumnType("citext").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsRequired();
        builder.Property(x => x.EffectiveFrom).HasColumnName("effective_from").IsRequired();
        builder.Property(x => x.EffectiveTo).HasColumnName("effective_to");
        builder.Property(x => x.ValidityDays).HasColumnName("validity_days").HasDefaultValue(14).IsRequired();
        builder.Property(x => x.RateLimitPerCustomerPerHour).HasColumnName("rate_limit_per_customer_per_hour").HasDefaultValue(10).IsRequired();
        builder.Property(x => x.RateLimitPerCompanyPerHour).HasColumnName("rate_limit_per_company_per_hour").HasDefaultValue(50).IsRequired();
        builder.Property(x => x.CompanyVerificationRequired).HasColumnName("company_verification_required").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.TaxPreviewDriftThresholdPct).HasColumnName("tax_preview_drift_threshold_pct").HasColumnType("numeric(5,2)").HasDefaultValue(5.00m).IsRequired();
        builder.Property(x => x.SlaDecisionBusinessDays).HasColumnName("sla_decision_business_days").HasDefaultValue(2).IsRequired();
        builder.Property(x => x.SlaWarningBusinessDays).HasColumnName("sla_warning_business_days").HasDefaultValue(1).IsRequired();
        builder.Property(x => x.InvitationTtlDays).HasColumnName("invitation_ttl_days").HasDefaultValue(14).IsRequired();
        builder.Property(x => x.HolidaysListJson).HasColumnName("holidays_list").HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb").IsRequired();

        builder.HasIndex(x => x.MarketCode)
            .IsUnique()
            .HasFilter("effective_to IS NULL")
            .HasDatabaseName("UX_quote_market_schemas_active_per_market");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("chk_qms_market", "market_code::text IN ('eg','ksa')");
            t.HasCheckConstraint("chk_qms_validity_positive", "validity_days > 0");
            t.HasCheckConstraint("chk_qms_invitation_ttl_positive", "invitation_ttl_days > 0");
            t.HasCheckConstraint("chk_qms_drift_in_range",
                "tax_preview_drift_threshold_pct >= 0 AND tax_preview_drift_threshold_pct <= 100");
            t.HasCheckConstraint("chk_qms_sla_nonneg",
                "sla_decision_business_days >= 0 AND sla_warning_business_days >= 0");
            t.HasCheckConstraint("chk_qms_rate_limits_positive",
                "rate_limit_per_customer_per_hour > 0 AND rate_limit_per_company_per_hour > 0");
        });
    }
}

public sealed class RepeatOrderTemplateConfiguration : IEntityTypeConfiguration<RepeatOrderTemplate>
{
    public void Configure(EntityTypeBuilder<RepeatOrderTemplate> builder)
    {
        builder.ToTable("repeat_order_templates", "b2b");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SourceQuoteId).HasColumnName("source_quote_id").IsRequired();
        builder.Property(x => x.MarketCode).HasColumnName("market_code").HasColumnType("citext").IsRequired().HasMaxLength(8);
        builder.Property(x => x.CompanyId).HasColumnName("company_id");
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.NameJson).HasColumnName("name").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by").IsRequired();

        builder.HasOne<Quote>()
            .WithMany()
            .HasForeignKey(x => x.SourceQuoteId)
            .OnDelete(DeleteBehavior.Restrict);

        // Two unique partial indexes (research §R12). The name column is jsonb; the partial
        // index is over the (id, name) pair where the appropriate scope column is non-null.
        // Postgres allows expression indexes against jsonb but the simple form below treats
        // the entire jsonb as the uniqueness key — adequate because handlers normalize the
        // jsonb shape on insert (sorted keys, no whitespace). A future migration MAY swap
        // these for expression indexes on `name->>'en'` if collation behavior demands it.
        builder.HasIndex(x => new { x.CompanyId, x.NameJson })
            .IsUnique()
            .HasFilter("company_id IS NOT NULL")
            .HasDatabaseName("UX_repeat_order_templates_company_name");
        builder.HasIndex(x => new { x.UserId, x.NameJson })
            .IsUnique()
            .HasFilter("company_id IS NULL")
            .HasDatabaseName("UX_repeat_order_templates_user_name");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("chk_repeat_order_templates_market",
                "market_code::text IN ('eg','ksa')");
        });
    }
}

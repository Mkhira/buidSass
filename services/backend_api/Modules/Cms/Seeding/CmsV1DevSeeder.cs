using System.Text.Json;
using BackendApi.Features.Seeding;
using BackendApi.Modules.Cms.Entities;
using BackendApi.Modules.Cms.Persistence;
using BackendApi.Modules.Cms.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Cms.Seeding;

/// <summary>
/// Dev/staging seeder per spec 024 tasks T138–T144. Populates synthetic
/// content spanning all 5 entity kinds × 4 lifecycle states with bilingual
/// coverage and per-market + cross-market `*` examples.
/// SeedGuard refuses to run in Production via the SeedContext.
/// Idempotent: re-running converges to a no-op (natural-key dedup per kind).
/// </summary>
public sealed class CmsV1DevSeeder : ISeeder
{
    public string Name => "cms.v1.dev";
    public int Version => 1;
    public IReadOnlyList<string> DependsOn => new[] { "cms.reference-data" };

    public async Task ApplyAsync(SeedContext ctx, CancellationToken ct)
    {
        var db = ctx.Services.GetRequiredService<CmsDbContext>();
        var nowUtc = DateTimeOffset.UtcNow;

        await SeedBannersAsync(db, nowUtc, ct);
        await SeedFeaturedSectionsAsync(db, nowUtc, ct);
        await SeedFaqEntriesAsync(db, nowUtc, ct);
        await SeedLegalPagesAsync(db, nowUtc, ct);
        await SeedBlogArticlesAsync(db, nowUtc, ct);
    }

    /// <summary>
    /// Dry-run mode per spec 024 task T136. Computes the planned changes
    /// against the live database without writing anything: returns a per-kind
    /// breakdown of <c>{kind, would_insert_count, already_present_count}</c>.
    /// Idempotent and side-effect-free. Used by the CLI <c>--mode=dry-run</c>
    /// flag and by integration tests that need to assert "this seeder would
    /// converge a fresh DB" without committing.
    /// </summary>
    public async Task<CmsV1DevSeederDryRunReport> ComputeDryRunAsync(
        SeedContext ctx, CancellationToken ct)
    {
        var db = ctx.Services.GetRequiredService<CmsDbContext>();
        var bannerPlan = await PlanBannersAsync(db, ct);
        var featuredPlan = await PlanFeaturedSectionsAsync(db, ct);
        var faqPlan = await PlanFaqEntriesAsync(db, ct);
        var legalPlan = await PlanLegalPagesAsync(db, ct);
        var blogPlan = await PlanBlogArticlesAsync(db, ct);
        return new CmsV1DevSeederDryRunReport(
            BannerSlots: bannerPlan,
            FeaturedSections: featuredPlan,
            FaqEntries: faqPlan,
            LegalPageVersions: legalPlan,
            BlogArticles: blogPlan);
    }

    private static async Task SeedBannersAsync(CmsDbContext db, DateTimeOffset nowUtc, CancellationToken ct)
    {
        var samples = new (string SlotKind, string Market, string HeadlineEn, string HeadlineAr, string State)[]
        {
            ("hero_top",        "EG",  "Welcome to Build Sass",        "أهلًا بكم في بيلد ساس",      "live"),
            ("category_strip",  "KSA", "Implants — KSA",                "زراعة الأسنان — السعودية",  "live"),
            ("footer_strip",    "EG",  "Free shipping over EGP 1000",   "شحن مجاني فوق ١٠٠٠ ج.م.",   "live"),
            ("home_secondary",  "KSA", "Ramadan promotion",             "عرض رمضان",                  "scheduled"),
            ("hero_top",        "*",   "Global brand kickoff",          "إطلاق العلامة العالمية",     "draft"),
            ("category_strip",  "EG",  "Last month sale",               "تخفيضات الشهر الماضي",       "archived"),
        };

        foreach (var s in samples)
        {
            var natural = $"{s.SlotKind}|{s.Market}|{s.HeadlineEn}";
            var exists = await db.BannerSlots.AnyAsync(b =>
                b.SlotKindWire == s.SlotKind &&
                b.MarketCode == s.Market &&
                b.HeadlineEn == s.HeadlineEn, ct);
            if (exists) continue;

            // Per-state event-order chronology: archived banners must have
            // been created + published BEFORE they were archived. Live /
            // scheduled / draft use nowUtc-anchored timestamps.
            var createdAt = s.State == "archived" ? nowUtc.AddDays(-60) : nowUtc;
            var publishedAt = s.State switch
            {
                "live" => nowUtc,
                "archived" => nowUtc.AddDays(-45),
                _ => (DateTimeOffset?)null,
            };
            await db.BannerSlots.AddAsync(new BannerSlot
            {
                Id = Guid.NewGuid(),
                SlotKindWire = s.SlotKind,
                HeadlineEn = s.HeadlineEn,
                HeadlineAr = s.HeadlineAr,
                CtaKindWire = "none",
                MarketCode = s.Market,
                StateWire = s.State,
                PriorityWithinSlot = 100,
                OwnerActorId = Guid.Empty,
                CreatedAtUtc = createdAt,
                EditorSaveAtUtc = createdAt,
                PublishedAtUtc = publishedAt,
                ScheduledStartUtc = s.State == "scheduled" ? nowUtc.AddDays(7) : null,
                ScheduledEndUtc = s.State == "scheduled" ? nowUtc.AddDays(37) : null,
                ArchivedAtUtc = s.State == "archived" ? nowUtc.AddDays(-30) : null,
                ArchiveReasonNote = s.State == "archived" ? "campaign concluded" : null,
                CtaHealthWire = "not_applicable",
            }, ct);
        }
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedFeaturedSectionsAsync(CmsDbContext db, DateTimeOffset nowUtc, CancellationToken ct)
    {
        var samples = new (string Kind, string Market, string TitleEn, string TitleAr)[]
        {
            ("home_top",         "EG",  "Top Picks",            "الأكثر مبيعًا"),
            ("home_mid",         "KSA", "Recommended for you",  "موصى لك"),
            ("category_landing", "EG",  "Premium Implants",     "زراعة متقدمة"),
            ("b2b_landing",      "KSA", "Clinic Bulk Deals",    "عروض العيادات بالجملة"),
        };

        foreach (var s in samples)
        {
            var exists = await db.FeaturedSections.AnyAsync(f =>
                f.SectionKindWire == s.Kind &&
                f.MarketCode == s.Market &&
                f.TitleEn == s.TitleEn, ct);
            if (exists) continue;

            await db.FeaturedSections.AddAsync(new FeaturedSection
            {
                Id = Guid.NewGuid(),
                SectionKindWire = s.Kind,
                MarketCode = s.Market,
                TitleEn = s.TitleEn,
                TitleAr = s.TitleAr,
                ReferencesJson = "[]",
                StateWire = "draft",
                DisplayPriority = 100,
                OwnerActorId = Guid.Empty,
                CreatedAtUtc = nowUtc,
                EditorSaveAtUtc = nowUtc,
            }, ct);
        }
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedFaqEntriesAsync(CmsDbContext db, DateTimeOffset nowUtc, CancellationToken ct)
    {
        var samples = new (string Category, string Market, string QuestionEn, string QuestionAr,
                          string AnswerEn, string AnswerAr, string State)[]
        {
            ("ordering",      "EG",  "How do I place an order?",       "كيف أقوم بطلب منتج؟",
             "Add to cart and proceed to checkout.", "أضف إلى السلة ثم تابع إلى الدفع.",   "live"),
            ("ordering",      "KSA", "How do I place an order?",       "كيف أقوم بطلب منتج؟",
             "Add to cart and proceed to checkout.", "أضف إلى السلة ثم تابع إلى الدفع.",   "live"),
            ("payment",       "EG",  "What payment methods are accepted?", "ما طرق الدفع المتاحة؟",
             "Visa, MasterCard, COD.", "فيزا، ماستركارد، الدفع عند الاستلام.",                "live"),
            ("shipping",      "KSA", "How long does delivery take?",   "كم مدة التوصيل؟",
             "2–5 business days.", "٢–٥ أيام عمل.",                                            "live"),
            ("returns",       "EG",  "What is your return policy?",    "ما هي سياسة الإرجاع؟",
             "30-day returns on unopened items.", "إرجاع خلال ٣٠ يومًا للمنتجات غير المفتوحة.", "live"),
            ("verification",  "KSA", "How do I verify as a dentist?",  "كيف أوثّق حسابي كطبيب؟",
             "Upload your license card.", "ارفع بطاقة الترخيص.",                              "live"),
            ("b2b",           "KSA", "How do I open a clinic account?", "كيف أفتح حسابًا للعيادة؟",
             "Contact our B2B team.", "تواصل مع فريقنا للأعمال.",                              "draft"),
            ("account",       "EG",  "Can I update my email?",         "هل أستطيع تغيير بريدي؟",
             "Yes, in Account Settings.", "نعم، من إعدادات الحساب.",                          "live"),
        };

        foreach (var s in samples)
        {
            var exists = await db.FaqEntries.AnyAsync(f =>
                f.CategoryWire == s.Category &&
                f.MarketCode == s.Market &&
                f.QuestionEn == s.QuestionEn, ct);
            if (exists) continue;

            await db.FaqEntries.AddAsync(new FaqEntry
            {
                Id = Guid.NewGuid(),
                CategoryWire = s.Category,
                MarketCode = s.Market,
                QuestionEn = s.QuestionEn,
                QuestionAr = s.QuestionAr,
                AnswerEn = s.AnswerEn,
                AnswerAr = s.AnswerAr,
                StateWire = s.State,
                DisplayOrder = 100,
                OwnerActorId = Guid.Empty,
                CreatedAtUtc = nowUtc,
                EditorSaveAtUtc = nowUtc,
                PublishedAtUtc = s.State == "live" ? nowUtc : null,
            }, ct);
        }
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedLegalPagesAsync(CmsDbContext db, DateTimeOffset nowUtc, CancellationToken ct)
    {
        var kinds = new[] { "terms", "privacy", "returns", "cookies" };
        var markets = new[] { "EG", "KSA", "*" };

        foreach (var kind in kinds)
        {
            foreach (var market in markets)
            {
                // Idempotency is two-sided per (kind, market): we create the
                // prior `v1.0` superseded row and the current `v2.0` live row
                // independently. CodeRabbit (PR #53) flagged the prior
                // one-sided approach (only checked v1.0) — under partial state
                // it would either fail to heal a missing live row or attempt
                // a duplicate insert.
                var priorExists = await db.LegalPageVersions.AnyAsync(v =>
                    v.LegalPageKindWire == kind &&
                    v.MarketCode == market &&
                    v.VersionLabel == "v1.0", ct);
                var liveExists = await db.LegalPageVersions.AnyAsync(v =>
                    v.LegalPageKindWire == kind &&
                    v.MarketCode == market &&
                    v.VersionLabel == "v2.0", ct);

                // Pre-allocate the live id so the prior row's superseded_by
                // pointer matches whichever row we end up persisting.
                var existingLiveId = liveExists
                    ? await db.LegalPageVersions
                        .Where(v => v.LegalPageKindWire == kind
                            && v.MarketCode == market
                            && v.VersionLabel == "v2.0")
                        .Select(v => (Guid?)v.Id)
                        .FirstAsync(ct)
                    : (Guid?)null;
                var liveId = existingLiveId ?? Guid.NewGuid();

                if (!priorExists)
                {
                    await db.LegalPageVersions.AddAsync(new LegalPageVersion
                    {
                        Id = Guid.NewGuid(),
                        LegalPageKindWire = kind,
                        MarketCode = market,
                        VersionLabel = "v1.0",
                        BodyEn = $"This is the {kind} ({market}) prior version.",
                        BodyAr = $"هذه نسخة {kind} السابقة لـ {market}.",
                        EffectiveAtUtc = nowUtc.AddDays(-90),
                        StateWire = "superseded",
                        SupersededAtUtc = nowUtc.AddDays(-30),
                        SupersededByVersionId = liveId,
                        OwnerActorId = Guid.Empty,
                        CreatedAtUtc = nowUtc.AddDays(-91),
                        EditorSaveAtUtc = nowUtc.AddDays(-91),
                        PublishedAtUtc = nowUtc.AddDays(-90),
                    }, ct);
                }
                if (!liveExists)
                {
                    await db.LegalPageVersions.AddAsync(new LegalPageVersion
                    {
                        Id = liveId,
                        LegalPageKindWire = kind,
                        MarketCode = market,
                        VersionLabel = "v2.0",
                        BodyEn = $"This is the current {kind} ({market}) version.",
                        BodyAr = $"هذه النسخة الحالية لـ {kind} ({market}).",
                        EffectiveAtUtc = nowUtc.AddDays(-30),
                        StateWire = "live",
                        OwnerActorId = Guid.Empty,
                        CreatedAtUtc = nowUtc.AddDays(-31),
                        EditorSaveAtUtc = nowUtc.AddDays(-31),
                        PublishedAtUtc = nowUtc.AddDays(-30),
                    }, ct);
                }
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedBlogArticlesAsync(CmsDbContext db, DateTimeOffset nowUtc, CancellationToken ct)
    {
        var samples = new (string Slug, string Locale, string Market, string Title, string Body, string Category, string State)[]
        {
            ("ramadan-tips-2026",       "ar", "EG",  "نصائح رمضان 2026",       "نصائح للأطباء خلال شهر رمضان...", "tips",   "live"),
            ("ramadan-tips-2026",       "en", "EG",  "Ramadan Tips 2026",      "Practical tips for clinicians during Ramadan...", "tips", "live"),
            ("new-implant-protocols",   "en", "*",   "New Implant Protocols",  "We've updated our recommended protocols...", "guides", "live"),
            ("ksa-license-update",      "ar", "KSA", "تحديث ترخيص السعودية",   "تحديثات على متطلبات الترخيص...", "news", "live"),
            ("draft-blog-1",            "en", "EG",  "Draft Article 1",        "Draft body for an unpublished article.", "tips", "draft"),
            ("scheduled-summer-promo",  "ar", "KSA", "عرض الصيف المجدول",      "عرض الصيف القادم...", "news", "scheduled"),
        };

        foreach (var s in samples)
        {
            var exists = await db.BlogArticles.AnyAsync(b =>
                b.MarketCode == s.Market &&
                b.AuthoredLocale == s.Locale &&
                b.Slug == s.Slug, ct);
            if (exists) continue;

            // Per-state chronology: live blogs must have been authored
            // BEFORE they were published. CreatedAt is pushed back so
            // PublishedAt = nowUtc.AddDays(-7) is later than CreatedAt.
            var createdAt = s.State == "live" ? nowUtc.AddDays(-8) : nowUtc;
            await db.BlogArticles.AddAsync(new BlogArticle
            {
                Id = Guid.NewGuid(),
                CategoryWire = s.Category,
                Slug = s.Slug,
                AuthoredLocale = s.Locale,
                Title = s.Title,
                Summary = s.Title,
                Body = s.Body,
                MarketCode = s.Market,
                StateWire = s.State,
                SeoMetaTitle = s.Title,
                SeoMetaDescription = s.Title,
                SeoSchemaOrgKind = "BlogPosting",
                OwnerActorId = Guid.Empty,
                CreatedAtUtc = createdAt,
                EditorSaveAtUtc = createdAt,
                PublishedAtUtc = s.State == "live" ? nowUtc.AddDays(-7) : null,
                ScheduledPublishAtUtc = s.State == "scheduled" ? nowUtc.AddDays(14) : null,
            }, ct);
        }
        await db.SaveChangesAsync(ct);
    }

    // ── Dry-run planners ─────────────────────────────────────────────────
    // Each planner mirrors its Seed*Async sibling but only counts what would
    // be inserted vs already-present. Side-effect-free (no AddAsync, no
    // SaveChangesAsync). Mirrors the natural-key dedup logic of the writer.

    private static async Task<CmsV1DevSeederKindPlan> PlanBannersAsync(CmsDbContext db, CancellationToken ct)
    {
        var samples = new (string SlotKind, string Market, string HeadlineEn)[]
        {
            ("hero_top",        "EG",  "Welcome to Build Sass"),
            ("category_strip",  "KSA", "Implants — KSA"),
            ("footer_strip",    "EG",  "Free shipping over EGP 1000"),
            ("home_secondary",  "KSA", "Ramadan promotion"),
            ("hero_top",        "*",   "Global brand kickoff"),
            ("category_strip",  "EG",  "Last month sale"),
        };
        var present = 0;
        foreach (var s in samples)
        {
            var exists = await db.BannerSlots.AnyAsync(b =>
                b.SlotKindWire == s.SlotKind &&
                b.MarketCode == s.Market &&
                b.HeadlineEn == s.HeadlineEn, ct);
            if (exists) present++;
        }
        return new CmsV1DevSeederKindPlan(
            Kind: "banner_slot",
            WouldInsertCount: samples.Length - present,
            AlreadyPresentCount: present);
    }

    private static async Task<CmsV1DevSeederKindPlan> PlanFeaturedSectionsAsync(CmsDbContext db, CancellationToken ct)
    {
        var samples = new (string Kind, string Market, string TitleEn)[]
        {
            ("home_top",         "EG",  "Top Picks"),
            ("home_mid",         "KSA", "Recommended for you"),
            ("category_landing", "EG",  "Premium Implants"),
            ("b2b_landing",      "KSA", "Clinic Bulk Deals"),
        };
        var present = 0;
        foreach (var s in samples)
        {
            var exists = await db.FeaturedSections.AnyAsync(f =>
                f.SectionKindWire == s.Kind &&
                f.MarketCode == s.Market &&
                f.TitleEn == s.TitleEn, ct);
            if (exists) present++;
        }
        return new CmsV1DevSeederKindPlan(
            Kind: "featured_section",
            WouldInsertCount: samples.Length - present,
            AlreadyPresentCount: present);
    }

    private static async Task<CmsV1DevSeederKindPlan> PlanFaqEntriesAsync(CmsDbContext db, CancellationToken ct)
    {
        var samples = new (string Category, string Market, string QuestionEn)[]
        {
            ("ordering",      "EG",  "How do I place an order?"),
            ("ordering",      "KSA", "How do I place an order?"),
            ("payment",       "EG",  "What payment methods are accepted?"),
            ("shipping",      "KSA", "How long does delivery take?"),
            ("returns",       "EG",  "What is your return policy?"),
            ("verification",  "KSA", "How do I verify as a dentist?"),
            ("b2b",           "KSA", "How do I open a clinic account?"),
            ("account",       "EG",  "Can I update my email?"),
        };
        var present = 0;
        foreach (var s in samples)
        {
            var exists = await db.FaqEntries.AnyAsync(f =>
                f.CategoryWire == s.Category &&
                f.MarketCode == s.Market &&
                f.QuestionEn == s.QuestionEn, ct);
            if (exists) present++;
        }
        return new CmsV1DevSeederKindPlan(
            Kind: "faq_entry",
            WouldInsertCount: samples.Length - present,
            AlreadyPresentCount: present);
    }

    private static async Task<CmsV1DevSeederKindPlan> PlanLegalPagesAsync(CmsDbContext db, CancellationToken ct)
    {
        var kinds = new[] { "terms", "privacy", "returns", "cookies" };
        var markets = new[] { "EG", "KSA", "*" };
        var totalRows = kinds.Length * markets.Length * 2; // v1.0 + v2.0 per pair
        var present = 0;
        foreach (var kind in kinds)
        {
            foreach (var market in markets)
            {
                var prior = await db.LegalPageVersions.AnyAsync(v =>
                    v.LegalPageKindWire == kind && v.MarketCode == market && v.VersionLabel == "v1.0", ct);
                var live = await db.LegalPageVersions.AnyAsync(v =>
                    v.LegalPageKindWire == kind && v.MarketCode == market && v.VersionLabel == "v2.0", ct);
                if (prior) present++;
                if (live) present++;
            }
        }
        return new CmsV1DevSeederKindPlan(
            Kind: "legal_page_version",
            WouldInsertCount: totalRows - present,
            AlreadyPresentCount: present);
    }

    private static async Task<CmsV1DevSeederKindPlan> PlanBlogArticlesAsync(CmsDbContext db, CancellationToken ct)
    {
        var samples = new (string Slug, string Locale, string Market)[]
        {
            ("ramadan-tips-2026",       "ar", "EG"),
            ("ramadan-tips-2026",       "en", "EG"),
            ("new-implant-protocols",   "en", "*"),
            ("ksa-license-update",      "ar", "KSA"),
            ("draft-blog-1",            "en", "EG"),
            ("scheduled-summer-promo",  "ar", "KSA"),
        };
        var present = 0;
        foreach (var s in samples)
        {
            var exists = await db.BlogArticles.AnyAsync(b =>
                b.MarketCode == s.Market &&
                b.AuthoredLocale == s.Locale &&
                b.Slug == s.Slug, ct);
            if (exists) present++;
        }
        return new CmsV1DevSeederKindPlan(
            Kind: "blog_article",
            WouldInsertCount: samples.Length - present,
            AlreadyPresentCount: present);
    }
}

/// <summary>Per-kind plan emitted by <see cref="CmsV1DevSeeder.ComputeDryRunAsync"/>.</summary>
public sealed record CmsV1DevSeederKindPlan(
    string Kind,
    int WouldInsertCount,
    int AlreadyPresentCount);

/// <summary>Aggregate dry-run report per spec 024 task T136.</summary>
public sealed record CmsV1DevSeederDryRunReport(
    CmsV1DevSeederKindPlan BannerSlots,
    CmsV1DevSeederKindPlan FeaturedSections,
    CmsV1DevSeederKindPlan FaqEntries,
    CmsV1DevSeederKindPlan LegalPageVersions,
    CmsV1DevSeederKindPlan BlogArticles)
{
    public int TotalWouldInsert =>
        BannerSlots.WouldInsertCount + FeaturedSections.WouldInsertCount +
        FaqEntries.WouldInsertCount + LegalPageVersions.WouldInsertCount +
        BlogArticles.WouldInsertCount;

    public int TotalAlreadyPresent =>
        BannerSlots.AlreadyPresentCount + FeaturedSections.AlreadyPresentCount +
        FaqEntries.AlreadyPresentCount + LegalPageVersions.AlreadyPresentCount +
        BlogArticles.AlreadyPresentCount;
}

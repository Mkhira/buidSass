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
                CreatedAtUtc = nowUtc,
                EditorSaveAtUtc = nowUtc,
                PublishedAtUtc = s.State == "live" || s.State == "archived" ? nowUtc : null,
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
            ("delivery",      "KSA", "How long does delivery take?",   "كم مدة التوصيل؟",
             "2–5 business days.", "٢–٥ أيام عمل.",                                            "live"),
            ("returns",       "EG",  "What is your return policy?",    "ما هي سياسة الإرجاع؟",
             "30-day returns on unopened items.", "إرجاع خلال ٣٠ يومًا للمنتجات غير المفتوحة.", "live"),
            ("verification",  "KSA", "How do I verify as a dentist?",  "كيف أوثّق حسابي كطبيب؟",
             "Upload your license card.", "ارفع بطاقة الترخيص.",                              "live"),
            ("b2b",           "KSA", "How do I open a clinic account?", "كيف أفتح حسابًا للعيادة؟",
             "Contact our B2B team.", "تواصل مع فريقنا للأعمال.",                              "draft"),
            ("technical",     "EG",  "Can I update my email?",         "هل أستطيع تغيير بريدي؟",
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
                // Prior superseded version (effective_at -90d).
                var priorLabel = "v1.0";
                var priorExists = await db.LegalPageVersions.AnyAsync(v =>
                    v.LegalPageKindWire == kind &&
                    v.MarketCode == market &&
                    v.VersionLabel == priorLabel, ct);
                if (!priorExists)
                {
                    var priorId = Guid.NewGuid();
                    var liveId = Guid.NewGuid();
                    await db.LegalPageVersions.AddAsync(new LegalPageVersion
                    {
                        Id = priorId,
                        LegalPageKindWire = kind,
                        MarketCode = market,
                        VersionLabel = priorLabel,
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
                CreatedAtUtc = nowUtc,
                EditorSaveAtUtc = nowUtc,
                PublishedAtUtc = s.State == "live" ? nowUtc.AddDays(-7) : null,
                ScheduledPublishAtUtc = s.State == "scheduled" ? nowUtc.AddDays(14) : null,
            }, ct);
        }
        await db.SaveChangesAsync(ct);
    }
}

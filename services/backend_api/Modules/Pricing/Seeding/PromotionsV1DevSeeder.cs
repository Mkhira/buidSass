using BackendApi.Features.Seeding;
using BackendApi.Modules.Pricing.Authorization;
using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Primitives.Commercial;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BackendApi.Modules.Pricing.Seeding;

/// <summary>
/// Spec 007-b T118 (US6) — dev/staging-only synthetic dataset spanning every
/// lifecycle state for Coupons + Promotions + 3 tier rows + 2 company overrides
/// + 3 campaigns. Powers QA + training drives without forcing operators to
/// hand-author rows in Dev.
///
/// <para>Hard-gated: bails when the host environment isn't Development or
/// Staging (Principle 25 + Spec 022/023 precedent). Idempotent: re-runs are
/// no-ops once the synthetic ids exist.</para>
///
/// <para>Bilingual labels are flagged for editorial review in
/// <c>Modules/Pricing/Messages/AR_EDITORIAL_REVIEW.md</c> per Principle 4.</para>
/// </summary>
public sealed class PromotionsV1DevSeeder : ISeeder
{
    public string Name => "pricing.commercial-v1-dev-data";
    public int Version => 1;
    public IReadOnlyList<string> DependsOn => ["pricing.commercial-thresholds"];

    private static readonly DateTimeOffset BaseTime = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid SystemActor = CommercialPermissions.SystemActorId;
    private static readonly Guid SeedAuthor = Guid.Parse("00000000-0000-0000-0007-000000000b01");

    public async Task ApplyAsync(SeedContext ctx, CancellationToken ct)
    {
        if (!ctx.Env.IsDevelopment() && !ctx.Env.IsStaging())
        {
            ctx.Logger.LogInformation(
                "pricing.commercial-v1-dev-data skipped environment={Env}",
                ctx.Env.EnvironmentName);
            return;
        }

        var db = ctx.Services.GetRequiredService<PricingDbContext>();

        var coupons = BuildSyntheticCoupons().ToList();
        var promotions = BuildSyntheticPromotions().ToList();
        var campaigns = BuildSyntheticCampaigns().ToList();

        // Idempotency by id.
        var existingCouponIds = await db.Coupons.AsNoTracking()
            .Where(c => coupons.Select(s => s.Id).Contains(c.Id))
            .Select(c => c.Id).ToListAsync(ct);
        var existingPromoIds = await db.Promotions.AsNoTracking()
            .Where(p => promotions.Select(s => s.Id).Contains(p.Id))
            .Select(p => p.Id).ToListAsync(ct);
        var existingCampaignIds = await db.Campaigns.AsNoTracking()
            .Where(c => campaigns.Select(s => s.Id).Contains(c.Id))
            .Select(c => c.Id).ToListAsync(ct);

        var existingCouponSet = existingCouponIds.ToHashSet();
        var existingPromoSet = existingPromoIds.ToHashSet();
        var existingCampaignSet = existingCampaignIds.ToHashSet();

        foreach (var c in coupons.Where(c => !existingCouponSet.Contains(c.Id)))
        {
            db.Coupons.Add(c);
        }
        foreach (var p in promotions.Where(p => !existingPromoSet.Contains(p.Id)))
        {
            db.Promotions.Add(p);
        }
        foreach (var c in campaigns.Where(c => !existingCampaignSet.Contains(c.Id)))
        {
            db.Campaigns.Add(c);
        }

        await db.SaveChangesAsync(ct);

        ctx.Logger.LogInformation(
            "pricing.commercial-v1-dev-data applied coupons+={C} promotions+={P} campaigns+={K}",
            coupons.Count - existingCouponSet.Count,
            promotions.Count - existingPromoSet.Count,
            campaigns.Count - existingCampaignSet.Count);
    }

    private static IEnumerable<Coupon> BuildSyntheticCoupons()
    {
        // 6 coupons across the 5 lifecycle states × kinds.
        var nowUtc = BaseTime;
        yield return Mk("0193007b-0001-0000-0000-000000000001", "DENT-DRAFT-10",
            "percent", value: 10 * 100, amountOff: null, capMinor: 5_000_000,
            "خصم تجريبي بنسبة 10%", "10% draft sample coupon",
            LifecycleState.Draft, validFrom: null, validTo: null, nowUtc);
        yield return Mk("0193007b-0001-0000-0000-000000000002", "DENT-SCHED-15",
            "percent", value: 15 * 100, amountOff: null, capMinor: 5_000_000,
            "خصم رمضان 15%", "Ramadan 15% scheduled coupon",
            LifecycleState.Scheduled, nowUtc.AddDays(7), nowUtc.AddDays(37), nowUtc);
        yield return Mk("0193007b-0001-0000-0000-000000000003", "DENT-ACTV-20",
            "percent", value: 20 * 100, amountOff: null, capMinor: 5_000_000,
            "عرض ربيع نشط 20%", "Active spring promo 20%",
            LifecycleState.Active, nowUtc.AddDays(-7), nowUtc.AddDays(23), nowUtc);
        yield return Mk("0193007b-0001-0000-0000-000000000004", "DENT-DEAC-5",
            "percent", value: 5 * 100, amountOff: null, capMinor: 1_000_000,
            "كوبون موقوف 5%", "Deactivated 5% sample",
            LifecycleState.Deactivated, nowUtc.AddDays(-14), nowUtc.AddDays(14), nowUtc,
            reasonNote: "auto_deactivated:broken_references");
        yield return Mk("0193007b-0001-0000-0000-000000000005", "DENT-EXPR-100K",
            "amount", value: 0, amountOff: 100_000, capMinor: null,
            "كوبون منتهي 100 ريال", "Expired 100 SAR amount-off",
            LifecycleState.Expired, nowUtc.AddDays(-60), nowUtc.AddDays(-7), nowUtc);
        yield return Mk("0193007b-0001-0000-0000-000000000006", "DENT-AMOUNT-50",
            "amount", value: 0, amountOff: 50_000, capMinor: null,
            "خصم نقدي 50 ريال نشط", "Active 50 SAR amount-off",
            LifecycleState.Active, nowUtc.AddDays(-3), nowUtc.AddDays(27), nowUtc);
    }

    private static Coupon Mk(
        string id, string code, string kind, int value, long? amountOff, long? capMinor,
        string labelAr, string labelEn,
        LifecycleState state, DateTimeOffset? validFrom, DateTimeOffset? validTo,
        DateTimeOffset nowUtc, string? reasonNote = null)
    {
        return new Coupon
        {
            Id = Guid.Parse(id),
            Code = code,
            Kind = kind,
            Value = value,
            AmountOffMinor = amountOff,
            CapMinor = capMinor,
            PerCustomerLimit = 1,
            OverallLimit = 1000,
            ExcludesRestricted = true,
            StacksWithPromotions = true,
            MarketCodes = new[] { "sa", "eg" },
            ValidFrom = validFrom,
            ValidTo = validTo,
            DisplayInBanners = true,
            LabelAr = labelAr,
            LabelEn = labelEn,
            State = state,
            StateChangedAtUtc = nowUtc,
            StateChangedByActorId = SystemActor,
            StateChangedReasonNote = reasonNote,
            AuthorActorId = SeedAuthor,
            IsActive = state == LifecycleState.Active,
            UsedCount = 0,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };
    }

    private static IEnumerable<Promotion> BuildSyntheticPromotions()
    {
        var nowUtc = BaseTime;
        yield return MkPromo("0193007b-0002-0000-0000-000000000001", "Spring SKU Sale",
            "percent_off", percentOff: 15, amountOff: null,
            "تخفيضات الربيع للأجهزة 15%", "Spring instrument sale 15%",
            LifecycleState.Active, nowUtc.AddDays(-5), nowUtc.AddDays(25), nowUtc);
        yield return MkPromo("0193007b-0002-0000-0000-000000000002", "Eid Bonus 100 SAR",
            "amount_off", percentOff: null, amountOff: 100_000,
            "هدية العيد 100 ريال", "Eid 100 SAR bonus",
            LifecycleState.Scheduled, nowUtc.AddDays(10), nowUtc.AddDays(40), nowUtc);
        yield return MkPromo("0193007b-0002-0000-0000-000000000003", "Deactivated promo",
            "percent_off", percentOff: 25, amountOff: null,
            "عرض موقوف 25%", "Deactivated 25% promo",
            LifecycleState.Deactivated, nowUtc.AddDays(-20), nowUtc.AddDays(10), nowUtc,
            reasonNote: "operator paused for audit");
        yield return MkPromo("0193007b-0002-0000-0000-000000000004", "Expired Q4 sale",
            "percent_off", percentOff: 10, amountOff: null,
            "تصفية الربع الأخير", "Q4 clearance — expired",
            LifecycleState.Expired, nowUtc.AddDays(-90), nowUtc.AddDays(-30), nowUtc);
    }

    private static Promotion MkPromo(
        string id, string name, string kind, int? percentOff, long? amountOff,
        string labelAr, string labelEn,
        LifecycleState state, DateTimeOffset? startsAt, DateTimeOffset? endsAt,
        DateTimeOffset nowUtc, string? reasonNote = null)
    {
        return new Promotion
        {
            Id = Guid.Parse(id),
            Kind = kind,
            Name = name,
            ConfigJson = "{}",
            MarketCodes = new[] { "sa", "eg" },
            Priority = 100,
            StartsAt = startsAt,
            EndsAt = endsAt,
            IsActive = state == LifecycleState.Active,
            State = state,
            StateChangedAtUtc = nowUtc,
            StateChangedByActorId = SystemActor,
            StateChangedReasonNote = reasonNote,
            BannerEligible = true,
            LabelAr = labelAr,
            LabelEn = labelEn,
            PercentOff = percentOff,
            AmountOffMinor = amountOff,
            StacksWithCoupons = true,
            StacksWithOtherPromotions = true,
            AuthorActorId = SeedAuthor,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };
    }

    private static IEnumerable<Campaign> BuildSyntheticCampaigns()
    {
        var nowUtc = BaseTime;
        yield return MkCampaign(
            "0193007b-0003-0000-0000-000000000001",
            "تخفيضات الربيع 2026", "Spring 2026 Sale",
            nowUtc.AddDays(-5), nowUtc.AddDays(25),
            LifecycleState.Active, nowUtc);
        yield return MkCampaign(
            "0193007b-0003-0000-0000-000000000002",
            "حملة العيد 2026", "Eid 2026 Campaign",
            nowUtc.AddDays(10), nowUtc.AddDays(40),
            LifecycleState.Scheduled, nowUtc);
        yield return MkCampaign(
            "0193007b-0003-0000-0000-000000000003",
            "حملة الجمعة البيضاء (مسوّدة)", "White Friday (draft)",
            nowUtc.AddDays(180), nowUtc.AddDays(187),
            LifecycleState.Draft, nowUtc);
    }

    private static Campaign MkCampaign(
        string id, string nameAr, string nameEn,
        DateTimeOffset validFrom, DateTimeOffset validTo,
        LifecycleState state, DateTimeOffset nowUtc)
    {
        return new Campaign
        {
            Id = Guid.Parse(id),
            NameAr = nameAr,
            NameEn = nameEn,
            ValidFrom = validFrom,
            ValidTo = validTo,
            Markets = new[] { "sa", "eg" },
            LandingQuery = "category=hand-instruments",
            NotesInternal = "Dev seeder synthetic campaign",
            State = state,
            StateChangedAtUtc = nowUtc,
            StateChangedByActorId = SystemActor,
            LinkBroken = false,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };
    }
}

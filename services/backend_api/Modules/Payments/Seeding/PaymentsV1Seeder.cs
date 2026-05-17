using BackendApi.Features.Seeding;
using BackendApi.Modules.Payments.Domain;
using BackendApi.Modules.Payments.Persistence;
using BackendApi.Modules.Payments.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Payments.Seeding;

/// <summary>
/// T073 — V1 reference-data seeder for the Payments module. Idempotent:
/// every Add is guarded by an existence check on a stable natural key.
/// Seeds the per-(market, method) <c>provider_routing</c> rows and the
/// <c>payment_methods_market_config</c> defaults so the customer
/// <c>GET /v1/payments/methods</c> endpoint returns the right set out of
/// the box.
/// </summary>
public sealed class PaymentsV1Seeder : ISeeder
{
    public string Name => "payments.reference-data";
    public int Version => 1;
    public IReadOnlyList<string> DependsOn => Array.Empty<string>();

    public async Task ApplyAsync(SeedContext ctx, CancellationToken ct)
    {
        var db = ctx.Services.GetRequiredService<PaymentsDbContext>();
        var nowUtc = DateTimeOffset.UtcNow;
        await SeedProviderRoutingAsync(db, nowUtc, ct);
        await SeedMethodConfigAsync(db, nowUtc, ct);
    }

    private static async Task SeedProviderRoutingAsync(PaymentsDbContext db, DateTimeOffset nowUtc, CancellationToken ct)
    {
        var seeds = new (string Market, string Method, string Primary, string? Backup)[]
        {
            // KSA cards: HyperPay primary, Tap backup.
            (PaymentsConstants.Markets.SA, PaymentsConstants.Methods.Card, PaymentsConstants.Providers.HyperPay, PaymentsConstants.Providers.Tap),
            (PaymentsConstants.Markets.SA, PaymentsConstants.Methods.Mada, PaymentsConstants.Providers.HyperPay, PaymentsConstants.Providers.Tap),
            (PaymentsConstants.Markets.SA, PaymentsConstants.Methods.ApplePay, PaymentsConstants.Providers.HyperPay, null),
            (PaymentsConstants.Markets.SA, PaymentsConstants.Methods.StcPay, PaymentsConstants.Providers.HyperPay, null),
            // KSA BNPL: Tabby + Tamara (concurrent — both surfaced; one row each with backup null).
            (PaymentsConstants.Markets.SA, PaymentsConstants.Methods.BnplTabby, PaymentsConstants.Providers.Tabby, null),
            (PaymentsConstants.Markets.SA, PaymentsConstants.Methods.BnplTamara, PaymentsConstants.Providers.Tamara, null),
            // EG cards: Paymob primary, Kashier backup.
            (PaymentsConstants.Markets.EG, PaymentsConstants.Methods.Card, PaymentsConstants.Providers.Paymob, PaymentsConstants.Providers.Kashier),
            (PaymentsConstants.Markets.EG, PaymentsConstants.Methods.ApplePay, PaymentsConstants.Providers.Paymob, null),
            (PaymentsConstants.Markets.EG, PaymentsConstants.Methods.Meeza, PaymentsConstants.Providers.Paymob, null),
            (PaymentsConstants.Markets.EG, PaymentsConstants.Methods.BnplValu, PaymentsConstants.Providers.Valu, null),
        };
        var present = await db.ProviderRoutings.Select(r => new { r.MarketCode, r.Method }).ToListAsync(ct);
        var presentKeys = present.Select(p => (p.MarketCode, p.Method)).ToHashSet();
        foreach (var s in seeds)
        {
            if (presentKeys.Contains((s.Market, s.Method))) continue;
            db.ProviderRoutings.Add(new ProviderRouting
            {
                MarketCode = s.Market,
                Method = s.Method,
                PrimaryProviderId = s.Primary,
                BackupProviderId = s.Backup,
                AutoFailoverEnabled = false, // BR-13 default-off (AC-34)
                FailoverThresholdPct = 50,
                FailoverWindowMinutes = 5,
                UpdatedAt = nowUtc,
            });
        }
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedMethodConfigAsync(PaymentsDbContext db, DateTimeOffset nowUtc, CancellationToken ct)
    {
        var present = await db.PaymentMethodsMarketConfigs.Select(c => new { c.MarketCode, c.Method }).ToListAsync(ct);
        var presentKeys = present.Select(p => (p.MarketCode, p.Method)).ToHashSet();
        foreach (var market in PaymentsConstants.Markets.All)
        {
            if (!PaymentsConstants.Methods.ByMarket.TryGetValue(market, out var methods)) continue;
            foreach (var method in methods)
            {
                if (presentKeys.Contains((market, method))) continue;
                // BR-9 — COD default eligibility is on at v1, but capped at SAR 5000 / EGP 50000.
                decimal? maxCart = method == PaymentsConstants.Methods.Cod
                    ? (market == PaymentsConstants.Markets.SA ? 5000m : 50000m)
                    : null;
                db.PaymentMethodsMarketConfigs.Add(new PaymentMethodMarketConfig
                {
                    MarketCode = market,
                    Method = method,
                    Enabled = true,
                    MinCartTotal = null,
                    MaxCartTotal = maxCart,
                    EligibilityJson = "{}",
                    UpdatedAt = nowUtc,
                });
            }
        }
        await db.SaveChangesAsync(ct);
    }
}

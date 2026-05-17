using BackendApi.Modules.Shipping.Domain;
using BackendApi.Modules.Shipping.Persistence;
using BackendApi.Modules.Shipping.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Shipping.Quote;

/// <summary>
/// Customer-facing quote command — resolves (market, address, weight,
/// cart_total) into the list of eligible methods + per-method fees
/// (per Flow 1 / AC-4 / AC-5).
/// </summary>
public sealed record QuoteShippingCommand(
    string MarketCode,
    string? City,
    string? PostalCode,
    decimal WeightKg,
    decimal CartTotal,
    string? CustomerTier) : IRequest<QuoteShippingResult>;

public sealed record QuoteShippingResult(
    bool ZoneResolved,
    Guid? ZoneId,
    IReadOnlyList<EligibleMethod> Methods);

public sealed record EligibleMethod(
    Guid MethodId,
    Guid MethodVersionId,
    string NameAr,
    string NameEn,
    decimal FeeAmount,
    string Currency,
    int EtaMinHours,
    int EtaMaxHours);

public sealed class QuoteShippingHandler(
    ShippingDbContext db,
    ZoneResolver zoneResolver,
    TimeProvider clock) : IRequestHandler<QuoteShippingCommand, QuoteShippingResult>
{
    public async Task<QuoteShippingResult> Handle(QuoteShippingCommand cmd, CancellationToken ct)
    {
        if (cmd.WeightKg < 0)
        {
            throw new ArgumentException("WeightKg must be non-negative", nameof(cmd));
        }
        if (!ShippingConstants.Markets.IsValid(cmd.MarketCode))
        {
            return new QuoteShippingResult(false, null, Array.Empty<EligibleMethod>());
        }

        var zone = await zoneResolver.ResolveAsync(cmd.MarketCode, cmd.City, cmd.PostalCode, ct);
        if (zone is null)
        {
            // AC-5 — out-of-zone returns the "no methods" empty list.
            return new QuoteShippingResult(false, null, Array.Empty<EligibleMethod>());
        }

        var nowUtc = clock.GetUtcNow();

        // For each published method-version, find the single applicable
        // fee tier for this zone whose weight range covers WeightKg and
        // whose effective_at <= now. The EXCLUDE constraint guarantees
        // at-most-one match per (method_version, zone) at any instant.
        var methods = await (
            from m in db.ShippingMethods.AsNoTracking()
            where m.MarketCode == cmd.MarketCode && m.DeletedAt == null && m.CurrentVersionId != null
            join v in db.ShippingMethodVersions.AsNoTracking() on m.CurrentVersionId equals v.Id
            where v.State == MethodVersionStates.Published && v.DeletedAt == null
                && (v.EffectiveAt == null || v.EffectiveAt <= nowUtc)
            join ft in db.FeeTables.AsNoTracking() on v.Id equals ft.MethodVersionId
            where ft.ZoneId == zone.Id
                && ft.DeletedAt == null
                && ft.EffectiveAt <= nowUtc
                && ft.WeightMinKg <= cmd.WeightKg
                && ft.WeightMaxKg > cmd.WeightKg
            orderby ft.EffectiveAt descending
            select new
            {
                MethodId = m.Id,
                MethodVersionId = v.Id,
                m.NameAr,
                m.NameEn,
                v.EtaMinHours,
                v.EtaMaxHours,
                ft.FeeAmount,
                ft.Currency,
            }).ToListAsync(ct);

        // De-duplicate to one tier per method (latest effective_at wins).
        var seen = new HashSet<Guid>();
        var eligible = new List<EligibleMethod>();
        foreach (var r in methods)
        {
            if (seen.Add(r.MethodId))
            {
                eligible.Add(new EligibleMethod(
                    r.MethodId, r.MethodVersionId, r.NameAr, r.NameEn,
                    r.FeeAmount, r.Currency, r.EtaMinHours, r.EtaMaxHours));
            }
        }

        return new QuoteShippingResult(true, zone.Id, eligible);
    }
}

using BackendApi.Modules.Shipping.Domain;
using BackendApi.Modules.Shipping.Persistence;
using BackendApi.Modules.Shipping.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Shipping.Features.Methods;

public sealed record FeeTier(decimal WeightMinKg, decimal WeightMaxKg, decimal FeeAmount);

public sealed record UpdateFeeTableCommand(
    Guid MethodVersionId,
    Guid ZoneId,
    IReadOnlyList<FeeTier> Tiers,
    DateTimeOffset EffectiveAt) : IRequest<UpdateFeeTableResult>;

public sealed record UpdateFeeTableResult(int RowsCreated);

public sealed class UpdateFeeTableHandler(ShippingDbContext db, TimeProvider clock)
    : IRequestHandler<UpdateFeeTableCommand, UpdateFeeTableResult>
{
    public async Task<UpdateFeeTableResult> Handle(UpdateFeeTableCommand cmd, CancellationToken ct)
    {
        var version = await db.ShippingMethodVersions
            .FirstOrDefaultAsync(v => v.Id == cmd.MethodVersionId, ct)
            ?? throw new InvalidOperationException("Method version not found.");

        // V-7 — non-overlapping tiers + minimum starts at 0kg.
        if (cmd.Tiers.Count == 0)
        {
            throw new ArgumentException("Tiers cannot be empty.", nameof(cmd));
        }
        var sorted = cmd.Tiers.OrderBy(t => t.WeightMinKg).ToList();
        if (sorted[0].WeightMinKg != 0m)
        {
            throw new ArgumentException("First tier must start at 0kg (V-7).", nameof(cmd));
        }
        for (var i = 0; i < sorted.Count; i++)
        {
            if (sorted[i].WeightMaxKg <= sorted[i].WeightMinKg)
            {
                throw new ArgumentException($"Tier {i}: WeightMaxKg must exceed WeightMinKg.", nameof(cmd));
            }
            if (sorted[i].FeeAmount < 0)
            {
                throw new ArgumentException($"Tier {i}: FeeAmount must be non-negative.", nameof(cmd));
            }
            if (i > 0 && sorted[i].WeightMinKg < sorted[i - 1].WeightMaxKg)
            {
                throw new ArgumentException($"Tier {i}: overlaps tier {i - 1} (V-7).", nameof(cmd));
            }
        }

        var market = await db.ShippingMethods.AsNoTracking()
            .Where(m => m.Id == version.MethodId)
            .Select(m => m.MarketCode)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Owning method not found.");
        var currency = ShippingConstants.Currencies.For(market);

        // Future effective_at — AC-16 / BR-3: in-flight carts keep their snapshot.
        // We insert new rows side-by-side with any existing tiers; the EXCLUDE
        // constraint guards weight-range overlap WITHIN the same
        // (method_version_id, zone_id) where deleted_at IS NULL. New rows
        // with a future effective_at do NOT participate in quote selection
        // until the timestamp passes.
        var nowUtc = clock.GetUtcNow();
        var rows = sorted.Select(t => new FeeTable
        {
            Id = Guid.NewGuid(),
            MethodVersionId = cmd.MethodVersionId,
            ZoneId = cmd.ZoneId,
            WeightMinKg = t.WeightMinKg,
            WeightMaxKg = t.WeightMaxKg,
            FeeAmount = t.FeeAmount,
            Currency = currency,
            EffectiveAt = cmd.EffectiveAt,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        }).ToList();

        // To avoid colliding with the EXCLUDE on (method_version_id, zone_id)
        // for any currently-active rows that we want to supersede, the caller
        // is expected to soft-delete the prior rows (set DeletedAt) BEFORE
        // calling this handler — admin UI workflow. Future-effective rows
        // simply append.

        db.FeeTables.AddRange(rows);
        await db.SaveChangesAsync(ct);
        return new UpdateFeeTableResult(rows.Count);
    }
}

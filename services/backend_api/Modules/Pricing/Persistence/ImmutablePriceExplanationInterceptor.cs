using BackendApi.Modules.Pricing.Entities;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BackendApi.Modules.Pricing.Persistence;

/// <summary>
/// Enforces write-once semantics on <see cref="PriceExplanation"/>: any UPDATE (Modified state)
/// or DELETE (Deleted state) on a persisted row is rejected before SaveChangesAsync touches the DB.
/// Spec 007-a FR-012 + Principle 25: explanations are immutable once written for a quote/order.
/// Added-state rows pass through normally.
/// </summary>
public sealed class ImmutablePriceExplanationInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        GuardImmutable(eventData);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        GuardImmutable(eventData);
        return base.SavingChanges(eventData, result);
    }

    private static void GuardImmutable(DbContextEventData eventData)
    {
        if (eventData.Context is null)
        {
            return;
        }
        foreach (var entry in eventData.Context.ChangeTracker.Entries<PriceExplanation>())
        {
            if (entry.State == Microsoft.EntityFrameworkCore.EntityState.Modified
                || entry.State == Microsoft.EntityFrameworkCore.EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"pricing.explanation.immutable: attempt to {entry.State} PriceExplanation id={entry.Entity.Id} (write-once).");
            }
        }
    }
}

using BackendApi.Modules.Identity.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BackendApi.Modules.Identity.Persistence;

public sealed class IdentitySaveChangesInterceptor(IAdminMarketChangeContext marketChangeContext) : SaveChangesInterceptor
{
    private readonly IAdminMarketChangeContext _marketChangeContext = marketChangeContext;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        if (context is null)
        {
            return ValueTask.FromResult(result);
        }

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (!IsIdentityEntity(entry.Entity.GetType().Namespace))
            {
                continue;
            }

            EnforceMarketCodeImmutability(entry);

            var updatedAtProperty = entry.Metadata.FindProperty("UpdatedAt");
            if (entry.State is EntityState.Modified && updatedAtProperty is not null)
            {
                entry.CurrentValues["UpdatedAt"] = DateTimeOffset.UtcNow;
            }
        }

        return ValueTask.FromResult(result);
    }

    private static bool IsIdentityEntity(string? typeNamespace)
    {
        return typeNamespace?.StartsWith("BackendApi.Modules.Identity.Entities", StringComparison.Ordinal) == true;
    }

    private void EnforceMarketCodeImmutability(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
    {
        if (entry.Entity is not Account || entry.State != EntityState.Modified)
        {
            return;
        }

        var marketProperty = entry.Property(nameof(Account.MarketCode));
        if (!marketProperty.IsModified)
        {
            return;
        }

        var originalStatus = entry.OriginalValues[nameof(Account.Status)]?.ToString();
        var currentStatus = entry.CurrentValues[nameof(Account.Status)]?.ToString();
        var isActiveAccount =
            string.Equals(originalStatus, "active", StringComparison.OrdinalIgnoreCase)
            || string.Equals(currentStatus, "active", StringComparison.OrdinalIgnoreCase);

        if (!isActiveAccount)
        {
            return;
        }

        if (_marketChangeContext.IsActive)
        {
            return;
        }

        throw new InvalidOperationException(
            "Active account market_code is immutable unless an admin market-change context is active.");
    }
}

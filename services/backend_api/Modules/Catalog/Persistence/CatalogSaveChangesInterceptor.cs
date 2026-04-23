using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BackendApi.Modules.Catalog.Persistence;

/// <summary>
/// Stamps <c>UpdatedAt</c> on every modified catalog entity so handlers don't need to remember
/// to set it individually. Explicit audit emission stays in handlers (principle 25) — this
/// interceptor intentionally does not publish generic audit rows because per-row actor/action
/// semantics are handler-specific.
/// </summary>
public sealed class CatalogSaveChangesInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is null)
        {
            return ValueTask.FromResult(result);
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var entry in eventData.Context.ChangeTracker.Entries())
        {
            if (entry.State is not EntityState.Modified)
            {
                continue;
            }

            var updatedAt = entry.Metadata.FindProperty("UpdatedAt");
            if (updatedAt is null)
            {
                continue;
            }

            var namespaceOk = entry.Entity.GetType().Namespace?.StartsWith("BackendApi.Modules.Catalog", StringComparison.Ordinal) == true;
            if (!namespaceOk)
            {
                continue;
            }

            entry.CurrentValues["UpdatedAt"] = now;
        }

        return ValueTask.FromResult(result);
    }
}

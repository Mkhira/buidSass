using System.Data.Common;
using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BackendApi.Modules.B2B.Hooks;

/// <summary>
/// Spec 021 tasks T143–T144. Subscribes to spec 005's
/// <see cref="IProductLifecycleSubscriber"/> and, for any non-terminal-but-not-yet-accepted
/// quote (<c>requested</c> or <c>revised</c>) referencing the archived SKU, flags
/// the quote with a <c>product_archived</c> hint in <c>internal_note</c> so admin
/// operators see the warning on their next authoring pass.
///
/// <para>Customer-facing surfaces are intentionally unchanged — the operator
/// adjusts pricing or replacement during the next revision cycle. Quotes already
/// in <c>pending-approver</c> are left alone (the pricing was already locked at
/// publish time).</para>
///
/// <para>Idempotent: re-delivery is a no-op when the existing
/// <c>internal_note</c> already references the archived SKU.</para>
/// </summary>
public sealed class ProductArchivedHandler(
    B2BDbContext db,
    TimeProvider clock,
    ILogger<ProductArchivedHandler> logger) : IProductLifecycleSubscriber
{
    public async Task OnProductArchivedAsync(ProductArchived evt, CancellationToken ct)
    {
        var nowUtc = clock.GetUtcNow();
        var sku = evt.Sku;

        // The SKU appears in either the originating cart snapshot (requested
        // quotes never published) or the current version's line items (revised
        // quotes). Both columns are jsonb on the storage side, so we cast to
        // text in the WHERE clause for substring matching. The SKU pattern is
        // wrapped with quotes to avoid matching SKUs that happen to be
        // substrings of other SKUs — snapshot rows store SKUs as `"sku"` tokens.
        var pattern = "%\"" + sku.Replace("%", "\\%").Replace("_", "\\_") + "\"%";
        var hint = $"product_archived:{sku}";

        // Resolve matching quote IDs via raw SQL (jsonb columns must be cast to
        // text for ILIKE — EF.Functions.ILike directly on jsonb fails with
        // `pg_catalog.like_escape(jsonb, unknown) does not exist`). We collect
        // ids first, then reload through the regular tracked LINQ surface so
        // EF brings xmin / row-version columns along for SaveChanges.
        var matchingIds = new HashSet<Guid>();

        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using (DbCommand cmd = connection.CreateCommand())
        {
            // EF Core's default convention leaves the primary-key column quoted
            // ("Id" — case-preserving) while every explicit `HasColumnName` is
            // snake_case. Match that mix verbatim in the SQL.
            cmd.CommandText = @"
                SELECT q.""Id"" FROM b2b.quotes q
                WHERE q.state IN ('requested','revised')
                  AND q.originating_cart_snapshot::text ILIKE @p
                UNION
                SELECT q.""Id"" FROM b2b.quotes q
                JOIN b2b.quote_versions v
                  ON v.""Id"" = q.current_version_id
                WHERE q.state IN ('requested','revised')
                  AND v.line_items::text ILIKE @p";
            cmd.Parameters.Add(new NpgsqlParameter("p", pattern));
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                matchingIds.Add(reader.GetGuid(0));
            }
        }

        if (matchingIds.Count == 0)
        {
            return;
        }

        var quotes = await db.Quotes
            .Where(q => matchingIds.Contains(q.Id))
            .ToListAsync(ct);

        if (quotes.Count == 0)
        {
            return;
        }

        foreach (var quote in quotes)
        {
            var existing = quote.InternalNote ?? string.Empty;
            if (existing.Contains(hint, StringComparison.Ordinal))
            {
                continue;
            }

            var prefix = existing.Length == 0 ? string.Empty : existing.TrimEnd() + "\n";
            quote.InternalNote = prefix + hint + " (archived at " + nowUtc.UtcDateTime.ToString("u") + ")";
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "ProductArchivedHandler failed to persist internal-note flags for SKU {Sku}; will be retried on redelivery.",
                sku);
            throw;
        }
    }
}

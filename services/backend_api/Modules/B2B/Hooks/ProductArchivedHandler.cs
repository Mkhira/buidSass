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
        var marketCode = evt.MarketCode;

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
                  AND q.market_code = @market
                  AND q.originating_cart_snapshot::text ILIKE @p
                UNION
                SELECT q.""Id"" FROM b2b.quotes q
                JOIN b2b.quote_versions v
                  ON v.""Id"" = q.current_version_id
                  AND v.market_code = @market
                WHERE q.state IN ('requested','revised')
                  AND q.market_code = @market
                  AND v.line_items::text ILIKE @p";
            cmd.Parameters.Add(new NpgsqlParameter("p", pattern));
            cmd.Parameters.Add(new NpgsqlParameter("market", marketCode));
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

        // Re-apply the requested/revised filter and market_code partition on the
        // tracked reload — between the raw-SQL id resolution above and
        // SaveChanges below, a concurrent operator action could have advanced
        // one of these quotes to `pending-approver` or a terminal state, in
        // which case appending a product_archived hint is no longer correct.
        // Market scoping (ADR-010) is reasserted defensively so a same-SKU row
        // in another market is never touched by an event for this market.
        var quotes = await db.Quotes
            .Where(q => matchingIds.Contains(q.Id)
                     && q.MarketCode == marketCode
                     && (q.State == "requested" || q.State == "revised"))
            .ToListAsync(ct);

        if (quotes.Count == 0)
        {
            return;
        }

        // Reload the current versions for tracked quotes so we can revalidate
        // that the archived SKU still appears on the live payload. Between the
        // raw-SQL id scan and this loop, an admin can publish a new revision
        // that swaps in a replacement SKU; the quote stays `revised`, but no
        // longer references `evt.Sku` and shouldn't be flagged.
        var currentVersionIds = quotes
            .Select(q => q.CurrentVersionId)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToList();
        var currentVersions = currentVersionIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.QuoteVersions
                .AsNoTracking()
                .Where(v => currentVersionIds.Contains(v.Id) && v.MarketCode == marketCode)
                .ToDictionaryAsync(v => v.Id, v => v.LineItemsJson, ct);

        // SKU appears in JSON payloads as a `"<sku>"` token (snapshot rows store
        // SKUs as quoted strings). The SQL ILIKE pattern wraps with `\"…\"`, so
        // the in-memory check uses the same delimiters to stay consistent.
        var skuToken = "\"" + sku + "\"";

        // Idempotency-token boundary: persisted hints always include the
        // " (archived at ..." suffix, so probing for `hint + " ("` avoids a
        // false-positive when a longer SKU starts with the same prefix
        // (e.g. `product_archived:AB` would otherwise match `product_archived:ABC`).
        var hintProbe = hint + " (";
        foreach (var quote in quotes)
        {
            var cartHasSku = (quote.OriginatingCartSnapshotJson ?? string.Empty)
                .Contains(skuToken, StringComparison.Ordinal);
            var versionHasSku = quote.CurrentVersionId is { } versionId
                && currentVersions.TryGetValue(versionId, out var lineItemsJson)
                && lineItemsJson.Contains(skuToken, StringComparison.Ordinal);
            if (!cartHasSku && !versionHasSku)
            {
                // A new revision dropped the archived SKU after the raw-SQL
                // scan — nothing to flag on this quote anymore.
                continue;
            }

            var existing = quote.InternalNote ?? string.Empty;
            if (existing.Contains(hintProbe, StringComparison.Ordinal))
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

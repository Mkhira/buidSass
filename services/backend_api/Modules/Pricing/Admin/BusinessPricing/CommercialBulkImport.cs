using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackendApi.Modules.Pricing.Admin.Common;
using BackendApi.Modules.Pricing.Authorization;
using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Primitives.Commercial;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Pricing.Admin.BusinessPricing;

/// <summary>
/// Spec 007-b business-pricing bulk-import preview/commit (contract §4.3 / §4.4).
/// Strict snake_case CSV header. Preview persists a transient payload server-side
/// keyed by an opaque token (15-min TTL); commit replays it under fresh-snapshot
/// validation so concurrent edits are detected.
/// </summary>
public static class CommercialBulkImport
{
    private const string ActorRole = CommercialPermissions.B2BAuthoring;
    private static readonly string[] RequiredHeaders = ["tier_code", "sku", "net_minor", "markets"];
    private const int PreviewTtlMinutes = 15;
    private const int MaxCsvRowCount = 20_000;
    private const long MaxFileSizeBytes = 25 * 1024 * 1024; // 25 MiB

    // In-memory preview store. Per-instance is sufficient for V1 (token TTL is 15 min
    // and Admin web is single-instance at launch); a Redis-backed store can replace
    // this once spec 015 admin-web reaches multi-node.
    private static readonly Dictionary<string, PreviewPayload> Payloads = new();
    private static readonly Lock PayloadsLock = new();

    private sealed record PreviewPayload(
        Guid ActorId,
        DateTimeOffset CreatedAtUtc,
        long SnapshotFingerprint,
        IReadOnlyList<BulkImportPreviewRow> WouldInsert,
        IReadOnlyList<BulkImportPreviewRow> WouldUpdate,
        IReadOnlyList<BulkImportPreviewRow> WouldSkip);

    public static async Task<IResult> PreviewAsync(
        HttpContext context,
        PricingDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        if (!context.Request.HasFormContentType)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.BusinessPricingCsvHeaderInvalid,
                "multipart/form-data with a `file` field is required.");
        }

        IFormFile? file = null;
        try
        {
            var form = await context.Request.ReadFormAsync(ct);
            file = form.Files["file"];
        }
        catch (Exception ex) when (
            ex is InvalidDataException                            // malformed multipart boundary
            or InvalidOperationException                          // wrong content type / form not allowed
            or System.IO.IOException                              // upstream stream truncated
            or Microsoft.AspNetCore.Http.BadHttpRequestException) // body-size / line-length limits
        {
            // CodeRabbit PR #80 round 1 nit: narrow the catch to the
            // form-parsing failure shapes. Cancellation + other unexpected
            // errors propagate so they surface in logs and metrics instead of
            // silently degrading into "empty file".
            _ = ex;
        }
        if (file is null || file.Length == 0)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.BusinessPricingCsvRowInvalid,
                "Empty or missing `file` upload.");
        }
        if (file.Length > MaxFileSizeBytes)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.BusinessPricingCsvRowInvalid,
                $"File exceeds {MaxFileSizeBytes / (1024 * 1024)} MiB.");
        }

        // Parse CSV (strict header, snake_case, comma-separated, UTF-8).
        var parseResult = await ParseCsvAsync(file, ct);
        if (parseResult.HeaderError is not null) return parseResult.HeaderError(context);

        // Resolve tier_code -> tier_id once; rows referencing unknown tier_codes
        // are rejected. Lowering happens client-side because the EF Core 9 Npgsql
        // provider does not translate `Slug.ToLowerInvariant()` in dictionary
        // key selectors. The tier table is tiny (single-digit row count at V1).
        var tierLookup = (await db.B2BTiers.AsNoTracking()
            .Select(t => new { t.Id, t.Slug })
            .ToListAsync(ct))
            .ToDictionary(t => t.Slug.ToLowerInvariant(), t => t.Id);

        // Build a representative snapshot fingerprint over rows that could be
        // affected by this import. Concurrent edits flip the fingerprint and
        // the commit step rejects the token.
        var snapshotFingerprint = await ComputeSnapshotFingerprintAsync(db, parseResult.Rows, tierLookup, ct);

        var wouldInsert = new List<BulkImportPreviewRow>();
        var wouldUpdate = new List<BulkImportPreviewRow>();
        var wouldSkip = new List<BulkImportPreviewRow>();
        var rejected = new List<BulkImportRejectedRow>(parseResult.Rejected);

        foreach (var row in parseResult.Rows)
        {
            if (!tierLookup.TryGetValue(row.TierCode.ToLowerInvariant(), out var tierId))
            {
                rejected.Add(new BulkImportRejectedRow(row.RowNumber,
                    $"Unknown tier_code '{row.TierCode}'.",
                    CommercialReasonCode.BusinessPricingCsvRowInvalid));
                continue;
            }

            // Each market produces an independent (tier, market, product) write.
            foreach (var rawMarket in row.Markets)
            {
                var market = rawMarket.Trim().ToLowerInvariant();
                if (market is not ("sa" or "eg"))
                {
                    rejected.Add(new BulkImportRejectedRow(row.RowNumber,
                        $"Unknown market '{rawMarket}'.",
                        CommercialReasonCode.BusinessPricingCsvRowInvalid));
                    continue;
                }

                var existing = await db.ProductTierPrices.AsNoTracking()
                    .FirstOrDefaultAsync(p =>
                        p.TierId == tierId
                        && p.CompanyId == null
                        && p.MarketCode == market
                        && p.ProductId == row.ProductId
                        && p.State == BusinessPricingState.Active, ct);

                var preview = new BulkImportPreviewRow(
                    row.RowNumber, row.TierCode, row.ProductId, row.NetMinor,
                    Markets: [market],
                    Outcome: existing is null ? "insert" : existing.NetMinor == row.NetMinor ? "skip" : "update");

                switch (preview.Outcome)
                {
                    case "insert": wouldInsert.Add(preview); break;
                    case "update": wouldUpdate.Add(preview); break;
                    default: wouldSkip.Add(preview); break;
                }
            }
        }

        var actorId = AdminCommercialResponseFactory.ResolveActorAccountId(context);
        var token = GenerateToken();
        var nowUtc = time.GetUtcNow();

        var payload = new PreviewPayload(
            actorId, nowUtc, snapshotFingerprint,
            wouldInsert.AsReadOnly(),
            wouldUpdate.AsReadOnly(),
            wouldSkip.AsReadOnly());

        lock (PayloadsLock)
        {
            // Garbage-collect expired tokens lazily on every preview write.
            PurgeExpiredLocked(nowUtc);
            Payloads[token] = payload;
        }

        return Results.Ok(new BulkImportPreviewResponse(
            PreviewToken: token,
            PreviewTokenExpiresAt: nowUtc.AddMinutes(PreviewTtlMinutes),
            WouldInsert: wouldInsert,
            WouldUpdate: wouldUpdate,
            WouldSkip: wouldSkip,
            Rejected: rejected,
            SnapshotFingerprint: snapshotFingerprint));
    }

    public static async Task<IResult> CommitAsync(
        [FromBody] BulkImportCommitRequest request,
        HttpContext context,
        PricingDbContext db,
        CommercialAuditWriter audit,
        TimeProvider time,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.PreviewToken))
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.BusinessPricingPreviewTokenExpired,
                "preview_token is required.");
        }

        var nowUtc = time.GetUtcNow();
        PreviewPayload? payload;
        lock (PayloadsLock)
        {
            PurgeExpiredLocked(nowUtc);
            Payloads.TryGetValue(request.PreviewToken, out payload);
        }
        if (payload is null)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.BusinessPricingPreviewTokenExpired,
                "Preview token expired or unknown.");
        }

        var actorId = AdminCommercialResponseFactory.ResolveActorAccountId(context);
        // Authorship guard: only the original previewer may commit. Mismatches
        // are reported as token-expired (same wire code) to avoid leaking
        // cross-actor token presence.
        if (payload.ActorId != actorId && actorId != Guid.Empty)
        {
            return AdminCommercialResponseFactory.Problem(context, 400,
                CommercialReasonCode.BusinessPricingPreviewTokenExpired,
                "Preview token does not belong to caller.");
        }

        // Snapshot-changed check. Re-resolve the fingerprint over the rows
        // referenced by the payload's preview body; if it doesn't match,
        // surface a fresh preview to the caller and require re-preview.
        var freshFingerprint = await RecomputeFingerprintFromPayloadAsync(db, payload, ct);
        if (freshFingerprint != payload.SnapshotFingerprint)
        {
            return AdminCommercialResponseFactory.Problem(context, 409,
                CommercialReasonCode.BusinessPricingPreviewSnapshotChanged,
                "Underlying rows changed since preview; please re-preview before committing.",
                extensions: new Dictionary<string, object?>
                {
                    ["fresh_snapshot_fingerprint"] = freshFingerprint,
                });
        }

        var inserted = 0;
        var updated = 0;
        var skipped = payload.WouldSkip.Count;

        // Apply all inserts + updates atomically. Re-resolve each tier_code to
        // its id (caller may have multiple tier_codes across the import).
        // See preview path for the client-side lowering rationale.
        var tierByCode = (await db.B2BTiers.AsNoTracking()
            .Select(t => new { t.Id, t.Slug })
            .ToListAsync(ct))
            .ToDictionary(t => t.Slug.ToLowerInvariant(), t => t.Id);

        foreach (var row in payload.WouldInsert)
        {
            if (!tierByCode.TryGetValue(row.TierCode.ToLowerInvariant(), out var tierId)) continue;
            foreach (var rawMarket in row.Markets)
            {
                var market = rawMarket.Trim().ToLowerInvariant();
                db.ProductTierPrices.Add(new ProductTierPrice
                {
                    Id = Guid.NewGuid(),
                    ProductId = row.ProductId,
                    TierId = tierId,
                    CompanyId = null,
                    MarketCode = market,
                    NetMinor = row.NetMinor,
                    State = BusinessPricingState.Active,
                    StateChangedAtUtc = nowUtc,
                    StateChangedByActorId = actorId,
                    CreatedAt = nowUtc,
                    UpdatedAt = nowUtc,
                });
                inserted++;
            }
        }

        foreach (var row in payload.WouldUpdate)
        {
            if (!tierByCode.TryGetValue(row.TierCode.ToLowerInvariant(), out var tierId)) continue;
            foreach (var rawMarket in row.Markets)
            {
                var market = rawMarket.Trim().ToLowerInvariant();
                var existing = await db.ProductTierPrices.FirstOrDefaultAsync(p =>
                    p.TierId == tierId
                    && p.CompanyId == null
                    && p.MarketCode == market
                    && p.ProductId == row.ProductId
                    && p.State == BusinessPricingState.Active, ct);
                if (existing is null) continue;
                existing.NetMinor = row.NetMinor;
                existing.UpdatedAt = nowUtc;
                updated++;
            }
        }

        // Bulk-import audit row carries the actor as the target id because the
        // operation spans many rows; the per-row commercial_audit_events entries
        // are added inside the engine on next read. We use a fresh GUID so the
        // platform audit channel has a unique entity_id (it rejects Guid.Empty).
        var bulkAuditId = Guid.NewGuid();
        var publishAudit = audit.StageLocal(
            "business_pricing", bulkAuditId, "business_pricing.bulk_imported",
            actorId, ActorRole,
            before: null,
            after: new { inserted, updated, skipped },
            diff: null,
            reasonNote: null,
            correlationId: null,
            nowUtc);

        await db.SaveChangesAsync(ct);
        await publishAudit(ct);

        // Single-use token: invalidate after a successful commit.
        lock (PayloadsLock)
        {
            Payloads.Remove(request.PreviewToken);
        }

        return Results.Ok(new BulkImportCommitResponse(inserted, updated, skipped, nowUtc));
    }

    // ----------------------- internal helpers -----------------------

    private sealed record CsvParseResult(
        IReadOnlyList<CsvRow> Rows,
        IReadOnlyList<BulkImportRejectedRow> Rejected,
        Func<HttpContext, IResult>? HeaderError);

    private sealed record CsvRow(int RowNumber, string TierCode, Guid ProductId, long NetMinor, string[] Markets);

    private static async Task<CsvParseResult> ParseCsvAsync(IFormFile file, CancellationToken ct)
    {
        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
        var headerLine = await reader.ReadLineAsync(ct);
        if (headerLine is null)
        {
            return new CsvParseResult([], [],
                ctx => AdminCommercialResponseFactory.Problem(ctx, 400,
                    CommercialReasonCode.BusinessPricingCsvHeaderInvalid,
                    "CSV is empty."));
        }

        // STRICT header: must equal "tier_code,sku,net_minor,markets" (snake_case).
        // Mixed-case / Title_Case headers (Net_Minor etc.) are rejected per research §R7.
        var headers = headerLine.Split(',', StringSplitOptions.TrimEntries);
        if (headers.Length != RequiredHeaders.Length
            || !headers.Zip(RequiredHeaders, (a, b) => a == b).All(eq => eq))
        {
            return new CsvParseResult([], [],
                ctx => AdminCommercialResponseFactory.Problem(ctx, 400,
                    CommercialReasonCode.BusinessPricingCsvHeaderInvalid,
                    $"CSV header must be exactly: {string.Join(",", RequiredHeaders)} (snake_case)."));
        }

        var rows = new List<CsvRow>();
        var rejected = new List<BulkImportRejectedRow>();
        var rowNumber = 1; // header is row 1

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            rowNumber++;
            if (rows.Count >= MaxCsvRowCount)
            {
                rejected.Add(new BulkImportRejectedRow(rowNumber,
                    $"Row limit ({MaxCsvRowCount}) exceeded; remaining rows skipped.",
                    CommercialReasonCode.BusinessPricingCsvRowInvalid));
                break;
            }
            if (string.IsNullOrWhiteSpace(line)) continue;

            var cells = line.Split(',', StringSplitOptions.TrimEntries);
            if (cells.Length != RequiredHeaders.Length)
            {
                rejected.Add(new BulkImportRejectedRow(rowNumber,
                    $"Expected {RequiredHeaders.Length} columns, got {cells.Length}.",
                    CommercialReasonCode.BusinessPricingCsvRowInvalid));
                continue;
            }

            if (!Guid.TryParse(cells[1], out var productId))
            {
                rejected.Add(new BulkImportRejectedRow(rowNumber,
                    $"sku '{cells[1]}' is not a valid product id.",
                    CommercialReasonCode.BusinessPricingCsvRowInvalid));
                continue;
            }
            if (!long.TryParse(cells[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var netMinor)
                || netMinor < 0)
            {
                rejected.Add(new BulkImportRejectedRow(rowNumber,
                    $"net_minor '{cells[2]}' is not a non-negative integer.",
                    CommercialReasonCode.BusinessPricingCsvRowInvalid));
                continue;
            }

            // markets is a `|`-delimited list inside the cell (avoids needing CSV quoting).
            var marketList = cells[3].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (marketList.Length == 0)
            {
                rejected.Add(new BulkImportRejectedRow(rowNumber,
                    "markets column is empty.",
                    CommercialReasonCode.BusinessPricingCsvRowInvalid));
                continue;
            }

            rows.Add(new CsvRow(rowNumber, cells[0], productId, netMinor, marketList));
        }

        return new CsvParseResult(rows, rejected, HeaderError: null);
    }

    private static async Task<long> ComputeSnapshotFingerprintAsync(
        PricingDbContext db,
        IReadOnlyList<CsvRow> rows,
        IReadOnlyDictionary<string, Guid> tierLookup,
        CancellationToken ct)
    {
        if (rows.Count == 0) return 0L;

        var tierIds = rows.Select(r => r.TierCode.ToLowerInvariant())
            .Where(tierLookup.ContainsKey)
            .Select(tc => tierLookup[tc])
            .Distinct()
            .ToArray();
        var productIds = rows.Select(r => r.ProductId).Distinct().ToArray();

        var snapshot = await db.ProductTierPrices.AsNoTracking()
            .Where(p => p.CompanyId == null
                && tierIds.Contains(p.TierId!.Value)
                && productIds.Contains(p.ProductId)
                && p.State == BusinessPricingState.Active)
            .Select(p => new { p.Id, p.NetMinor, p.UpdatedAt, p.XminRowVersion })
            .ToListAsync(ct);

        return ComputeFingerprint(snapshot.Select(s => $"{s.Id:N}:{s.NetMinor}:{s.UpdatedAt.UtcTicks}:{s.XminRowVersion}"));
    }

    private static async Task<long> RecomputeFingerprintFromPayloadAsync(
        PricingDbContext db,
        PreviewPayload payload,
        CancellationToken ct)
    {
        var tierCodes = payload.WouldInsert.Concat(payload.WouldUpdate).Concat(payload.WouldSkip)
            .Select(r => r.TierCode.ToLowerInvariant()).Distinct().ToArray();
        var productIds = payload.WouldInsert.Concat(payload.WouldUpdate).Concat(payload.WouldSkip)
            .Select(r => r.ProductId).Distinct().ToArray();

        // Fetch every tier and normalise on the client side. The fetched set
        // is small (one row per active tier), and pushing
        // `Slug.ToLowerInvariant()` to Postgres requires `LOWER()` translation
        // which the EF Core 9 Npgsql provider does not surface for ToDictionary's
        // key selector.
        var allTiers = await db.B2BTiers.AsNoTracking()
            .Select(t => new { t.Id, t.Slug })
            .ToListAsync(ct);
        var tierLookup = allTiers
            .Where(t => tierCodes.Contains(t.Slug.ToLowerInvariant()))
            .ToDictionary(t => t.Slug.ToLowerInvariant(), t => t.Id);
        var tierIds = tierLookup.Values.ToArray();

        var snapshot = await db.ProductTierPrices.AsNoTracking()
            .Where(p => p.CompanyId == null
                && tierIds.Contains(p.TierId!.Value)
                && productIds.Contains(p.ProductId)
                && p.State == BusinessPricingState.Active)
            .Select(p => new { p.Id, p.NetMinor, p.UpdatedAt, p.XminRowVersion })
            .ToListAsync(ct);

        return ComputeFingerprint(snapshot.Select(s => $"{s.Id:N}:{s.NetMinor}:{s.UpdatedAt.UtcTicks}:{s.XminRowVersion}"));
    }

    private static long ComputeFingerprint(IEnumerable<string> parts)
    {
        // Deterministic + order-invariant fingerprint via sorted-XOR of FNV-1a hashes.
        // FNV offset basis (14695981039346656037) exceeds long.MaxValue; the hash
        // accumulator MUST be ulong so the literal fits without overflow. The
        // returned long is the unchecked reinterpretation of the final ulong.
        ulong acc = 0UL;
        foreach (var p in parts.OrderBy(x => x, StringComparer.Ordinal))
        {
            unchecked
            {
                ulong h = 14695981039346656037UL;  // FNV offset basis (64-bit)
                foreach (var c in p)
                {
                    h ^= c;
                    h *= 1099511628211UL;          // FNV prime (64-bit)
                }
                acc ^= h;
            }
        }
        return unchecked((long)acc);
    }

    private static string GenerateToken()
    {
        Span<byte> buf = stackalloc byte[24];
        RandomNumberGenerator.Fill(buf);
        return Convert.ToHexString(buf);
    }

    private static void PurgeExpiredLocked(DateTimeOffset nowUtc)
    {
        var cutoff = nowUtc.AddMinutes(-PreviewTtlMinutes);
        var expired = Payloads.Where(kv => kv.Value.CreatedAtUtc < cutoff).Select(kv => kv.Key).ToArray();
        foreach (var k in expired) Payloads.Remove(k);
    }
}

using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.B2B.Entities;
using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.B2B.Primitives;
using BackendApi.Modules.Shared;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BackendApi.Modules.B2B.Quotes.Admin.AuthorQuoteDraft;

/// <summary>
/// Spec 021 T087 — author-draft handler. State transitions
/// <c>requested|revised → drafted</c>. Persists the draft body in
/// <c>Quote.DraftBodyJson</c> for the publish slice (T088) to materialize into a
/// <see cref="QuoteVersion"/>.
///
/// Below-baseline checks: handler calls
/// <see cref="IPricingBaselineProvider.GetBaselinesAsync"/> for the line SKUs and
/// rejects with <c>400 quote.below_baseline_reason_required</c> if any line has
/// <c>override_unit_price &lt; baseline</c> AND no override reason. (The validator
/// enforces the shape — handler enforces the "below" comparison.)
/// </summary>
public sealed class AuthorQuoteDraftHandler
{
    private readonly B2BDbContext _db;
    private readonly IPricingBaselineProvider _baselineProvider;
    private readonly IAuditEventPublisher _auditPublisher;
    private readonly TimeProvider _time;

    public AuthorQuoteDraftHandler(
        B2BDbContext db,
        IPricingBaselineProvider baselineProvider,
        IAuditEventPublisher auditPublisher,
        TimeProvider time)
    {
        _db = db;
        _baselineProvider = baselineProvider;
        _auditPublisher = auditPublisher;
        _time = time;
    }

    public async Task<AuthorQuoteDraftResult> HandleAsync(
        Guid quoteId,
        Guid actorId,
        AuthorQuoteDraftRequest body,
        CancellationToken ct)
    {
        var quote = await _db.Quotes.FirstOrDefaultAsync(q => q.Id == quoteId, ct);
        if (quote is null) return AuthorQuoteDraftResult.NotFound();

        if (!QuoteStateExtensions.TryParseToken(quote.State, out var current)) return AuthorQuoteDraftResult.InvalidState();
        if (current != QuoteState.Requested && current != QuoteState.Revised)
        {
            return AuthorQuoteDraftResult.InvalidState();
        }

        // Below-baseline gate via pricing engine.
        var skus = (body.Lines ?? Array.Empty<AuthorQuoteDraftLine>())
            .Where(l => !string.IsNullOrWhiteSpace(l.Sku))
            .Select(l => l.Sku!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var baselines = skus.Count == 0
            ? new Dictionary<string, PricingBaseline>()
            : (await _baselineProvider.GetBaselinesAsync(quote.CustomerId, skus, ct))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        foreach (var line in body.Lines ?? Array.Empty<AuthorQuoteDraftLine>())
        {
            if (string.IsNullOrWhiteSpace(line.Sku) || line.OverrideUnitPrice is null) continue;
            if (baselines.TryGetValue(line.Sku, out var baseline)
                && line.OverrideUnitPrice.Value < baseline.BaselineUnitPrice)
            {
                if (line.OverrideReason is null
                    || (string.IsNullOrWhiteSpace(line.OverrideReason.En)
                        && string.IsNullOrWhiteSpace(line.OverrideReason.Ar)))
                {
                    return AuthorQuoteDraftResult.BelowBaselineReasonRequired();
                }
            }
        }

        var nowUtc = _time.GetUtcNow();
        var priorState = quote.State;

        // Compute next version_number and write a new QuoteVersion as the draft.
        // QuoteVersion rows are row-immutable (data-model §2.6); each author-draft
        // call mints a fresh row. Concurrent drafts on the same quote race on the
        // unique (quote_id, version_number, market_code) index — the loser maps
        // its DbUpdateException to 409 invalid_state_for_action.
        var nextVersion = (await _db.QuoteVersions
            .Where(v => v.QuoteId == quote.Id)
            .Select(v => (int?)v.VersionNumber)
            .MaxAsync(ct) ?? 0) + 1;

        decimal subtotal = 0m, totalDiscount = 0m, totalTaxPreview = 0m;
        var lineItemsForJson = new List<object>();
        var currency = "SAR";
        foreach (var line in body.Lines ?? Array.Empty<AuthorQuoteDraftLine>())
        {
            if (string.IsNullOrWhiteSpace(line.Sku)) continue;
            var baseline = baselines.TryGetValue(line.Sku, out var b) ? b : null;
            var baselineUnit = baseline?.BaselineUnitPrice ?? 0m;
            var unitPrice = line.OverrideUnitPrice ?? baselineUnit;
            var qty = line.Quantity ?? 1;
            var lineDiscount = line.LineDiscountAmount ?? 0m;
            var lineTaxPreview = (baseline?.TaxPreviewUnitAmount ?? 0m) * qty;
            subtotal += unitPrice * qty;
            totalDiscount += lineDiscount;
            totalTaxPreview += lineTaxPreview;
            currency = baseline?.Currency ?? currency;
            lineItemsForJson.Add(new
            {
                sku = line.Sku,
                qty,
                baseline_unit_price = baselineUnit,
                override_unit_price = line.OverrideUnitPrice,
                override_reason = line.OverrideReason,
                line_discount_amount = lineDiscount,
                line_tax_preview = lineTaxPreview,
                currency,
            });
        }
        var grandTotal = subtotal - totalDiscount + totalTaxPreview;

        var versionId = Guid.NewGuid();
        var version = new QuoteVersion
        {
            Id = versionId,
            QuoteId = quote.Id,
            MarketCode = quote.MarketCode,
            VersionNumber = nextVersion,
            AuthoredBy = actorId,
            PublishedAt = nowUtc,
            LineItemsJson = JsonSerializer.Serialize(lineItemsForJson),
            TermsTextJson = JsonSerializer.Serialize(new
            {
                en = body.TermsText?.En ?? "",
                ar = body.TermsText?.Ar ?? "",
            }),
            TermsDays = body.TermsDays ?? 0,
            ValidityExtends = body.ValidityExtends ?? false,
            TotalsSummaryJson = JsonSerializer.Serialize(new
            {
                subtotal,
                total_discount = totalDiscount,
                total_tax_preview = totalTaxPreview,
                grand_total = grandTotal,
                currency,
            }),
            CustomerRevisionCommentJson = null,
        };
        _db.QuoteVersions.Add(version);

        // The DraftBodyJson now also carries the internal_note + the version id so
        // publish (T088) can pick up the latest draft version efficiently.
        quote.DraftBodyJson = JsonSerializer.Serialize(new
        {
            draft_version_id = versionId,
            internal_note = body.InternalNote,
            validity_extends = body.ValidityExtends ?? false,
            authored_by = actorId,
            authored_at = nowUtc,
        });
        if (!string.IsNullOrWhiteSpace(body.InternalNote))
        {
            quote.InternalNote = body.InternalNote;
        }
        quote.State = QuoteState.Drafted.ToToken();

        var transition = new QuoteStateTransition
        {
            Id = Guid.NewGuid(),
            QuoteId = quote.Id,
            MarketCode = quote.MarketCode,
            PriorState = priorState,
            NewState = quote.State,
            ActorKind = QuoteActorKind.AdminOperator.ToToken(),
            ActorId = actorId,
            ReasonJson = null,
            MetadataJson = JsonSerializer.Serialize(new
            {
                source = "author_draft",
                version_number = nextVersion,
                version_id = versionId,
            }),
            OccurredAt = nowUtc,
        };
        _db.QuoteStateTransitions.Add(transition);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AuthorQuoteDraftResult.InvalidState();
        }
        catch (DbUpdateException ex) when (AuthorQuoteDraftHelpers.IsUniqueViolation(ex))
        {
            // Concurrent author-draft race lost the (quote_id, version_number) check.
            return AuthorQuoteDraftResult.InvalidState();
        }

        // Audit each below-baseline override per FR-040 / SC-004.
        foreach (var line in body.Lines ?? Array.Empty<AuthorQuoteDraftLine>())
        {
            if (string.IsNullOrWhiteSpace(line.Sku) || line.OverrideUnitPrice is null) continue;
            if (baselines.TryGetValue(line.Sku, out var baseline)
                && line.OverrideUnitPrice.Value < baseline.BaselineUnitPrice)
            {
                try
                {
                    await _auditPublisher.PublishAsync(new AuditEvent(
                        ActorId: actorId,
                        ActorRole: "admin_operator",
                        Action: "quote.line_override",
                        EntityType: "quote",
                        EntityId: quote.Id,
                        BeforeState: new { sku = line.Sku, baseline_unit_price = baseline.BaselineUnitPrice },
                        AfterState: new
                        {
                            sku = line.Sku,
                            override_unit_price = line.OverrideUnitPrice,
                            baseline_unit_price = baseline.BaselineUnitPrice,
                            override_reason = line.OverrideReason,
                        },
                        Reason: "below_baseline_override"), ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { /* audit replay path via outbox */ }
            }
        }

        try
        {
            await _auditPublisher.PublishAsync(new AuditEvent(
                ActorId: actorId,
                ActorRole: "admin_operator",
                Action: "quote.state_changed",
                EntityType: "quote",
                EntityId: quote.Id,
                BeforeState: new { state = priorState },
                AfterState: new { state = quote.State },
                Reason: "author_draft"), ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { }

        return AuthorQuoteDraftResult.Success(new AuthorQuoteDraftResponse(quote.Id, quote.State));
    }
}

file static class AuthorQuoteDraftHelpers
{
    public static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation;
}

public sealed record AuthorQuoteDraftResult(
    bool IsSuccess,
    int StatusCode,
    QuoteReasonCode? ReasonCode,
    AuthorQuoteDraftResponse? Response)
{
    public static AuthorQuoteDraftResult Success(AuthorQuoteDraftResponse r) => new(true, 200, null, r);
    public static AuthorQuoteDraftResult NotFound() => new(false, 404, QuoteReasonCode.QuoteNotFound, null);
    public static AuthorQuoteDraftResult InvalidState() => new(false, 409, QuoteReasonCode.QuoteInvalidStateForAction, null);
    public static AuthorQuoteDraftResult BelowBaselineReasonRequired() =>
        new(false, 400, QuoteReasonCode.QuoteBelowBaselineReasonRequired, null);
}

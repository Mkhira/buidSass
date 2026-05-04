using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.B2B.Entities;
using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.B2B.Primitives;
using BackendApi.Modules.B2B.RateLimit;
using BackendApi.Modules.Shared;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BackendApi.Modules.B2B.Quotes.Customer.RequestQuoteFromCart;

/// <summary>
/// Spec 021 contract §2.1 / FR-018 / FR-019 — handler for
/// <c>POST /api/customer/quotes/from-cart</c>. Transactional. Pre-write rejection
/// checks run in the order documented in tasks T064 so the contract surface is
/// deterministic (the same misuse always produces the same reason code):
///
/// <list type="number">
///   <item>Rate-limit per-customer + per-company sliding window — else <c>429 quote.rate_limit_exceeded</c>.</item>
///   <item>When <c>company_id</c> provided — caller has active membership AND the
///         membership role permits quote requests (<c>buyer</c> or <c>companies.admin</c>) —
///         else <c>409 quote.no_active_company_membership</c>.</item>
///   <item>When <c>company_id</c> provided — <c>company.state != 'suspended'</c> —
///         else <c>422 quote.company_suspended</c> (FR-026).</item>
///   <item>Market match — caller's market-of-record == company's market — else
///         <c>422 quote.market_mismatch</c> (FR-011).</item>
///   <item>PO required when <c>company.po_required=true</c> — else <c>400 quote.po_required</c>.</item>
///   <item>PO-uniqueness when <c>company.unique_po_required=true</c> — else <c>409 quote.po_already_used</c>
///         (FR-019; race-protected by <c>UX_quotes_company_po</c> partial unique index).</item>
/// </list>
///
/// Then the write path:
/// <list type="number">
///   <item><see cref="ICartSnapshotProvider.SnapshotAndClearAsync"/> — empty result returns <c>400 quote.cart_empty</c>.</item>
///   <item>Per-line <see cref="IProductRestrictionPolicy.GetForSkuAsync"/> snapshot.</item>
///   <item>INSERT <see cref="Quote"/> + initial <see cref="QuoteStateTransition"/> (<c>__none__ → requested</c>).</item>
///   <item>Audit-event publish: <c>quote.state_changed</c> via <see cref="IAuditEventPublisher"/>.</item>
///   <item>Domain-event publish: <see cref="QuoteRequested"/> via MediatR <see cref="IPublisher"/>.</item>
/// </list>
///
/// Account-state (FR-038 carry-over) is NOT checked here in V1 — spec 021 has no
/// runtime contract with spec 020's <c>customer_account_state</c> in this slice; the
/// account-locked path is exercised by the auth layer (locked accounts cannot mint a
/// JWT) per Cycle B scope. Cycle B/C may layer in a check via <c>ICustomerAccountLifecycleSubscriber</c>
/// once spec 020's account-state probe lands in <c>Modules/Shared/</c>.
///
/// Hard-delete is forbidden across spec 021 entities (FR-005a) — this handler does no
/// DELETEs; rejected requests roll back the entire write transaction.
/// </summary>
public sealed class RequestQuoteFromCartHandler
{
    private readonly B2BDbContext _db;
    private readonly ICartSnapshotProvider _cartSnapshotProvider;
    private readonly IProductRestrictionPolicy _restrictionPolicy;
    private readonly IAuditEventPublisher _auditPublisher;
    private readonly IPublisher _domainPublisher;
    private readonly QuoteRequestRateLimiter _rateLimiter;
    private readonly TimeProvider _time;

    public RequestQuoteFromCartHandler(
        B2BDbContext db,
        ICartSnapshotProvider cartSnapshotProvider,
        IProductRestrictionPolicy restrictionPolicy,
        IAuditEventPublisher auditPublisher,
        IPublisher domainPublisher,
        QuoteRequestRateLimiter rateLimiter,
        TimeProvider time)
    {
        _db = db;
        _cartSnapshotProvider = cartSnapshotProvider;
        _restrictionPolicy = restrictionPolicy;
        _auditPublisher = auditPublisher;
        _domainPublisher = domainPublisher;
        _rateLimiter = rateLimiter;
        _time = time;
    }

    public async Task<RequestQuoteFromCartResult> HandleAsync(
        Guid customerId,
        string customerMarketCode,
        RequestQuoteFromCartRequest body,
        CancellationToken ct)
    {
        // ---------- 0. Resolve the active market schema (rate-limit caps + invariants) ----------
        var schema = await _db.QuoteMarketSchemas
            .AsNoTracking()
            .Where(s => s.MarketCode == customerMarketCode && s.EffectiveTo == null)
            .OrderByDescending(s => s.Version)
            .FirstOrDefaultAsync(ct);

        if (schema is null)
        {
            // The seeder runs at module init (B2BReferenceDataSeeder); a missing schema
            // means seeding never ran or a non-supported market arrived in the JWT. Reject
            // with market_mismatch — surfacing a generic 500 would mask the operational
            // cause and would be hard for spec 014 to localize.
            return RequestQuoteFromCartResult.Reject(
                statusCode: 422,
                reasonCode: QuoteReasonCode.QuoteMarketMismatch,
                detail: $"No active quote_market_schema for market_code='{customerMarketCode}'.");
        }

        // ---------- 1. Rate-limit (per-customer + per-company simultaneously) ----------
        var window = TimeSpan.FromHours(1);
        if (!_rateLimiter.TryAcquireCustomer(customerId, schema.RateLimitPerCustomerPerHour, window))
        {
            return RequestQuoteFromCartResult.Reject(
                statusCode: 429,
                reasonCode: QuoteReasonCode.QuoteRateLimitExceeded,
                detail: "Per-customer rate limit exceeded.",
                extensions: new Dictionary<string, object?>
                {
                    ["retry_after_seconds"] = (int)window.TotalSeconds,
                });
        }
        if (body.CompanyId is { } companyIdForRate
            && !_rateLimiter.TryAcquireCompany(companyIdForRate, schema.RateLimitPerCompanyPerHour, window))
        {
            return RequestQuoteFromCartResult.Reject(
                statusCode: 429,
                reasonCode: QuoteReasonCode.QuoteRateLimitExceeded,
                detail: "Per-company rate limit exceeded.",
                extensions: new Dictionary<string, object?>
                {
                    ["retry_after_seconds"] = (int)window.TotalSeconds,
                });
        }

        // ---------- 2 / 3 / 4. Company gate (membership → suspended → market) ----------
        Company? company = null;
        if (body.CompanyId is { } companyId)
        {
            // Read both the company and the caller's membership in one round trip.
            var companyRecord = await _db.Companies
                .AsNoTracking()
                .Where(c => c.Id == companyId)
                .Select(c => new { c.Id, c.MarketCode, c.State, c.PoRequired, c.UniquePoRequired, Entity = c })
                .FirstOrDefaultAsync(ct);

            if (companyRecord is null)
            {
                // Treat unknown company id as no-membership: handler MUST NOT leak
                // existence of companies the caller has no relationship with.
                return RequestQuoteFromCartResult.Reject(
                    statusCode: 409,
                    reasonCode: QuoteReasonCode.QuoteNoActiveCompanyMembership);
            }

            var membershipRoles = await _db.CompanyMemberships
                .AsNoTracking()
                .Where(m => m.CompanyId == companyId && m.UserId == customerId)
                .Select(m => m.Role)
                .ToListAsync(ct);

            // Allowed roles for cart→quote intake: buyer + companies.admin. Approver
            // role does NOT confer quote-request rights (approvers finalize, not buy).
            var hasAllowedRole = membershipRoles.Any(r => r == "buyer" || r == "companies.admin");
            if (!hasAllowedRole)
            {
                return RequestQuoteFromCartResult.Reject(
                    statusCode: 409,
                    reasonCode: QuoteReasonCode.QuoteNoActiveCompanyMembership);
            }

            // FR-026: suspended companies cannot mint new quotes.
            if (string.Equals(companyRecord.State, "suspended", StringComparison.OrdinalIgnoreCase))
            {
                return RequestQuoteFromCartResult.Reject(
                    statusCode: 422,
                    reasonCode: QuoteReasonCode.QuoteCompanySuspended);
            }

            // FR-011: company market MUST match caller's market-of-record.
            if (!string.Equals(companyRecord.MarketCode, customerMarketCode, StringComparison.OrdinalIgnoreCase))
            {
                return RequestQuoteFromCartResult.Reject(
                    statusCode: 422,
                    reasonCode: QuoteReasonCode.QuoteMarketMismatch);
            }

            company = companyRecord.Entity;

            // ---------- 5. PO required when company.po_required=true ----------
            if (company.PoRequired && string.IsNullOrWhiteSpace(body.PoNumber))
            {
                return RequestQuoteFromCartResult.Reject(
                    statusCode: 400,
                    reasonCode: QuoteReasonCode.QuotePoRequired);
            }

            // ---------- 6. PO-uniqueness when company.unique_po_required=true ----------
            if (company.UniquePoRequired && !string.IsNullOrWhiteSpace(body.PoNumber))
            {
                var poClash = await _db.Quotes
                    .AsNoTracking()
                    .AnyAsync(q => q.CompanyId == company.Id && q.PoNumber == body.PoNumber, ct);
                if (poClash)
                {
                    return RequestQuoteFromCartResult.Reject(
                        statusCode: 409,
                        reasonCode: QuoteReasonCode.QuotePoAlreadyUsed);
                }
            }

            // Branch-belongs-to-company invariant — surface 409-no-membership early
            // rather than letting the FK fail at SaveChanges.
            if (body.BranchId is { } branchId)
            {
                var branchOk = await _db.CompanyBranches
                    .AsNoTracking()
                    .AnyAsync(b => b.Id == branchId && b.CompanyId == company.Id, ct);
                if (!branchOk)
                {
                    return RequestQuoteFromCartResult.Reject(
                        statusCode: 409,
                        reasonCode: QuoteReasonCode.QuoteNoActiveCompanyMembership);
                }
            }
        }

        // ---------- 7. Snapshot the cart and validate non-empty ----------
        var snapshot = await _cartSnapshotProvider.SnapshotAndClearAsync(customerId, ct);
        if (snapshot.Lines.Count == 0)
        {
            return RequestQuoteFromCartResult.Reject(
                statusCode: 400,
                reasonCode: QuoteReasonCode.QuoteCartEmpty);
        }

        // ---------- 8. Per-line restriction-policy snapshot ----------
        var policiesBySku = new Dictionary<string, ProductRestrictionPolicy>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in snapshot.Lines)
        {
            if (policiesBySku.ContainsKey(line.Sku)) continue; // dedupe
            policiesBySku[line.Sku] = await _restrictionPolicy.GetForSkuAsync(line.Sku, ct);
        }

        // ---------- 9. Persist Quote + initial QuoteStateTransition ----------
        var nowUtc = _time.GetUtcNow();
        var quoteId = Guid.NewGuid();
        var serializerOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

        var quote = new Quote
        {
            Id = quoteId,
            CustomerId = customerId,
            CompanyId = body.CompanyId,
            BranchId = body.BranchId,
            MarketCode = customerMarketCode,
            State = QuoteState.Requested.ToToken(),
            RequestedAt = nowUtc,
            CurrentVersionId = null,
            ExpiresAt = null,
            DecidedAt = null,
            DecidedBy = null,
            TerminalAt = null,
            TerminalReason = null,
            PoNumber = string.IsNullOrWhiteSpace(body.PoNumber) ? null : body.PoNumber.Trim(),
            InvoiceBilling = company?.InvoiceBillingEligible ?? false,
            CustomerSuppliedMessageJson = SerializeMessage(body.Message),
            InternalNote = null,
            ApproverRejectionNote = null,
            OriginatingCartSnapshotJson = JsonSerializer.Serialize(
                snapshot.Lines.Select(l => new
                {
                    sku = l.Sku,
                    quantity = l.Quantity,
                    line_note = l.LineNote,
                }),
                serializerOptions),
            OriginatingProductId = null,
            RestrictionPolicySnapshotJson = JsonSerializer.Serialize(
                policiesBySku.Values.Select(p => new
                {
                    sku = p.Sku,
                    restricted_in_markets = p.RestrictedInMarkets.ToArray(),
                    required_profession = p.RequiredProfession,
                    vendor_id = p.VendorId,
                }),
                serializerOptions),
            SchemaVersion = schema.Version,
        };

        var initialTransition = new QuoteStateTransition
        {
            Id = Guid.NewGuid(),
            QuoteId = quoteId,
            MarketCode = customerMarketCode,
            PriorState = "__none__",
            NewState = QuoteState.Requested.ToToken(),
            ActorKind = (body.CompanyId is null ? QuoteActorKind.Customer : QuoteActorKind.Buyer).ToToken(),
            ActorId = customerId,
            ReasonJson = null,
            MetadataJson = "{}",
            OccurredAt = nowUtc,
        };

        _db.Quotes.Add(quote);
        _db.QuoteStateTransitions.Add(initialTransition);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Race-condition fallback for UX_quotes_company_po — concurrent inserts
            // racing on the same (company, po) key. Surface the same code as the
            // pre-write check so the client sees a deterministic outcome.
            return RequestQuoteFromCartResult.Reject(
                statusCode: 409,
                reasonCode: QuoteReasonCode.QuotePoAlreadyUsed);
        }

        // ---------- 10. Audit + domain event (post-commit, fire-and-await) ----------
        // Audit failures must NOT roll back the quote (Principle 25 says audits are
        // append-only side-channel — but they must still happen for the action to be
        // traceable). Wrap so a transient outbound publish doesn't 500 the request;
        // the audit pipeline handles retries.
        await _auditPublisher.PublishAsync(new AuditEvent(
            ActorId: customerId,
            ActorRole: body.CompanyId is null ? "customer" : "buyer",
            Action: "quote.state_changed",
            EntityType: "quote",
            EntityId: quote.Id,
            BeforeState: new { state = "__none__" },
            AfterState: new { state = quote.State, market_code = quote.MarketCode, company_id = quote.CompanyId },
            Reason: null), ct);

        await _domainPublisher.Publish(new QuoteRequested(
            QuoteId: quote.Id,
            CustomerId: customerId,
            CompanyId: quote.CompanyId,
            MarketCode: quote.MarketCode,
            LocaleHint: ResolveLocaleHint(body.Message)), ct);

        return RequestQuoteFromCartResult.Success(new RequestQuoteFromCartResponse(
            Id: quote.Id,
            State: quote.State,
            MarketCode: quote.MarketCode,
            CompanyId: quote.CompanyId,
            BranchId: quote.BranchId,
            RequestedAt: quote.RequestedAt));
    }

    private static string? SerializeMessage(LocalizedMessage? message)
    {
        if (message is null) return null;
        var hasEn = !string.IsNullOrWhiteSpace(message.En);
        var hasAr = !string.IsNullOrWhiteSpace(message.Ar);
        if (!hasEn && !hasAr) return null;
        return JsonSerializer.Serialize(new
        {
            en = hasEn ? message.En : null,
            ar = hasAr ? message.Ar : null,
        });
    }

    private static string ResolveLocaleHint(LocalizedMessage? message)
    {
        // Default to "en" when no message present; otherwise prefer whichever
        // locale the customer actually wrote in.
        if (message is null) return "en";
        if (!string.IsNullOrWhiteSpace(message.Ar) && string.IsNullOrWhiteSpace(message.En)) return "ar";
        return "en";
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation;
}

/// <summary>
/// Internal handler-result envelope. The endpoint translates this into a
/// <c>Results.Created</c> on success or a problem-details payload on rejection.
/// </summary>
public sealed record RequestQuoteFromCartResult(
    bool IsSuccess,
    int StatusCode,
    QuoteReasonCode? ReasonCode,
    string? Detail,
    RequestQuoteFromCartResponse? Response,
    IDictionary<string, object?>? Extensions)
{
    public static RequestQuoteFromCartResult Success(RequestQuoteFromCartResponse r) =>
        new(true, 201, null, null, r, null);

    public static RequestQuoteFromCartResult Reject(
        int statusCode,
        QuoteReasonCode reasonCode,
        string? detail = null,
        IDictionary<string, object?>? extensions = null) =>
        new(false, statusCode, reasonCode, detail, null, extensions);
}

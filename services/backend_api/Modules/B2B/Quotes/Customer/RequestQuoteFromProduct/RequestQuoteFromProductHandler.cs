using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.B2B.Entities;
using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.B2B.Primitives;
using BackendApi.Modules.B2B.Quotes.Customer.RequestQuoteFromCart;
using BackendApi.Modules.B2B.RateLimit;
using BackendApi.Modules.Shared;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BackendApi.Modules.B2B.Quotes.Customer.RequestQuoteFromProduct;

/// <summary>
/// Spec 021 contract §2.2 / FR-018 — handler for
/// <c>POST /api/customer/quotes/from-product</c>. Mirrors
/// <see cref="RequestQuoteFromCartHandler"/>'s pre-write rejection cascade
/// (rate-limit → membership → suspended → market → po-required → po-already-used)
/// with two from-product-specific differences:
/// <list type="number">
///   <item>The cart is NOT cleared. Cart items remain; the quote is independent.</item>
///   <item>An additional <c>quote.product_not_quotable</c> gate via
///         <see cref="IProductCatalogQuery.IsQuotableAsync"/> — the spec 005 catalog
///         owns the canonical "this product is quotable" flag (per task T076).</item>
/// </list>
///
/// SKU resolution: the catalog has not yet exposed a stable SKU for a given
/// product id (spec 005 gap). Until that lands, the originating cart-snapshot
/// row uses a synthetic <c>product:{guid}</c> token so the restriction-policy
/// snapshot, audit trail, and admin-detail rendering all have a consistent
/// per-line identifier. The synthetic token is replaced with the canonical SKU
/// when spec 005's catalog query lands; admin authoring (T087) reads the latest
/// snapshot at author time so the swap is non-breaking.
/// </summary>
public sealed class RequestQuoteFromProductHandler
{
    private readonly B2BDbContext _db;
    private readonly IProductCatalogQuery _catalog;
    private readonly IProductRestrictionPolicy _restrictionPolicy;
    private readonly IAuditEventPublisher _auditPublisher;
    private readonly IPublisher _domainPublisher;
    private readonly QuoteRequestRateLimiter _rateLimiter;
    private readonly TimeProvider _time;
    private readonly ILogger<RequestQuoteFromProductHandler> _logger;

    public RequestQuoteFromProductHandler(
        B2BDbContext db,
        IProductCatalogQuery catalog,
        IProductRestrictionPolicy restrictionPolicy,
        IAuditEventPublisher auditPublisher,
        IPublisher domainPublisher,
        QuoteRequestRateLimiter rateLimiter,
        TimeProvider time,
        ILogger<RequestQuoteFromProductHandler> logger)
    {
        _db = db;
        _catalog = catalog;
        _restrictionPolicy = restrictionPolicy;
        _auditPublisher = auditPublisher;
        _domainPublisher = domainPublisher;
        _rateLimiter = rateLimiter;
        _time = time;
        _logger = logger;
    }

    public async Task<RequestQuoteFromProductResult> HandleAsync(
        Guid customerId,
        string customerMarketCode,
        RequestQuoteFromProductRequest body,
        CancellationToken ct)
    {
        // The validator guarantees ProductId / Quantity are present and Quantity ≥ 1.
        var productId = body.ProductId!.Value;
        var quantity = body.Quantity!.Value;

        // ---------- 0. Resolve the active market schema ----------
        var schema = await _db.QuoteMarketSchemas
            .AsNoTracking()
            .Where(s => s.MarketCode == customerMarketCode && s.EffectiveTo == null)
            .OrderByDescending(s => s.Version)
            .FirstOrDefaultAsync(ct);
        if (schema is null)
        {
            return RequestQuoteFromProductResult.Reject(
                statusCode: 422,
                reasonCode: QuoteReasonCode.QuoteMarketMismatch,
                detail: $"No active quote_market_schema for market_code='{customerMarketCode}'.");
        }

        // ---------- 1. Per-customer rate-limit ----------
        // Account-state (FR-038 / `quote.account_inactive`) carry-over: same posture as
        // RequestQuoteFromCartHandler (lines 44-48) — spec 021 has no runtime contract
        // with spec 020's `customer_account_state` in this slice; the auth layer
        // ensures locked accounts cannot mint a JWT in the first place. The
        // 422 quote.account_inactive surface is reserved for the post-auth lifecycle
        // hook landing in Phase 10 (T140-T142).
        var window = TimeSpan.FromHours(1);
        if (!_rateLimiter.TryAcquireCustomer(customerId, schema.RateLimitPerCustomerPerHour, window))
        {
            return RequestQuoteFromProductResult.Reject(
                statusCode: 429,
                reasonCode: QuoteReasonCode.QuoteRateLimitExceeded,
                detail: "Per-customer rate limit exceeded.",
                extensions: new Dictionary<string, object?>
                {
                    ["retry_after_seconds"] = (int)window.TotalSeconds,
                });
        }

        // ---------- 2 / 3 / 4. Company gate (membership → suspended → market) ----------
        // CodeRabbit Round 1: every reject after Step 1 acquires the customer-bucket
        // slot must release it so a malformed/unauthorized request doesn't permanently
        // burn the caller's hourly quota. Helper inlined to keep the cascade readable.
        RequestQuoteFromProductResult RejectAndRelease(int statusCode, QuoteReasonCode code,
            string? detail = null, IDictionary<string, object?>? extensions = null)
        {
            _rateLimiter.ReleaseCustomer(customerId);
            return RequestQuoteFromProductResult.Reject(statusCode, code, detail, extensions);
        }

        Company? company = null;
        if (body.CompanyId is { } companyId)
        {
            company = await _db.Companies
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == companyId, ct);
            if (company is null)
            {
                return RejectAndRelease(409, QuoteReasonCode.QuoteNoActiveCompanyMembership);
            }

            var membershipRoles = await _db.CompanyMemberships
                .AsNoTracking()
                .Where(m => m.CompanyId == companyId && m.UserId == customerId)
                .Select(m => m.Role)
                .ToListAsync(ct);
            var hasAllowedRole = membershipRoles.Any(r => r == "buyer" || r == "companies.admin");
            if (!hasAllowedRole)
            {
                return RejectAndRelease(409, QuoteReasonCode.QuoteNoActiveCompanyMembership);
            }

            if (string.Equals(company.State, "suspended", StringComparison.OrdinalIgnoreCase))
            {
                return RejectAndRelease(422, QuoteReasonCode.QuoteCompanySuspended);
            }

            if (!string.Equals(company.MarketCode, customerMarketCode, StringComparison.OrdinalIgnoreCase))
            {
                return RejectAndRelease(422, QuoteReasonCode.QuoteMarketMismatch);
            }

            // Per-company rate-limit (membership-gated to prevent cross-tenant DoS).
            if (!_rateLimiter.TryAcquireCompany(companyId, schema.RateLimitPerCompanyPerHour, window))
            {
                return RejectAndRelease(429,
                    QuoteReasonCode.QuoteRateLimitExceeded,
                    "Per-company rate limit exceeded.",
                    new Dictionary<string, object?>
                    {
                        ["retry_after_seconds"] = (int)window.TotalSeconds,
                    });
            }

            if (company.PoRequired && string.IsNullOrWhiteSpace(body.PoNumber))
            {
                return RejectAndRelease(400, QuoteReasonCode.QuotePoRequired);
            }

            if (company.UniquePoRequired && !string.IsNullOrWhiteSpace(body.PoNumber))
            {
                var poClash = await _db.Quotes
                    .AsNoTracking()
                    .AnyAsync(q => q.CompanyId == company.Id && q.PoNumber == body.PoNumber, ct);
                if (poClash)
                {
                    return RejectAndRelease(409, QuoteReasonCode.QuotePoAlreadyUsed);
                }
            }

            if (body.BranchId is { } branchId)
            {
                var branchOk = await _db.CompanyBranches
                    .AsNoTracking()
                    .AnyAsync(b => b.Id == branchId && b.CompanyId == company.Id, ct);
                if (!branchOk)
                {
                    return RejectAndRelease(409, QuoteReasonCode.QuoteNoActiveCompanyMembership);
                }
            }
        }

        // ---------- 5. Product-quotability gate (FR-005 / spec.md US2) ----------
        // Spec 005 owns the canonical "is this product available for quote requests"
        // flag. Non-quotable products short-circuit BEFORE we mint the quote row.
        var quotable = await _catalog.IsQuotableAsync(productId, ct);
        if (!quotable)
        {
            return RejectAndRelease(400, QuoteReasonCode.QuoteProductNotQuotable);
        }

        // ---------- 6. Restriction-policy snapshot (single line) ----------
        var syntheticSku = $"product:{productId:N}";
        var policy = await _restrictionPolicy.GetForSkuAsync(syntheticSku, ct);

        // ---------- 7. Persist Quote + initial QuoteStateTransition ----------
        var nowUtc = _time.GetUtcNow();
        var quoteId = Guid.NewGuid();
        var serializerOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

        // For from-product, originating_cart_snapshot is set to a single synthetic
        // line so the eligibility gate (T070) and admin-detail (T086) can read SKUs
        // off the same field — no per-surface branching. The OriginatingProductId
        // column carries the canonical product reference for future SKU swaps.
        var snapshotLine = new
        {
            sku = syntheticSku,
            quantity,
            line_note = (string?)null,
            originating_product_id = productId,
        };

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
            OriginatingCartSnapshotJson = JsonSerializer.Serialize(new[] { snapshotLine }, serializerOptions),
            OriginatingProductId = productId,
            RestrictionPolicySnapshotJson = JsonSerializer.Serialize(new[]
            {
                new
                {
                    sku = policy.Sku,
                    restricted_in_markets = policy.RestrictedInMarkets.ToArray(),
                    required_profession = policy.RequiredProfession,
                    vendor_id = policy.VendorId,
                },
            }, serializerOptions),
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
            MetadataJson = JsonSerializer.Serialize(new
            {
                source = "from-product",
                originating_product_id = productId,
            }),
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
            // PO unique-constraint race lost — release rate-limit tokens since the
            // request never produced a persisted quote (CodeRabbit Round 1).
            _rateLimiter.ReleaseCustomer(customerId);
            return RequestQuoteFromProductResult.Reject(
                statusCode: 409,
                reasonCode: QuoteReasonCode.QuotePoAlreadyUsed);
        }

        // ---------- 8. Audit + domain event (post-commit, isolated) ----------
        try
        {
            await _auditPublisher.PublishAsync(new AuditEvent(
                ActorId: customerId,
                ActorRole: body.CompanyId is null ? "customer" : "buyer",
                Action: "quote.state_changed",
                EntityType: "quote",
                EntityId: quote.Id,
                BeforeState: new { state = "__none__" },
                AfterState: new
                {
                    state = quote.State,
                    market_code = quote.MarketCode,
                    company_id = quote.CompanyId,
                    originating_product_id = productId,
                },
                Reason: "from_product"), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Audit publish failed for quote.state_changed from-product (quote_id={QuoteId}, customer_id={CustomerId}). "
                + "Quote was committed; audit-pipeline retry is responsible for replay.",
                quote.Id, customerId);
        }

        try
        {
            await _domainPublisher.Publish(new QuoteRequested(
                QuoteId: quote.Id,
                CustomerId: customerId,
                CompanyId: quote.CompanyId,
                MarketCode: quote.MarketCode,
                LocaleHint: ResolveLocaleHint(body.Message)), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "QuoteRequested domain-event publish failed for from-product (quote_id={QuoteId}). "
                + "Notification subscribers (spec 025) will not fire unless replayed.",
                quote.Id);
        }

        return RequestQuoteFromProductResult.Success(new RequestQuoteFromProductResponse(
            Id: quote.Id,
            State: quote.State,
            MarketCode: quote.MarketCode,
            CompanyId: quote.CompanyId,
            BranchId: quote.BranchId,
            OriginatingProductId: productId,
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
        if (message is null) return "en";
        if (!string.IsNullOrWhiteSpace(message.Ar) && string.IsNullOrWhiteSpace(message.En)) return "ar";
        return "en";
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation;
}

public sealed record RequestQuoteFromProductResult(
    bool IsSuccess,
    int StatusCode,
    QuoteReasonCode? ReasonCode,
    string? Detail,
    RequestQuoteFromProductResponse? Response,
    IDictionary<string, object?>? Extensions)
{
    public static RequestQuoteFromProductResult Success(RequestQuoteFromProductResponse r) =>
        new(true, 201, null, null, r, null);

    public static RequestQuoteFromProductResult Reject(
        int statusCode,
        QuoteReasonCode reasonCode,
        string? detail = null,
        IDictionary<string, object?>? extensions = null) =>
        new(false, statusCode, reasonCode, detail, null, extensions);
}

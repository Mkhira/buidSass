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
/// Spec 021 contract §2.2 — handler for
/// <c>POST /api/customer/quotes/from-product</c>. Mirror of
/// <see cref="RequestQuoteFromCartHandler"/> with three deliberate deltas (per
/// contract §2.2 "same as §2.1 except…"):
///
/// <list type="number">
///   <item>The line item is built from <c>(product_id, quantity)</c> in the request
///         body — there is no cart snapshot.</item>
///   <item>The cart is NOT cleared (the customer may legitimately have other items
///         in their cart that are unrelated to this quote).</item>
///   <item>One additional pre-write check after the standard cascade:
///         <c>IProductCatalogQuery.IsQuotableAsync(product_id)</c> — false
///         surfaces <c>400 quote.product_not_quotable</c> (FR mapping table §6).</item>
/// </list>
///
/// Pre-write rejection sequence (deterministic, identical ordering to T064 modulo
/// the appended quotability check):
/// <list type="number">
///   <item>Account active (FR-038 carry-over hook — placeholder until spec 020 wires
///         <c>ICustomerAccountLifecycleSubscriber</c> probing into this hot path).</item>
///   <item>Per-customer rate-limit (anti-DoS gate before any cross-module read).</item>
///   <item>Company gate (membership → suspended → market) — only when
///         <c>company_id</c> supplied.</item>
///   <item>Per-company rate-limit (membership-gated; refunds the customer slot on
///         per-company refusal — same fix-shape as the cart handler).</item>
///   <item>PO required when <c>company.po_required=true</c>.</item>
///   <item>PO-uniqueness when <c>company.unique_po_required=true</c>.</item>
///   <item>Branch belongs to the company (when supplied).</item>
///   <item>Product is quotable (<c>IProductCatalogQuery.IsQuotableAsync</c>).</item>
/// </list>
///
/// <strong>Per-line restriction snapshot</strong>: spec 005's
/// <see cref="IProductCatalogQuery"/> does not yet expose a Guid→SKU resolver, so the
/// restriction policy is keyed on a synthetic SKU token of the form
/// <c>"product:{product_id}"</c>. The
/// <see cref="StubProductRestrictionPolicy"/>'s default-Unrestricted behavior keeps
/// happy-path tests green; tests asserting restriction-driven eligibility seed the
/// stub against the same synthetic key. When spec 005 lands a real
/// <c>GetSkusForProductAsync</c> probe, this handler updates to fan the snapshot
/// across the resolved SKUs (TODO: spec 005 extension — see contracts §2.2 / FR-036).
///
/// <strong>Hard-delete</strong>: forbidden across spec 021 entities (FR-005a) — this
/// handler does no DELETEs; rejected requests roll back the entire write transaction.
/// </summary>
public sealed class RequestQuoteFromProductHandler
{
    private readonly B2BDbContext _db;
    private readonly IProductRestrictionPolicy _restrictionPolicy;
    private readonly IProductCatalogQuery _catalogQuery;
    private readonly IAuditEventPublisher _auditPublisher;
    private readonly IPublisher _domainPublisher;
    private readonly QuoteRequestRateLimiter _rateLimiter;
    private readonly TimeProvider _time;
    private readonly ILogger<RequestQuoteFromProductHandler> _logger;

    public RequestQuoteFromProductHandler(
        B2BDbContext db,
        IProductRestrictionPolicy restrictionPolicy,
        IProductCatalogQuery catalogQuery,
        IAuditEventPublisher auditPublisher,
        IPublisher domainPublisher,
        QuoteRequestRateLimiter rateLimiter,
        TimeProvider time,
        ILogger<RequestQuoteFromProductHandler> logger)
    {
        _db = db;
        _restrictionPolicy = restrictionPolicy;
        _catalogQuery = catalogQuery;
        _auditPublisher = auditPublisher;
        _domainPublisher = domainPublisher;
        _rateLimiter = rateLimiter;
        _time = time;
        _logger = logger;
    }

    public async Task<RequestQuoteFromProductResult> HandleAsync(
        Guid customerId,
        string customerMarketCode,
        bool callerAccountLocked,
        RequestQuoteFromProductRequest body,
        CancellationToken ct)
    {
        // Defensive — the validator already enforces both, but the handler must not
        // trust them blindly when called from internal pipelines.
        if (!body.ProductId.HasValue || body.ProductId.Value == Guid.Empty || !body.Quantity.HasValue || body.Quantity.Value <= 0)
        {
            return RequestQuoteFromProductResult.Reject(
                statusCode: 400,
                reasonCode: QuoteReasonCode.QuoteRequiredFieldMissing,
                detail: "product_id and quantity are required and must be valid.");
        }

        var productId = body.ProductId.Value;
        var quantity = body.Quantity.Value;

        // ---------- 0. Resolve the active market schema (rate-limit caps + invariants) ----------
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

        // ---------- 1. Account active (FR-038) ----------
        // Spec 020 (Identity) signals account-locked customers via the
        // `account.locked` permission claim until ICustomerAccountLifecycleSubscriber
        // fans the state into our DbContext. The endpoint surfaces the boolean here
        // so the handler stays I/O-cheap for the common active path.
        if (callerAccountLocked)
        {
            return RequestQuoteFromProductResult.Reject(
                statusCode: 422,
                reasonCode: QuoteReasonCode.QuoteAccountInactive,
                detail: "Caller account is not active.");
        }

        // ---------- 2. Per-customer rate-limit (cheap anti-DoS gate) ----------
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

        // ---------- 3-6. Company gate (membership → suspended → market → per-company rate-limit → PO) ----------
        Company? company = null;
        if (body.CompanyId is { } companyId)
        {
            company = await _db.Companies
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == companyId, ct);
            if (company is null)
            {
                return RequestQuoteFromProductResult.Reject(
                    statusCode: 409,
                    reasonCode: QuoteReasonCode.QuoteNoActiveCompanyMembership);
            }

            var membershipRoles = await _db.CompanyMemberships
                .AsNoTracking()
                .Where(m => m.CompanyId == companyId && m.UserId == customerId)
                .Select(m => m.Role)
                .ToListAsync(ct);

            var hasAllowedRole = membershipRoles.Any(r => r == "buyer" || r == "companies.admin");
            if (!hasAllowedRole)
            {
                return RequestQuoteFromProductResult.Reject(
                    statusCode: 409,
                    reasonCode: QuoteReasonCode.QuoteNoActiveCompanyMembership);
            }

            // FR-026 — suspended companies cannot mint new quotes.
            if (string.Equals(company.State, "suspended", StringComparison.OrdinalIgnoreCase))
            {
                return RequestQuoteFromProductResult.Reject(
                    statusCode: 422,
                    reasonCode: QuoteReasonCode.QuoteCompanySuspended);
            }

            // FR-011 — company market MUST match caller's market-of-record.
            if (!string.Equals(company.MarketCode, customerMarketCode, StringComparison.OrdinalIgnoreCase))
            {
                return RequestQuoteFromProductResult.Reject(
                    statusCode: 422,
                    reasonCode: QuoteReasonCode.QuoteMarketMismatch);
            }

            // ---------- Per-company rate-limit (membership-gated) ----------
            if (!_rateLimiter.TryAcquireCompany(companyId, schema.RateLimitPerCompanyPerHour, window))
            {
                _rateLimiter.ReleaseCustomer(customerId);
                return RequestQuoteFromProductResult.Reject(
                    statusCode: 429,
                    reasonCode: QuoteReasonCode.QuoteRateLimitExceeded,
                    detail: "Per-company rate limit exceeded.",
                    extensions: new Dictionary<string, object?>
                    {
                        ["retry_after_seconds"] = (int)window.TotalSeconds,
                    });
            }

            // ---------- PO required when company.po_required=true ----------
            if (company.PoRequired && string.IsNullOrWhiteSpace(body.PoNumber))
            {
                return RequestQuoteFromProductResult.Reject(
                    statusCode: 400,
                    reasonCode: QuoteReasonCode.QuotePoRequired);
            }

            // ---------- PO-uniqueness when company.unique_po_required=true ----------
            if (company.UniquePoRequired && !string.IsNullOrWhiteSpace(body.PoNumber))
            {
                var poClash = await _db.Quotes
                    .AsNoTracking()
                    .AnyAsync(q => q.CompanyId == company.Id && q.PoNumber == body.PoNumber, ct);
                if (poClash)
                {
                    return RequestQuoteFromProductResult.Reject(
                        statusCode: 409,
                        reasonCode: QuoteReasonCode.QuotePoAlreadyUsed);
                }
            }

            // Branch-belongs-to-company invariant.
            if (body.BranchId is { } branchId)
            {
                var branchOk = await _db.CompanyBranches
                    .AsNoTracking()
                    .AnyAsync(b => b.Id == branchId && b.CompanyId == company.Id, ct);
                if (!branchOk)
                {
                    return RequestQuoteFromProductResult.Reject(
                        statusCode: 409,
                        reasonCode: QuoteReasonCode.QuoteNoActiveCompanyMembership);
                }
            }
        }

        // ---------- 7. Product quotability (spec 005) ----------
        // §2.2 — the product must exist and be flagged quotable. Spec 005's
        // implementation lands later; the test stub treats every product as
        // quotable by default and tests opt into the negative path by seeding
        // ProductCatalogQuery.NonQuotableProductIds.
        var isQuotable = await _catalogQuery.IsQuotableAsync(productId, ct);
        if (!isQuotable)
        {
            return RequestQuoteFromProductResult.Reject(
                statusCode: 400,
                reasonCode: QuoteReasonCode.QuoteProductNotQuotable);
        }

        // ---------- 8. Per-line restriction snapshot ----------
        // TODO(spec-005): when IProductCatalogQuery exposes a Guid→SKU resolver,
        // expand this snapshot across all SKUs the product publishes. For now we
        // key on a synthetic "product:{guid}" token so the snapshot row carries
        // identifying information; the StubProductRestrictionPolicy returns an
        // Unrestricted result by default (no eligibility gate at acceptance time).
        var syntheticSku = $"product:{productId:N}";
        var policy = await _restrictionPolicy.GetForSkuAsync(syntheticSku, ct);

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
            // FR-027: invoice_billing follows the company's eligibility for company
            // quotes; for individual customers it is ALWAYS false (no business invoicing
            // path in V1). Mirror of T064.
            InvoiceBilling = company?.InvoiceBillingEligible ?? false,
            CustomerSuppliedMessageJson = SerializeMessage(body.Message),
            InternalNote = null,
            ApproverRejectionNote = null,
            // §2.2 — originating_cart_snapshot stays NULL; the line is materialized
            // from product_id + quantity at admin authoring time.
            OriginatingCartSnapshotJson = null,
            OriginatingProductId = productId,
            RestrictionPolicySnapshotJson = JsonSerializer.Serialize(
                new[]
                {
                    new
                    {
                        sku = policy.Sku,
                        product_id = productId,
                        quantity,
                        restricted_in_markets = policy.RestrictedInMarkets.ToArray(),
                        required_profession = policy.RequiredProfession,
                        vendor_id = policy.VendorId,
                    },
                },
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
            MetadataJson = JsonSerializer.Serialize(new
            {
                origin = "product",
                product_id = productId,
                quantity,
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
            // Race-condition fallback for UX_quotes_company_po — concurrent inserts
            // racing on the same (company, po) key.
            return RequestQuoteFromProductResult.Reject(
                statusCode: 409,
                reasonCode: QuoteReasonCode.QuotePoAlreadyUsed);
        }

        // ---------- 10. Audit + domain event (post-commit, isolated from response) ----------
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
                Reason: null), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Audit publish failed for quote.state_changed (quote_id={QuoteId}, customer_id={CustomerId}, "
                + "origin=product). Quote was committed; audit-pipeline retry path is responsible for replay.",
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
                "QuoteRequested domain-event publish failed (quote_id={QuoteId}, origin=product). "
                + "Notification subscribers (spec 025) will not fire for this quote unless replayed.",
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

/// <summary>
/// Internal handler-result envelope. The endpoint translates this into a
/// <c>Results.Created</c> on success or a problem-details payload on rejection.
/// </summary>
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

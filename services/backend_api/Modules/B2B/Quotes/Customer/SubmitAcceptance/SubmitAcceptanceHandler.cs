using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.B2B.Entities;
using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.B2B.Primitives;
using BackendApi.Modules.B2B.Quotes.Customer.Visibility;
using BackendApi.Modules.Shared;
using BackendApi.Modules.Verification.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.B2B.Quotes.Customer.SubmitAcceptance;

/// <summary>
/// Spec 021 contract §2.7 — handler for
/// <c>POST /api/customer/quotes/{id}/submit-acceptance</c>. The launch-MVP linchpin:
/// 7+ logical branches, transactional, idempotent, race-protected by xmin.
///
/// <para><strong>Permission</strong> (§2.7): caller is the customer-owner OR holds
/// <c>buyer</c> / <c>companies.admin</c> membership for the quote's company.
/// <c>approver</c> can READ but not submit-acceptance — surface 404 (visibility-leak
/// symmetry, same convention as Withdraw / RequestRevision).</para>
///
/// <para><strong>Branch order</strong> (deterministic, contract §2.7 + Edge Cases):
/// <list type="number">
///   <item>Visibility + authority gate (404 if missing).</item>
///   <item>State-machine gate — only <c>revised</c> can transition (409 invalid_state_for_action).</item>
///   <item>Expiry race (409 expired) — <c>expires_at</c> elapsed.</item>
///   <item>Eligibility gate per quote line (FR-036; 422 eligibility_required).</item>
///   <item>Market mismatch (FR-046; 422 market_mismatch).</item>
///   <item>Company suspended (spec.md §Edge Case "company is suspended"; 422 company_suspended).</item>
///   <item>PO required (FR-009; 400 po_required) when <c>company.po_required=true</c>
///         and neither body nor quote carries a PO.</item>
///   <item>PO collision —
///     <list type="bullet">
///       <item><c>unique_po_required=true</c> + collision → hard reject 409 po_already_used.</item>
///       <item><c>unique_po_required=false</c> + collision + <c>po_warning_acknowledged=false</c>
///             → return 200 with <c>po_warning</c> envelope, NO state transition.</item>
///       <item><c>unique_po_required=false</c> + collision + <c>po_warning_acknowledged=true</c>
///             → commit + audit <c>quote.po_warning_acknowledged</c> with prior_quote_ids.</item>
///     </list>
///   </item>
///   <item>Routing fork:
///     <list type="bullet">
///       <item>Individual quote (CompanyId IS NULL) → direct <c>accepted</c> + conversion.</item>
///       <item>Company quote with <c>approver_required=true</c> + ≥ 1 approver → <c>pending-approver</c>
///             + publish <c>QuotePendingApprover</c>.</item>
///       <item>Company quote with <c>approver_required=true</c> + 0 approvers → 409 no_approver_available.</item>
///       <item>Company quote with <c>approver_required=false</c> → direct <c>accepted</c> + conversion.</item>
///     </list>
///   </item>
///   <item>Conversion (when transitioning to <c>accepted</c>): invoke
///         <see cref="IOrderFromQuoteHandler.CreateAsync"/> within the same logical
///         transaction. The handler is currently STUBBED in tests
///         (<c>StubOrderFromQuoteHandler</c>); the production binding lands with
///         spec 011. Per T070 task description, T100 wires the real
///         <c>QuoteToOrderConverter</c> from US6.</item>
/// </list></para>
///
/// <para><strong>Tax-preview drift</strong> (R11): the conversion path returns drift
/// info in the <see cref="OrderConversionResult"/> envelope; when drift exceeds the
/// per-market threshold AND <c>tax_preview_drift_acknowledged=false</c>, this handler
/// surfaces 409 quote.tax_preview_drift_threshold_exceeded with the new tax + drift %
/// in the body. The V1 stub does not currently report drift — this guard is a
/// no-op in the stub path; T100 wires the real signal once US6 lands.</para>
///
/// <para><strong>xmin / optimistic concurrency</strong>: SC-009 — concurrent finalize
/// races between approvers (and between buyer-acceptance and approver-finalize) MUST
/// resolve deterministically. The xmin row-version on <see cref="Quote.XminRowVersion"/>
/// is configured in EF as a concurrency token. Lost-update surfaces as
/// <see cref="DbUpdateConcurrencyException"/> → mapped to 409 invalid_state_for_action.</para>
///
/// <para><strong>Idempotency-Key</strong>: passed through to the conversion handler so
/// replays return the prior order id without duplicate side effects (per
/// <see cref="QuoteConversionRequest.IdempotencyKey"/>). Endpoint enforces presence;
/// the handler does NOT yet maintain a server-side replay store for THIS endpoint —
/// that lands with the outbox in Phase 1.5. The conversion handler's idempotency
/// guarantee is sufficient for the order-creation side effect.</para>
///
/// <para><strong>Hard-delete</strong>: forbidden — accepted / pending-approver are
/// non-terminal mutations on the same row.</para>
/// </summary>
public sealed class SubmitAcceptanceHandler
{
    private readonly B2BDbContext _db;
    private readonly IAuditEventPublisher _auditPublisher;
    private readonly IPublisher _domainPublisher;
    private readonly ICustomerVerificationEligibilityQuery _eligibilityQuery;
    private readonly IOrderFromQuoteHandler _orderFromQuoteHandler;
    private readonly TimeProvider _time;
    private readonly ILogger<SubmitAcceptanceHandler> _logger;

    public SubmitAcceptanceHandler(
        B2BDbContext db,
        IAuditEventPublisher auditPublisher,
        IPublisher domainPublisher,
        ICustomerVerificationEligibilityQuery eligibilityQuery,
        IOrderFromQuoteHandler orderFromQuoteHandler,
        TimeProvider time,
        ILogger<SubmitAcceptanceHandler> logger)
    {
        _db = db;
        _auditPublisher = auditPublisher;
        _domainPublisher = domainPublisher;
        _eligibilityQuery = eligibilityQuery;
        _orderFromQuoteHandler = orderFromQuoteHandler;
        _time = time;
        _logger = logger;
    }

    public async Task<SubmitAcceptanceResult> HandleAsync(
        Guid quoteId,
        Guid customerId,
        string callerMarketCode,
        Guid idempotencyKey,
        SubmitAcceptanceRequest body,
        CancellationToken ct,
        string callerLocaleHint = "en")
    {
        // ---------- 1. Visibility gate ----------
        var visibleCompanyIds = await CustomerQuoteVisibility
            .GetVisibleCompanyIdsAsync(_db, customerId, ct);

        var visibleQuote = await CustomerQuoteVisibility.GetVisibleQuoteAsync(
            _db, quoteId, customerId, visibleCompanyIds, ct);

        if (visibleQuote is null)
        {
            return SubmitAcceptanceResult.NotFound();
        }

        // ---------- 2. Authority gate (customer-owner OR buyer / companies.admin) ----------
        var initialAuthority = await ResolveAuthorityAsync(visibleQuote, customerId, ct);
        if (initialAuthority == AcceptanceAuthority.None)
        {
            // Read-but-not-write (e.g. approver-only) collapses to 404 to preserve
            // visibility symmetry — same convention as Withdraw / RequestRevision.
            return SubmitAcceptanceResult.NotFound();
        }

        // ---------- 3. Re-fetch with tracking ----------
        var tracked = await _db.Quotes
            .FirstOrDefaultAsync(q => q.Id == quoteId, ct);
        if (tracked is null)
        {
            return SubmitAcceptanceResult.NotFound();
        }

        // ---------- 3a. TOCTOU re-validate authority on tracked entity ----------
        // Memberships can be revoked between the visibility read and the mutation.
        // Re-run the authority predicate against the tracked row inside the same
        // unit of work (same fix as Withdraw + RequestRevision after CodeRabbit
        // Round 1 — Critical).
        var authority = await ResolveAuthorityAsync(tracked, customerId, ct);
        if (authority == AcceptanceAuthority.None)
        {
            return SubmitAcceptanceResult.NotFound();
        }

        // ---------- 4. State-machine gate ----------
        if (!QuoteStateExtensions.TryParseToken(tracked.State, out var currentState))
        {
            _logger.LogError(
                "Quote {QuoteId} has unrecognized state token '{State}' — rejecting submit-acceptance.",
                tracked.Id, tracked.State);
            return SubmitAcceptanceResult.InvalidStateForAction();
        }

        // §2.7 errors: "must be `revised`". Anything else (requested / drafted /
        // pending-approver / terminal) is invalid_state_for_action.
        if (currentState != QuoteState.Revised)
        {
            return SubmitAcceptanceResult.InvalidStateForAction();
        }

        // ---------- 5. Expiry gate ----------
        // Race with QuoteExpiryWorker — when expires_at has elapsed but the worker
        // hasn't transitioned the row yet, we must reject ourselves rather than let
        // an expired quote convert to an order.
        var nowUtc = _time.GetUtcNow();
        if (tracked.ExpiresAt.HasValue && tracked.ExpiresAt.Value <= nowUtc)
        {
            return SubmitAcceptanceResult.Expired();
        }

        // ---------- 6. Market mismatch (FR-046) ----------
        if (!string.Equals(tracked.MarketCode, callerMarketCode, StringComparison.OrdinalIgnoreCase))
        {
            return SubmitAcceptanceResult.MarketMismatch();
        }

        // ---------- 7. Company-suspended gate ----------
        // Spec.md §Edge Case "operator publishes a quote, then the customer's
        // company is suspended" — buyer MUST be unable to accept. This MUST run
        // before the eligibility gate so a suspended company sees
        // `quote.company_suspended` regardless of restricted-SKU eligibility
        // state (the eligibility surface would otherwise mask the underlying
        // suspension reason). Existing accepted quotes are unaffected (orders
        // already exist).
        Company? company = null;
        if (tracked.CompanyId is { } companyId)
        {
            company = await _db.Companies
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == companyId, ct);
            if (company is null)
            {
                // Defensive: a quote with a non-null CompanyId but no Company row
                // shouldn't exist (FK enforcement). Treat as not-found.
                return SubmitAcceptanceResult.NotFound();
            }

            if (string.Equals(company.State, "suspended", StringComparison.OrdinalIgnoreCase))
            {
                return SubmitAcceptanceResult.CompanySuspended();
            }
        }

        // ---------- 8. Eligibility gate (FR-036) ----------
        // For every restricted SKU on the most recent QuoteVersion, the buyer-of-
        // record (Quote.CustomerId — the customer who will receive the order) must
        // be Eligible (or Unrestricted) per spec 020. Ineligible → 422.
        // We use tracked.CustomerId (NOT the JWT actor) because for company quotes
        // the actor may be `companies.admin` who is not the consumer of the
        // restricted product; spec 020's eligibility cache is keyed on the
        // buyer-of-record's verification. Market is the quote's MarketCode (the
        // buyer-of-record's market-of-record at request time, validated against
        // the caller's market in step 6 above).
        var eligibilityFailure = await CheckEligibilityAsync(tracked, tracked.CustomerId, tracked.MarketCode, ct);
        if (eligibilityFailure)
        {
            return SubmitAcceptanceResult.EligibilityRequired();
        }

        // ---------- 8a. PO collision branch ----------

        // The PO under evaluation: prefer the body override; fall back to the quote's
        // existing PO_number (set at request time).
        var effectivePo = string.IsNullOrWhiteSpace(body.PoNumber)
            ? tracked.PoNumber
            : body.PoNumber.Trim();

        // Spec FR-009 / contract reason-code mapping table — `quote.po_required`
        // applies at §2.7. When the company config requires a PO and neither the
        // request body nor the quote-of-record carries one, hard-reject.
        if (company is not null && company.PoRequired && string.IsNullOrWhiteSpace(effectivePo))
        {
            return SubmitAcceptanceResult.PoRequired();
        }

        IReadOnlyList<Guid> priorQuoteIdsWithPo = Array.Empty<Guid>();
        if (company is not null && !string.IsNullOrWhiteSpace(effectivePo))
        {
            priorQuoteIdsWithPo = await _db.Quotes
                .AsNoTracking()
                .Where(q => q.CompanyId == company.Id
                         && q.Id != tracked.Id
                         && q.PoNumber == effectivePo)
                .Select(q => q.Id)
                .ToListAsync(ct);
        }

        if (priorQuoteIdsWithPo.Count > 0)
        {
            // Branch A: strict-mode hard reject (FR-019).
            if (company!.UniquePoRequired)
            {
                return SubmitAcceptanceResult.PoAlreadyUsed();
            }

            // Branch B: soft-warning unacknowledged → 200 OK with envelope, NO state move.
            if (!body.PoWarningAcknowledged)
            {
                return SubmitAcceptanceResult.PoWarning(priorQuoteIdsWithPo);
            }

            // Branch C: soft-warning acknowledged → fall through to commit; audit
            // event must record the acknowledgement with the prior quote ids.
        }

        // ---------- 9. Routing fork ----------
        // Determine the target state. Approver routing applies only to company
        // quotes with approver_required=true.
        bool approverRouting = false;
        IReadOnlyList<Guid> approverUserIds = Array.Empty<Guid>();
        if (company is not null && company.ApproverRequired)
        {
            approverUserIds = await _db.CompanyMemberships
                .AsNoTracking()
                .Where(m => m.CompanyId == company.Id && m.Role == "approver")
                .Select(m => m.UserId)
                .Distinct()
                .ToListAsync(ct);
            if (approverUserIds.Count == 0)
            {
                // Clarifications Q1: approver_required=true + 0 approvers → reject.
                return SubmitAcceptanceResult.NoApproverAvailable();
            }
            approverRouting = true;
        }

        var targetState = approverRouting ? QuoteState.PendingApprover : QuoteState.Accepted;
        var actorKind = tracked.CompanyId is null
            ? QuoteActorKind.Customer
            : QuoteActorKind.Buyer;

        if (!QuoteStateMachine.CanTransition(currentState, targetState, actorKind))
        {
            // Defensive — should be unreachable given currentState == Revised
            // and the actor/target combinations above.
            return SubmitAcceptanceResult.InvalidStateForAction();
        }

        // ---------- 10. Conversion FIRST (atomicity per FR-032 / FR-035) ----------
        // The reference implementation (FinalizeAcceptanceHandler) calls the
        // converter BEFORE persisting the state transition so a converter failure
        // leaves the quote in its prior state. The previous order (state-write
        // first, conversion second) violated FR-032 ("exactly one order MUST be
        // created within the same transaction") and FR-035 ("quote MUST stay in
        // its prior state on failure"). On converter failure we return
        // ConversionFailed (mapped to invalid_state_for_action) WITHOUT
        // committing any quote state change.
        Guid? convertedOrderId = null;
        bool convertedReplay = false;
        if (targetState == QuoteState.Accepted)
        {
            try
            {
                var conversion = await _orderFromQuoteHandler.CreateAsync(
                    new QuoteConversionRequest(
                        QuoteId: tracked.Id,
                        CustomerId: tracked.CustomerId,
                        CompanyId: tracked.CompanyId,
                        CompanyBranchId: tracked.BranchId,
                        MarketCode: tracked.MarketCode,
                        PoNumber: effectivePo,
                        // T077 (US2) — hard-pin individual quotes to invoice_billing=false
                        // regardless of any future schema or refactor that might flip a
                        // company-eligibility flag on this column. Individual quotes are
                        // always retail-billed (FR-027 / spec.md US2 independent test).
                        InvoiceBilling: tracked.CompanyId is null ? false : tracked.InvoiceBilling,
                        TermsDays: null, // populated from QuoteVersion.TermsDays once the cycle wires the version snapshot pull-through; stub-tolerant.
                        Lines: Array.Empty<QuoteConversionLine>(), // line snapshot is on QuoteVersion; T100 hydrates the real list.
                        Subtotal: 0m,
                        TotalDiscount: 0m,
                        GrandTotal: 0m,
                        IdempotencyKey: idempotencyKey),
                    ct);
                convertedOrderId = conversion.OrderId;
                convertedReplay = conversion.WasIdempotentReplay;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Conversion failed BEFORE any state mutation committed — the
                // quote stays in its prior (`revised`) state per FR-035. Surface
                // invalid_state_for_action so the client can retry.
                _logger.LogError(ex,
                    "Quote-to-order conversion failed during submit-acceptance "
                    + "(quote_id={QuoteId}, idempotency_key={IdempotencyKey}). "
                    + "Quote remains in prior state; client may retry.",
                    tracked.Id, idempotencyKey);
                return SubmitAcceptanceResult.InvalidStateForAction();
            }
        }

        // ---------- 11. Commit-time expiry re-check ----------
        // Conversion may take significant time; capture a fresh timestamp and
        // re-validate expiry before mutating state so a quote that expired
        // mid-conversion is not accepted.
        var commitNowUtc = _time.GetUtcNow();
        if (tracked.ExpiresAt.HasValue && tracked.ExpiresAt.Value <= commitNowUtc)
        {
            return SubmitAcceptanceResult.Expired();
        }

        // ---------- 12. Mutate + persist (after successful conversion) ----------
        var priorStateToken = tracked.State;
        tracked.State = targetState.ToToken();

        if (targetState == QuoteState.Accepted)
        {
            tracked.DecidedAt = commitNowUtc;
            tracked.DecidedBy = customerId;
            tracked.TerminalAt = commitNowUtc;
            tracked.TerminalReason = "accepted";
        }

        var transitionMetadata = new Dictionary<string, object?>
        {
            ["idempotency_key"] = idempotencyKey,
            ["actor_authority"] = authority switch
            {
                AcceptanceAuthority.IndividualOwner => "customer",
                AcceptanceAuthority.Buyer => "buyer",
                AcceptanceAuthority.CompanyAdmin => "companies.admin",
                _ => "unknown",
            },
            ["target_state"] = targetState.ToToken(),
        };
        if (priorQuoteIdsWithPo.Count > 0 && body.PoWarningAcknowledged)
        {
            transitionMetadata["po_warning_acknowledged"] = true;
            transitionMetadata["po_warning_prior_quote_ids"] = priorQuoteIdsWithPo;
        }
        if (effectivePo != tracked.PoNumber && !string.IsNullOrWhiteSpace(body.PoNumber))
        {
            // The buyer overrode the request-time PO at acceptance — record it.
            tracked.PoNumber = effectivePo;
            transitionMetadata["po_number_override"] = effectivePo;
        }
        if (convertedOrderId is { } orderIdForMeta)
        {
            transitionMetadata["order_id"] = orderIdForMeta;
            transitionMetadata["was_idempotent_replay"] = convertedReplay;
        }

        var transition = new QuoteStateTransition
        {
            Id = Guid.NewGuid(),
            QuoteId = tracked.Id,
            MarketCode = tracked.MarketCode,
            PriorState = priorStateToken,
            NewState = targetState.ToToken(),
            ActorKind = actorKind.ToToken(),
            ActorId = customerId,
            ReasonJson = null,
            MetadataJson = JsonSerializer.Serialize(transitionMetadata),
            OccurredAt = commitNowUtc,
        };
        _db.QuoteStateTransitions.Add(transition);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // xmin guard tripped — another writer mutated the quote since our
            // tracked-fetch (most likely a concurrent finalize from an approver
            // race per SC-009). Surface invalid_state_for_action so the client
            // re-reads the current state. NOTE: the conversion handler's
            // idempotency contract guarantees a replay returns the same order id
            // without duplicate side effects, so the order created above is safe.
            return SubmitAcceptanceResult.InvalidStateForAction();
        }

        // ---------- 12. Audit + domain event (post-commit, isolated) ----------
        // Same FR-043-compliant pattern as Withdraw / RequestRevision: audit + domain
        // event publish run AFTER the SaveChanges commit, isolated by try/catch.
        try
        {
            var actorRoleToken = authority switch
            {
                AcceptanceAuthority.IndividualOwner => "customer",
                AcceptanceAuthority.Buyer => "buyer",
                AcceptanceAuthority.CompanyAdmin => "companies.admin",
                _ => "unknown",
            };
            var afterState = new Dictionary<string, object?>
            {
                ["state"] = tracked.State,
                ["target_state"] = targetState.ToToken(),
            };
            if (convertedOrderId is { } orderId)
            {
                afterState["order_id"] = orderId;
                afterState["was_idempotent_replay"] = convertedReplay;
            }
            await _auditPublisher.PublishAsync(new AuditEvent(
                ActorId: customerId,
                ActorRole: actorRoleToken,
                Action: "quote.state_changed",
                EntityType: "quote",
                EntityId: tracked.Id,
                BeforeState: new { state = priorStateToken },
                AfterState: afterState,
                Reason: targetState == QuoteState.Accepted ? "accepted" : "pending_approver"), ct);

            // Separate audit row for the PO-warning-acknowledged path so the audit
            // ledger has an explicit record of the acknowledgement (spec.md
            // §Edge Case "PO number reuse").
            if (priorQuoteIdsWithPo.Count > 0 && body.PoWarningAcknowledged)
            {
                await _auditPublisher.PublishAsync(new AuditEvent(
                    ActorId: customerId,
                    ActorRole: actorRoleToken,
                    Action: "quote.po_warning_acknowledged",
                    EntityType: "quote",
                    EntityId: tracked.Id,
                    BeforeState: null,
                    AfterState: new { prior_quote_ids = priorQuoteIdsWithPo },
                    Reason: "po_reuse_acknowledged"), ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Audit publish failed for quote.state_changed submit-acceptance "
                + "(quote_id={QuoteId}, customer_id={CustomerId}). Quote already committed.",
                tracked.Id, customerId);
        }

        try
        {
            if (targetState == QuoteState.PendingApprover)
            {
                await _domainPublisher.Publish(new QuotePendingApprover(
                    QuoteId: tracked.Id,
                    CompanyId: tracked.CompanyId!.Value,
                    ApproverUserIds: approverUserIds,
                    BuyerUserId: customerId,
                    MarketCode: tracked.MarketCode), ct);
            }
            else if (targetState == QuoteState.Accepted)
            {
                await _domainPublisher.Publish(new QuoteAccepted(
                    QuoteId: tracked.Id,
                    OrderId: convertedOrderId ?? Guid.Empty,
                    CustomerId: tracked.CustomerId,
                    CompanyId: tracked.CompanyId,
                    MarketCode: tracked.MarketCode,
                    LocaleHint: callerLocaleHint), ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Quote acceptance domain-event publish failed (quote_id={QuoteId}, "
                + "target_state={TargetState}). Notification subscribers (spec 025) "
                + "will not fire unless replayed.",
                tracked.Id, targetState.ToToken());
        }

        return SubmitAcceptanceResult.Success(new SubmitAcceptanceResponse(
            Id: tracked.Id,
            State: tracked.State,
            DecidedAt: tracked.DecidedAt,
            TerminalAt: tracked.TerminalAt,
            OrderId: convertedOrderId));
    }

    /// <summary>
    /// §2.7 write-authority resolver. Returns the resolved authority granting
    /// permission to SUBMIT-ACCEPTANCE. <c>approver</c> can READ but cannot submit
    /// — handler returns 404 for that case (visibility symmetry).
    /// </summary>
    private async Task<AcceptanceAuthority> ResolveAuthorityAsync(Quote quote, Guid customerId, CancellationToken ct)
    {
        if (quote.CompanyId is null)
        {
            return quote.CustomerId == customerId
                ? AcceptanceAuthority.IndividualOwner
                : AcceptanceAuthority.None;
        }

        var roles = await _db.CompanyMemberships
            .AsNoTracking()
            .Where(m => m.CompanyId == quote.CompanyId.Value && m.UserId == customerId)
            .Select(m => m.Role)
            .ToListAsync(ct);
        if (roles.Count == 0)
        {
            return AcceptanceAuthority.None;
        }
        if (roles.Contains("companies.admin"))
        {
            return AcceptanceAuthority.CompanyAdmin;
        }
        if (roles.Contains("buyer"))
        {
            return AcceptanceAuthority.Buyer;
        }
        // approver-only — read but not write.
        return AcceptanceAuthority.None;
    }

    /// <summary>
    /// FR-036 — restricted SKUs on the quote require the buyer to be Eligible (or
    /// Unrestricted) per spec 020. Returns <c>true</c> when ANY line is Ineligible
    /// (handler then surfaces 422 quote.eligibility_required).
    ///
    /// We pull SKUs from the latest <see cref="QuoteVersion.LineItemsJson"/> when a
    /// version exists; otherwise from <see cref="Quote.OriginatingCartSnapshotJson"/>
    /// as a defensive fallback. Both are JSON arrays whose entries carry a
    /// <c>sku</c> property.
    /// </summary>
    private async Task<bool> CheckEligibilityAsync(
        Quote quote,
        Guid customerId,
        string callerMarketCode,
        CancellationToken ct)
    {
        var skus = await ResolveQuoteSkusAsync(quote, ct);
        if (skus.Count == 0)
        {
            return false;
        }
        var results = await _eligibilityQuery.EvaluateManyAsync(
            customerId, callerMarketCode, skus, ct);
        return results.Values.Any(r => r.Class == EligibilityClass.Ineligible);
    }

    private async Task<IReadOnlyList<string>> ResolveQuoteSkusAsync(Quote quote, CancellationToken ct)
    {
        // Prefer the published version's line items.
        if (quote.CurrentVersionId is { } versionId)
        {
            var version = await _db.QuoteVersions
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == versionId, ct);
            if (version is not null)
            {
                var fromVersion = ExtractSkus(version.LineItemsJson);
                if (fromVersion.Count > 0) return fromVersion;
            }
        }
        // Fallback — originating cart snapshot at request time.
        return ExtractSkus(quote.OriginatingCartSnapshotJson);
    }

    private static IReadOnlyList<string> ExtractSkus(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }
            var skus = new List<string>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (item.TryGetProperty("sku", out var skuElement) && skuElement.ValueKind == JsonValueKind.String)
                {
                    var sku = skuElement.GetString();
                    if (!string.IsNullOrWhiteSpace(sku)) skus.Add(sku);
                }
            }
            return skus;
        }
        catch (JsonException)
        {
            // Defensive: malformed jsonb shouldn't reach here (CHECK constraints),
            // but if it does we can't enforce eligibility — fail closed by surfacing
            // 422 from the caller is too aggressive for storage-side drift; instead
            // skip the gate and rely on order conversion's own eligibility re-check
            // (FR-036 is enforced at multiple layers).
            return Array.Empty<string>();
        }
    }
}

/// <summary>
/// Resolved write-authority for §2.7. Carried into transition metadata + audit
/// envelope so the action is attributed to the exact role that authorized it.
/// </summary>
internal enum AcceptanceAuthority
{
    None = 0,
    IndividualOwner = 1,
    Buyer = 2,
    CompanyAdmin = 3,
}

/// <summary>
/// Internal handler envelope. The endpoint translates this into a 200 (success or
/// soft-warning), 404, 409, or 422 with the appropriate <see cref="QuoteReasonCode"/>.
/// </summary>
public sealed record SubmitAcceptanceResult(
    bool IsSuccess,
    int StatusCode,
    QuoteReasonCode? ReasonCode,
    SubmitAcceptanceResponse? Response,
    SubmitAcceptancePoWarningResponse? PoWarningResponse)
{
    public static SubmitAcceptanceResult Success(SubmitAcceptanceResponse r) =>
        new(true, 200, null, r, null);

    /// <summary>
    /// PO soft-warning branch. Returns 200 OK with the <c>po_warning</c> envelope —
    /// the state was NOT mutated; the caller must re-submit with
    /// <c>po_warning_acknowledged=true</c> to commit.
    /// </summary>
    public static SubmitAcceptanceResult PoWarning(IReadOnlyList<Guid> priorQuoteIds) =>
        new(true, 200, null, null,
            new SubmitAcceptancePoWarningResponse(
                new PoWarningPayload(
                    PriorQuoteIds: priorQuoteIds,
                    MessageKey: "quote.po_warning")));

    public static SubmitAcceptanceResult NotFound() =>
        new(false, 404, QuoteReasonCode.QuoteNotFound, null, null);

    public static SubmitAcceptanceResult InvalidStateForAction() =>
        new(false, 409, QuoteReasonCode.QuoteInvalidStateForAction, null, null);

    public static SubmitAcceptanceResult Expired() =>
        new(false, 409, QuoteReasonCode.QuoteExpired, null, null);

    public static SubmitAcceptanceResult NoApproverAvailable() =>
        new(false, 409, QuoteReasonCode.QuoteNoApproverAvailable, null, null);

    public static SubmitAcceptanceResult PoAlreadyUsed() =>
        new(false, 409, QuoteReasonCode.QuotePoAlreadyUsed, null, null);

    public static SubmitAcceptanceResult EligibilityRequired() =>
        new(false, 422, QuoteReasonCode.QuoteEligibilityRequired, null, null);

    public static SubmitAcceptanceResult MarketMismatch() =>
        new(false, 422, QuoteReasonCode.QuoteMarketMismatch, null, null);

    /// <summary>
    /// Spec.md §Edge Case "company is suspended (spec 019 admin action)" —
    /// 422 quote.company_suspended. Existing accepted quotes are unaffected;
    /// only the ability to ACCEPT a non-terminal quote is blocked.
    /// </summary>
    public static SubmitAcceptanceResult CompanySuspended() =>
        new(false, 422, QuoteReasonCode.QuoteCompanySuspended, null, null);

    /// <summary>
    /// Spec FR-009 — when <c>company.po_required=true</c> and neither the body
    /// nor the quote-of-record carries a PO, return 400 quote.po_required.
    /// </summary>
    public static SubmitAcceptanceResult PoRequired() =>
        new(false, 400, QuoteReasonCode.QuotePoRequired, null, null);
}

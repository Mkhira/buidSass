using BackendApi.Modules.Cart.Customer.Common;
using BackendApi.Modules.Cart.Entities;
using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Cart.Primitives;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Inventory.Persistence;
using BackendApi.Modules.Pricing.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Cart.Customer.Merge;

public sealed record MergeRequest(string MarketCode);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapMergeEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/merge", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    internal static async Task<MergeOutcome> ExecuteAsync(
        Guid accountId,
        string marketCodeRaw,
        string? suppliedToken,
        CartDbContext db,
        CatalogDbContext catalogDb,
        InventoryDbContext inventoryDb,
        PricingDbContext pricingDb,
        CartResolver resolver,
        CartMerger merger,
        CartViewBuilder viewBuilder,
        CartInventoryOrchestrator inventoryOrchestrator,
        CustomerContextResolver customerContextResolver,
        ILogger logger,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        var marketCode = marketCodeRaw.Trim().ToLowerInvariant();

        // Anonymous-side lookup FIRST so we don't accidentally create an empty auth cart when
        // there's nothing to merge into.
        Entities.Cart? anonCart = null;
        if (!string.IsNullOrWhiteSpace(suppliedToken))
        {
            anonCart = await resolver.LookupAsync(db, accountId: null, suppliedToken, marketCode, nowUtc, ct);
        }

        // Always resolve (or create) the auth cart — this is the merge destination.
        var authResolved = await resolver.ResolveOrCreateAsync(db, accountId, suppliedToken: null, marketCode, nowUtc, ct);
        var authCart = authResolved.Cart;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (CustomerCartResponseFactory.IsConcurrencyConflict(ex))
        {
            db.ChangeTracker.Clear();
            return new MergeOutcome(null, null, ConflictDetail: "Cart creation race during merge.");
        }

        var notices = new List<CartMerger.MergeNotice>();

        if (anonCart is not null && anonCart.Id != authCart.Id)
        {
            var anonLines = await db.CartLines.AsNoTracking().Where(l => l.CartId == anonCart.Id).ToListAsync(ct);
            var authLines = await db.CartLines.Where(l => l.CartId == authCart.Id).ToListAsync(ct);

            // Pull max_per_order + restriction snapshot from catalog for every participating
            // product. CR review PR #30 round 3: merged CartLines must carry the restricted
            // flag so the eligibility evaluator + checkout restriction gate see them; the
            // previous merge wrote `Restricted = false` regardless of the product.
            var productIds = anonLines.Select(l => l.ProductId).Concat(authLines.Select(l => l.ProductId)).Distinct().ToArray();
            var productMeta = await catalogDb.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .Select(p => new { p.Id, p.MaxPerOrder, p.Restricted, p.RestrictionReasonCode })
                .ToListAsync(ct);
            var productCaps = productMeta.ToDictionary(x => x.Id, x => x.MaxPerOrder);
            var productRestriction = productMeta.ToDictionary(
                x => x.Id, x => (x.Restricted, x.RestrictionReasonCode));

            var mergeAnon = anonLines.Select(l => new CartMerger.MergeLine(
                l.ProductId, l.Qty, productCaps.GetValueOrDefault(l.ProductId, 0))).ToList();
            var mergeAuth = authLines.Select(l => new CartMerger.MergeLine(
                l.ProductId, l.Qty, productCaps.GetValueOrDefault(l.ProductId, 0))).ToList();
            var merged = merger.Merge(mergeAnon, mergeAuth);
            notices.AddRange(merged.Notices);

            // Track every reservation side-effect so that if the final cart SaveChanges hits a
            // 23505 we can compensate — without this, inventoryDb is already committed while the
            // cart state is rolled back, leaving phantom active reservations (CR #5).
            var createdReservations = new List<Guid>();
            // CR review on PR #30: tuple now carries the source cart id so a 23505 rollback
            // re-reserves each released hold against its ORIGINAL cart, not always authCart.
            var releasedReservations = new List<(Guid ProductId, int Qty, Guid SourceCartId)>();
            var anyLineSkipped = false;

            foreach (var mergedLine in merged.Lines)
            {
                var existing = authLines.SingleOrDefault(l => l.ProductId == mergedLine.ProductId);
                var anonForProduct = anonLines.SingleOrDefault(l => l.ProductId == mergedLine.ProductId);

                // CR #4: release BOTH auth + anon reservations for this product before we try to
                // reserve the merged qty. Otherwise the anon's hold is still counted against ATS,
                // so `anon:1 + auth:1 → merged:2` fails on stock that's already reserved by the
                // source cart.
                Guid? priorAuthReservation = existing?.ReservationId;
                Guid? priorAnonReservation = anonForProduct?.ReservationId;
                if (priorAuthReservation is { } prAuth)
                {
                    await inventoryOrchestrator.TryReleaseAsync(
                        inventoryDb, prAuth, accountId, "cart.merge.qty_updated", ct);
                    releasedReservations.Add((mergedLine.ProductId, existing!.Qty, authCart.Id));
                }
                if (priorAnonReservation is { } prAnon)
                {
                    await inventoryOrchestrator.TryReleaseAsync(
                        inventoryDb, prAnon, accountId, "cart.merge.anon_claimed", ct);
                    releasedReservations.Add((mergedLine.ProductId, anonForProduct!.Qty, anonCart.Id));
                }

                var reservation = await inventoryOrchestrator.TryReserveAsync(
                    inventoryDb, catalogDb, mergedLine.ProductId, mergedLine.Qty, marketCode, accountId, authCart.Id, nowUtc, ct);
                if (!reservation.IsSuccess)
                {
                    anyLineSkipped = true;
                    // Merge failed for this product — do our best to restore the auth side so the
                    // pre-merge state stays consistent. The anon line stays on its original cart
                    // for this product (we'll skip its archival below).
                    if (existing is not null && priorAuthReservation is not null)
                    {
                        var restore = await inventoryOrchestrator.TryReserveAsync(
                            inventoryDb, catalogDb, mergedLine.ProductId, existing.Qty, marketCode, accountId, authCart.Id, nowUtc, ct);
                        if (restore.IsSuccess)
                        {
                            existing.ReservationId = restore.ReservationId;
                            existing.UpdatedAt = nowUtc;
                            if (restore.ReservationId is { } rid) createdReservations.Add(rid);
                        }
                        else
                        {
                            existing.ReservationId = null;
                            existing.StockChanged = true;
                            existing.UpdatedAt = nowUtc;
                        }
                    }
                    logger.LogWarning(
                        "cart.merge.reservation_failed productId={ProductId} qty={Qty} reason={Reason}",
                        mergedLine.ProductId, mergedLine.Qty, reservation.ReasonCode);
                    notices.Add(new CartMerger.MergeNotice(
                        mergedLine.ProductId,
                        reservation.ReasonCode ?? "cart.merge.reservation_failed",
                        mergedLine.Qty,
                        existing?.Qty ?? 0));
                    continue;
                }

                if (reservation.ReservationId is { } newId) createdReservations.Add(newId);

                var (productRestricted, productRestrictionCode) =
                    productRestriction.TryGetValue(mergedLine.ProductId, out var rs)
                        ? rs : (false, (string?)null);
                if (existing is null)
                {
                    db.CartLines.Add(new CartLine
                    {
                        Id = Guid.NewGuid(),
                        CartId = authCart.Id,
                        MarketCode = marketCode,
                        ProductId = mergedLine.ProductId,
                        Qty = mergedLine.Qty,
                        ReservationId = reservation.ReservationId,
                        Restricted = productRestricted,
                        RestrictionReasonCode = productRestrictionCode,
                        AddedAt = nowUtc,
                        UpdatedAt = nowUtc,
                    });
                }
                else
                {
                    existing.Qty = mergedLine.Qty;
                    existing.ReservationId = reservation.ReservationId;
                    existing.Restricted = productRestricted;
                    existing.RestrictionReasonCode = productRestrictionCode;
                    existing.UpdatedAt = nowUtc;
                }
            }

            // CR review on PR #30: only mark anon as merged when EVERY line was successfully
            // moved. If any product was left behind in the reservation-failure path, the anon
            // cart stays Active so the customer can still reach those items — otherwise the
            // skipped lines would become unreachable.
            if (!anyLineSkipped)
            {
                CartStatuses.TryTransition(anonCart, CartStatuses.Merged, "merged", nowUtc);
            }
            else
            {
                logger.LogInformation(
                    "cart.merge.partial_anon_retained anonCartId={AnonCartId} authCartId={AuthCartId}",
                    anonCart.Id, authCart.Id);
            }

            // Anon coupon is adopted only if auth side had none (R2) AND the coupon is still
            // valid for the authenticated account's market + restriction set (spec edge case 5).
            // If the coupon no longer applies we drop it silently and surface a merge notice so
            // the client can explain the gap — doing nothing would leave the auth cart with a
            // stale coupon code that Pricing would reject on next preview.
            if (string.IsNullOrEmpty(authCart.CouponCode) && !string.IsNullOrEmpty(anonCart.CouponCode))
            {
                var carryInvalid = await IsCouponInvalidForAsync(
                    pricingDb, db, anonCart.CouponCode!, authCart.Id, marketCode, nowUtc, ct);
                if (carryInvalid.Invalid)
                {
                    notices.Add(new CartMerger.MergeNotice(
                        Guid.Empty,
                        carryInvalid.ReasonCode ?? "cart.coupon.invalid",
                        RequestedQty: 0,
                        AppliedQty: 0));
                    logger.LogInformation(
                        "cart.merge.coupon_dropped authCartId={AuthCartId} code={Code} reason={Reason}",
                        authCart.Id, anonCart.CouponCode, carryInvalid.ReasonCode);
                }
                else
                {
                    authCart.CouponCode = anonCart.CouponCode;
                }
            }

            authCart.LastTouchedAt = nowUtc;
            authCart.UpdatedAt = nowUtc;

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (CustomerCartResponseFactory.IsConcurrencyConflict(ex))
            {
                // CR #5: compensate inventory side effects before surfacing 409. Released holds
                // need to be re-reserved (best effort); newly-created reservations we drove must
                // be released so they don't leak once the cart write is rolled back.
                await CompensateAsync(inventoryDb, catalogDb, inventoryOrchestrator,
                    createdReservations, releasedReservations,
                    marketCode, accountId, nowUtc, logger, ct);
                db.ChangeTracker.Clear();
                return new MergeOutcome(null, null, ConflictDetail: "Cart line insert race during merge.");
            }
        }
        else
        {
            // No merge to perform — still touch the auth cart timestamp.
            authCart.LastTouchedAt = nowUtc;
            authCart.UpdatedAt = nowUtc;
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (CustomerCartResponseFactory.IsConcurrencyConflict(ex))
            {
                db.ChangeTracker.Clear();
                return new MergeOutcome(null, null, ConflictDetail: "Cart touch race during merge.");
            }
        }

        var ctxInfo = await customerContextResolver.ResolveAsync(accountId, ct);
        var lines = await db.CartLines.AsNoTracking().Where(l => l.CartId == authCart.Id).OrderBy(l => l.AddedAt).ToListAsync(ct);
        var saved = await db.CartSavedItems.AsNoTracking().Where(s => s.CartId == authCart.Id).ToListAsync(ct);
        var b2b = await db.CartB2BMetadata.AsNoTracking().SingleOrDefaultAsync(b => b.CartId == authCart.Id, ct);
        var view = await viewBuilder.BuildAsync(authCart, lines, saved, b2b, catalogDb, ctxInfo.VerifiedForRestriction, ctxInfo.IsB2B, nowUtc, ct);

        logger.LogInformation(
            "cart.merged authCartId={AuthCartId} anonCartId={AnonCartId} notices={NoticeCount}",
            authCart.Id, anonCart?.Id, notices.Count);

        return new MergeOutcome(view, notices, null);
    }

    /// <summary>
    /// Best-effort compensation after the merge's cart-side write lost a 23505 race. Released
    /// holds are re-reserved (so the pre-merge state is closer to restored); newly-created
    /// reservations are released (so they don't leak once the cart write is rolled back).
    /// Failures inside compensation are logged, not rethrown — surfacing 409 to the caller is
    /// the primary concern.
    /// </summary>
    private static async Task CompensateAsync(
        InventoryDbContext inventoryDb,
        CatalogDbContext catalogDb,
        CartInventoryOrchestrator inventoryOrchestrator,
        List<Guid> createdReservations,
        List<(Guid ProductId, int Qty, Guid SourceCartId)> releasedReservations,
        string marketCode,
        Guid accountId,
        DateTimeOffset nowUtc,
        ILogger logger,
        CancellationToken ct)
    {
        foreach (var rid in createdReservations)
        {
            try
            {
                await inventoryOrchestrator.TryReleaseAsync(
                    inventoryDb, rid, accountId, "cart.merge.concurrency_rollback", ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "cart.merge.compensation.release_failed reservationId={ReservationId}", rid);
            }
        }
        // CR review on PR #30: re-reserve each released hold against ITS ORIGINAL cart, not the
        // auth cart. Otherwise an anon-side hold would be restored against the wrong cart and
        // the original anon line would remain unreserved post-rollback.
        foreach (var (productId, qty, sourceCartId) in releasedReservations)
        {
            try
            {
                await inventoryOrchestrator.TryReserveAsync(
                    inventoryDb, catalogDb, productId, qty, marketCode, accountId, sourceCartId, nowUtc, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "cart.merge.compensation.reserve_failed productId={ProductId} qty={Qty} sourceCartId={SourceCartId}",
                    productId, qty, sourceCartId);
            }
        }
    }

    /// <summary>
    /// Re-validates an anonymous-side coupon against the authenticated cart's market +
    /// restriction set before the merge adopts it (spec edge case 5). Mirrors the gate chain in
    /// ApplyCoupon so a coupon that would be rejected by a direct POST /coupon is also rejected
    /// on carry-over. Returns the rejection reason code (or null on pass).
    /// </summary>
    internal static async Task<(bool Invalid, string? ReasonCode)> IsCouponInvalidForAsync(
        PricingDbContext pricingDb,
        CartDbContext cartDb,
        string code,
        Guid cartId,
        string marketCode,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        var normalized = code.Trim().ToUpperInvariant();
        var coupon = await pricingDb.Coupons.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Code == normalized && c.DeletedAt == null, ct);
        if (coupon is null || !coupon.IsActive) return (true, "cart.coupon.invalid");
        // CR review on PR #30 round 2: distinct reason for "starts later" so the client UI
        // can render "valid from {date}" instead of a misleading "expired" message.
        if (coupon.ValidFrom is { } vf && nowUtc < vf) return (true, "cart.coupon.not_yet_valid");
        if (coupon.ValidTo is { } vt && nowUtc > vt) return (true, "cart.coupon.expired");
        if (coupon.MarketCodes.Length > 0
            && !coupon.MarketCodes.Any(m => string.Equals(m, marketCode, StringComparison.OrdinalIgnoreCase)))
        {
            return (true, "cart.coupon.invalid");
        }
        if (coupon.OverallLimit is { } limit && coupon.UsedCount >= limit)
        {
            return (true, "cart.coupon.limit_reached");
        }
        if (coupon.ExcludesRestricted)
        {
            // CR review on PR #30 round 2: also scan the EF change tracker so a merge that
            // just brought restricted lines into the auth cart (still uncommitted) is caught
            // BEFORE we adopt the coupon. Persisted-only check missed mid-merge state.
            var hasRestrictedTracked = cartDb.ChangeTracker
                .Entries<BackendApi.Modules.Cart.Entities.CartLine>()
                .Any(e => e.Entity.CartId == cartId
                    && e.Entity.Restricted
                    && e.State != Microsoft.EntityFrameworkCore.EntityState.Deleted);
            var hasRestricted = hasRestrictedTracked
                || await cartDb.CartLines.AsNoTracking()
                    .Where(l => l.CartId == cartId).AnyAsync(l => l.Restricted, ct);
            if (hasRestricted) return (true, "cart.coupon.excludes_restricted");
        }
        return (false, null);
    }

    private static async Task<IResult> HandleAsync(
        MergeRequest request,
        HttpContext context,
        CartDbContext db,
        CatalogDbContext catalogDb,
        InventoryDbContext inventoryDb,
        PricingDbContext pricingDb,
        CartResolver resolver,
        CartMerger merger,
        CartViewBuilder viewBuilder,
        CartInventoryOrchestrator inventoryOrchestrator,
        CustomerContextResolver customerContextResolver,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.MarketCode))
        {
            return CustomerCartResponseFactory.Problem(context, 400, "cart.market_required", "Market required", "");
        }
        var accountId = CustomerCartResponseFactory.ResolveAccountId(context);
        if (accountId is null)
        {
            return CustomerCartResponseFactory.Problem(context, 401, "cart.auth_required", "Auth required", "");
        }

        var suppliedToken = GetCart.Endpoint.ResolveToken(context);
        var logger = loggerFactory.CreateLogger("Cart.Merge");

        var outcome = await ExecuteAsync(
            accountId.Value, request.MarketCode, suppliedToken,
            db, catalogDb, inventoryDb, pricingDb, resolver, merger, viewBuilder,
            inventoryOrchestrator, customerContextResolver, logger,
            DateTimeOffset.UtcNow, ct);

        if (outcome.ConflictDetail is not null)
        {
            return CustomerCartResponseFactory.ConcurrencyConflict(context, outcome.ConflictDetail);
        }

        // Clear the anon cookie — its cart is now archived/merged.
        context.Response.Cookies.Delete("cart_token");

        var view = outcome.View!;
        return Results.Ok(new
        {
            id = view.Id,
            marketCode = view.MarketCode,
            status = view.Status,
            lines = view.Lines,
            savedItems = view.SavedItems,
            couponCode = view.CouponCode,
            pricing = view.Pricing,
            checkoutEligibility = view.CheckoutEligibility,
            b2b = view.B2b,
            mergeNotices = outcome.Notices!.Select(n => new
            {
                productId = n.ProductId,
                reasonCode = n.ReasonCode,
                requestedQty = n.RequestedQty,
                appliedQty = n.AppliedQty,
            }).ToArray(),
        });
    }

    public sealed record MergeOutcome(CartView? View, IReadOnlyList<CartMerger.MergeNotice>? Notices, string? ConflictDetail);
}

using BackendApi.Modules.Cart.Customer.Common;
using BackendApi.Modules.Cart.Entities;
using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Cart.Primitives;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Inventory.Persistence;
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

            // Pull max_per_order from catalog for every participating product.
            var productIds = anonLines.Select(l => l.ProductId).Concat(authLines.Select(l => l.ProductId)).Distinct().ToArray();
            var productCaps = await catalogDb.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .Select(p => new { p.Id, p.MaxPerOrder })
                .ToDictionaryAsync(x => x.Id, x => x.MaxPerOrder, ct);

            var mergeAnon = anonLines.Select(l => new CartMerger.MergeLine(
                l.ProductId, l.Qty, productCaps.GetValueOrDefault(l.ProductId, 0))).ToList();
            var mergeAuth = authLines.Select(l => new CartMerger.MergeLine(
                l.ProductId, l.Qty, productCaps.GetValueOrDefault(l.ProductId, 0))).ToList();
            var merged = merger.Merge(mergeAnon, mergeAuth);
            notices.AddRange(merged.Notices);

            foreach (var mergedLine in merged.Lines)
            {
                var existing = authLines.SingleOrDefault(l => l.ProductId == mergedLine.ProductId);

                // Release auth's prior reservation BEFORE reserving the new qty (L8). This keeps
                // stock.Reserved accurate instead of transiently double-counting.
                Guid? priorReservationId = existing?.ReservationId;
                if (priorReservationId is { } prId)
                {
                    await inventoryOrchestrator.TryReleaseAsync(
                        inventoryDb, prId, accountId, "cart.merge.qty_updated", ct);
                }

                var reservation = await inventoryOrchestrator.TryReserveAsync(
                    inventoryDb, catalogDb, mergedLine.ProductId, mergedLine.Qty, marketCode, accountId, authCart.Id, nowUtc, ct);
                if (!reservation.IsSuccess)
                {
                    // Restore the auth side's prior reservation so pre-merge state is preserved.
                    if (existing is not null && priorReservationId is not null)
                    {
                        var restore = await inventoryOrchestrator.TryReserveAsync(
                            inventoryDb, catalogDb, mergedLine.ProductId, existing.Qty, marketCode, accountId, authCart.Id, nowUtc, ct);
                        if (restore.IsSuccess)
                        {
                            existing.ReservationId = restore.ReservationId;
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

                if (existing is null)
                {
                    db.CartLines.Add(new CartLine
                    {
                        Id = Guid.NewGuid(),
                        CartId = authCart.Id,
                        ProductId = mergedLine.ProductId,
                        Qty = mergedLine.Qty,
                        ReservationId = reservation.ReservationId,
                        AddedAt = nowUtc,
                        UpdatedAt = nowUtc,
                    });
                }
                else
                {
                    existing.Qty = mergedLine.Qty;
                    existing.ReservationId = reservation.ReservationId;
                    existing.UpdatedAt = nowUtc;
                }
            }

            // Release any anon-side reservations still dangling (anon lines that weren't
            // re-reserved on the auth cart because of failure).
            foreach (var anonLine in anonLines.Where(l => l.ReservationId.HasValue))
            {
                await inventoryOrchestrator.TryReleaseAsync(
                    inventoryDb, anonLine.ReservationId!.Value, accountId, "cart.merge.anon_archived", ct);
            }

            anonCart.Status = "merged";
            anonCart.ArchivedAt = nowUtc;
            anonCart.ArchivedReason = "merged";
            anonCart.UpdatedAt = nowUtc;

            // Anon coupon is adopted only if auth side had none (R2).
            if (string.IsNullOrEmpty(authCart.CouponCode) && !string.IsNullOrEmpty(anonCart.CouponCode))
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
            db.ChangeTracker.Clear();
            return new MergeOutcome(null, null, ConflictDetail: "Cart line insert race during merge.");
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

    private static async Task<IResult> HandleAsync(
        MergeRequest request,
        HttpContext context,
        CartDbContext db,
        CatalogDbContext catalogDb,
        InventoryDbContext inventoryDb,
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
            db, catalogDb, inventoryDb, resolver, merger, viewBuilder,
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

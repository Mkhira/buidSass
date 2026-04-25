using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Cart.Primitives;
using BackendApi.Modules.Checkout.Customer.Common;
using BackendApi.Modules.Checkout.Entities;
using BackendApi.Modules.Checkout.Persistence;
using BackendApi.Modules.Checkout.Primitives;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BackendApi.Modules.Checkout.Customer.StartSession;

public sealed record StartSessionRequest(Guid CartId, string MarketCode);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapStartSessionEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/sessions", HandleAsync);
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        StartSessionRequest request,
        HttpContext context,
        CheckoutDbContext db,
        CartDbContext cartDb,
        CartTokenProvider cartTokenProvider,
        IOptions<CheckoutOptions> options,
        CheckoutAuditEmitter audit,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.MarketCode))
        {
            return CustomerCheckoutResponseFactory.Problem(context, 400, "checkout.market_required", "Market required", "");
        }
        if (request.CartId == Guid.Empty)
        {
            return CustomerCheckoutResponseFactory.Problem(context, 400, "checkout.cart_required", "Cart required", "");
        }

        var marketCode = request.MarketCode.Trim().ToLowerInvariant();
        var accountId = await CustomerCheckoutResponseFactory.TryResolveAuthenticatedAccountAsync(context);
        var suppliedToken = ResolveCartToken(context);

        var cart = await cartDb.Carts.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == request.CartId, ct);
        if (cart is null || !string.Equals(cart.Status, CartStatuses.Active, StringComparison.OrdinalIgnoreCase))
        {
            return CustomerCheckoutResponseFactory.Problem(context, 404, "checkout.cart.not_found", "Cart not found", "");
        }
        if (!string.Equals(cart.MarketCode, marketCode, StringComparison.OrdinalIgnoreCase))
        {
            return CustomerCheckoutResponseFactory.Problem(context, 409, "checkout.market_mismatch", "Cart market mismatch", "");
        }
        // Cart ownership: either the caller is the authed owner, or supplied the anon token.
        if (cart.AccountId is { } ownerId)
        {
            if (accountId != ownerId)
            {
                return CustomerCheckoutResponseFactory.Problem(context, 403, "checkout.cart.not_owned", "Cart is owned by another account", "");
            }
        }
        else if (cart.CartTokenHash is { } hash)
        {
            if (string.IsNullOrWhiteSpace(suppliedToken)
                || !cartTokenProvider.TryDecode(suppliedToken, DateTimeOffset.UtcNow, out var suppliedHash)
                || !hash.AsSpan().SequenceEqual(suppliedHash))
            {
                return CustomerCheckoutResponseFactory.Problem(context, 403, "checkout.cart.not_owned", "Cart token mismatch", "");
            }
        }

        var anyLine = await cartDb.CartLines.AsNoTracking().AnyAsync(l => l.CartId == cart.Id, ct);
        if (!anyLine)
        {
            return CustomerCheckoutResponseFactory.Problem(context, 409, "checkout.cart.empty", "Cart is empty", "");
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var ttl = TimeSpan.FromMinutes(options.Value.SessionTtlMinutes);

        var session = new CheckoutSession
        {
            Id = Guid.NewGuid(),
            CartId = cart.Id,
            AccountId = accountId,
            CartTokenHash = cart.CartTokenHash,
            MarketCode = marketCode,
            State = CheckoutStates.Init,
            CouponCode = cart.CouponCode,
            LastTouchedAt = nowUtc,
            ExpiresAt = nowUtc.Add(ttl),
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };
        db.Sessions.Add(session);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (CustomerCheckoutResponseFactory.IsConcurrencyConflict(ex))
        {
            return CustomerCheckoutResponseFactory.Problem(context, 409, "checkout.concurrency_conflict", "Concurrency conflict", "Retry.");
        }

        // FR-015: every state transition writes an audit row. Guest sessions (anon cart token)
        // are still customer-initiated — `system` role is reserved for worker / webhook actors.
        await audit.EmitSessionTransitionAsync(
            session, CheckoutAuditActions.SessionCreated, accountId,
            CheckoutAuditEmitter.CustomerRole,
            reason: $"market={marketCode}", ct);

        return Results.Ok(new
        {
            sessionId = session.Id,
            state = session.State,
            expiresAt = session.ExpiresAt,
        });
    }

    internal static string? ResolveCartToken(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Cart-Token", out var header) && !string.IsNullOrWhiteSpace(header))
        {
            return header.ToString();
        }
        if (context.Request.Cookies.TryGetValue("cart_token", out var cookie) && !string.IsNullOrWhiteSpace(cookie))
        {
            return cookie;
        }
        return null;
    }
}

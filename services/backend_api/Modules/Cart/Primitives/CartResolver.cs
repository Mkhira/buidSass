using BackendApi.Modules.Cart.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cart.Primitives;

/// <summary>
/// Resolves the "current" active cart for the caller. Lookup strategy (R1 + spec edge case 6):
///   * If the caller is authenticated → one active cart per (account, market); create on demand.
///   * Else if an X-Cart-Token header or cart_token cookie decodes validly → lookup by hash.
///   * Else → issue a new token + new anonymous cart.
/// </summary>
public sealed class CartResolver(CartTokenProvider tokenProvider)
{
    private readonly CartTokenProvider _tokenProvider = tokenProvider;

    public sealed record ResolvedCart(Entities.Cart Cart, string? IssuedToken);

    public async Task<ResolvedCart> ResolveOrCreateAsync(
        CartDbContext db,
        Guid? accountId,
        string? suppliedToken,
        string marketCode,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var normalizedMarket = marketCode.Trim().ToLowerInvariant();
        Entities.Cart? cart = null;
        string? issuedToken = null;

        if (accountId is Guid aid)
        {
            cart = await db.Carts
                .Where(c => c.AccountId == aid && c.MarketCode == normalizedMarket && c.Status == "active")
                .SingleOrDefaultAsync(cancellationToken);
        }

        // Only follow the token branch for anonymous callers. Authenticated callers who supply
        // an anon token must go through POST /merge explicitly — claiming-on-lookup would bypass
        // the merger's qty summing + reservation consolidation and violate FR-003.
        if (cart is null && accountId is null && !string.IsNullOrWhiteSpace(suppliedToken)
            && _tokenProvider.TryDecode(suppliedToken, nowUtc, out var hash))
        {
            cart = await db.Carts
                .Where(c => c.CartTokenHash == hash && c.MarketCode == normalizedMarket && c.Status == "active")
                .SingleOrDefaultAsync(cancellationToken);
        }

        if (cart is null)
        {
            byte[]? tokenHash = null;
            if (accountId is null)
            {
                var issued = _tokenProvider.Issue(nowUtc);
                tokenHash = issued.Hash;
                issuedToken = issued.Token;
            }
            cart = new Entities.Cart
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                CartTokenHash = tokenHash,
                MarketCode = normalizedMarket,
                Status = "active",
                LastTouchedAt = nowUtc,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc,
                OwnerId = "platform",
            };
            db.Carts.Add(cart);
        }

        return new ResolvedCart(cart, issuedToken);
    }

    public async Task<Entities.Cart?> LookupAsync(
        CartDbContext db,
        Guid? accountId,
        string? suppliedToken,
        string marketCode,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var normalizedMarket = marketCode.Trim().ToLowerInvariant();
        if (accountId is Guid aid)
        {
            var cart = await db.Carts
                .Where(c => c.AccountId == aid && c.MarketCode == normalizedMarket && c.Status == "active")
                .SingleOrDefaultAsync(cancellationToken);
            if (cart is not null) return cart;
        }
        // Anonymous-only token branch — mirrors ResolveOrCreateAsync. Authenticated callers must
        // use POST /merge to adopt an anon cart.
        if (accountId is null
            && !string.IsNullOrWhiteSpace(suppliedToken)
            && _tokenProvider.TryDecode(suppliedToken, nowUtc, out var hash))
        {
            return await db.Carts
                .Where(c => c.CartTokenHash == hash && c.MarketCode == normalizedMarket && c.Status == "active")
                .SingleOrDefaultAsync(cancellationToken);
        }
        return null;
    }
}

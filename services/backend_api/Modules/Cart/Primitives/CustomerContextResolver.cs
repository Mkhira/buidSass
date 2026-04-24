using BackendApi.Modules.Identity.Persistence;
using BackendApi.Modules.Pricing.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cart.Primitives;

/// <summary>
/// Resolves the cart-relevant slice of an account's profile: whether the buyer is a B2B tier
/// holder and whether their professional verification state unlocks restricted products.
/// Spec 011 will widen "verified" to cover market-specific + document-type gates; today we
/// treat any `verified` status as a pass.
/// </summary>
public sealed class CustomerContextResolver(IdentityDbContext identityDb, PricingDbContext pricingDb)
{
    public sealed record Context(bool IsB2B, bool VerifiedForRestriction);

    public async Task<Context> ResolveAsync(Guid? accountId, CancellationToken ct)
    {
        if (accountId is null || accountId == Guid.Empty)
        {
            return new Context(false, false);
        }

        var accountStatus = await identityDb.Accounts
            .AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => a.ProfessionalVerificationStatus)
            .SingleOrDefaultAsync(ct);

        var verified = string.Equals(accountStatus, "verified", StringComparison.OrdinalIgnoreCase);

        var isB2B = await pricingDb.AccountB2BTiers
            .AsNoTracking()
            .AnyAsync(t => t.AccountId == accountId, ct);

        return new Context(isB2B, verified);
    }
}

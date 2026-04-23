using System.Security.Claims;
using BackendApi.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Identity.Admin.Me;

public static class AdminMeHandler
{
    public static async Task<AdminMeResponse?> HandleAsync(
        ClaimsPrincipal user,
        IdentityDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var subject = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(subject, out var accountId))
        {
            return null;
        }

        var account = await dbContext.Accounts.SingleOrDefaultAsync(
            x => x.Id == accountId && x.Surface == "admin",
            cancellationToken);

        if (account is null)
        {
            return null;
        }

        var permissions = user.Claims
            .Where(x => x.Type is "permission" or "permissions" or "perm")
            .Select(x => x.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new AdminMeResponse(
            account.Id,
            account.EmailDisplay,
            account.Locale,
            account.MarketCode,
            permissions);
    }
}

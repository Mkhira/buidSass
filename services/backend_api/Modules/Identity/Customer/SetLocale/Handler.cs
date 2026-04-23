using System.Security.Claims;
using BackendApi.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Identity.Customer.SetLocale;

public static class SetLocaleHandler
{
    public static async Task<bool> HandleAsync(
        ClaimsPrincipal user,
        SetLocaleRequest request,
        IdentityDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var subject = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(subject, out var accountId))
        {
            return false;
        }

        var account = await dbContext.Accounts.SingleOrDefaultAsync(
            x => x.Id == accountId && x.Surface == "customer",
            cancellationToken);

        if (account is null)
        {
            return false;
        }

        account.Locale = request.Locale;
        account.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}

using System.Security.Claims;
using BackendApi.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Identity.Customer.Me;

public static class CustomerMeHandler
{
    public static async Task<CustomerMeResponse?> HandleAsync(
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
            x => x.Id == accountId && x.Surface == "customer",
            cancellationToken);

        if (account is null)
        {
            return null;
        }

        var roles = await (
                from accountRole in dbContext.AccountRoles
                join role in dbContext.Roles on accountRole.RoleId equals role.Id
                where accountRole.AccountId == accountId
                select role.Code)
            .Distinct()
            .ToListAsync(cancellationToken);

        return new CustomerMeResponse(
            account.Id,
            account.EmailDisplay,
            account.PhoneE164,
            account.EmailVerifiedAt,
            account.PhoneVerifiedAt,
            account.Locale,
            roles);
    }
}

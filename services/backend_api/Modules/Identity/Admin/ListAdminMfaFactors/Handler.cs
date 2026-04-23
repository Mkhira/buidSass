using BackendApi.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Identity.Admin.ListAdminMfaFactors;

public static class ListAdminMfaFactorsHandler
{
    public static async Task<ListAdminMfaFactorsResponse> HandleAsync(
        ListAdminMfaFactorsRequest request,
        IdentityDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var factors = await dbContext.AdminMfaFactors
            .Where(x => x.AccountId == request.AccountId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new AdminMfaFactorItem(x.Id, x.Kind, x.ConfirmedAt, x.RevokedAt, x.LastUsedAt))
            .ToListAsync(cancellationToken);

        return new ListAdminMfaFactorsResponse(factors);
    }
}

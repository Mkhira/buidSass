using System.Security.Claims;
using BackendApi.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Identity.Admin.ListAdminSessions;

public static class ListAdminSessionsHandler
{
    public static async Task<ListAdminSessionsResponse> HandleAsync(
        ClaimsPrincipal user,
        ListAdminSessionsRequest request,
        IdentityDbContext dbContext,
        CancellationToken cancellationToken)
    {
        Guid.TryParse(user.FindFirstValue("sid"), out var currentSessionId);

        var sessions = await dbContext.Sessions
            .Where(x => x.AccountId == request.AccountId
                        && x.Surface == "admin"
                        && x.Status == "active")
            .OrderByDescending(x => x.LastSeenAt)
            .Select(x => new AdminSessionResponseItem(
                x.Id,
                x.CreatedAt,
                x.LastSeenAt,
                x.ClientAgent,
                x.Id == currentSessionId))
            .ToListAsync(cancellationToken);

        return new ListAdminSessionsResponse(sessions);
    }
}

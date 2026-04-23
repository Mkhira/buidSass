using System.Security.Claims;
using BackendApi.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Identity.Customer.ListSessions;

public static class ListSessionsHandler
{
    public static async Task<ListSessionsResponse> HandleAsync(
        ClaimsPrincipal user,
        IdentityDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var subject = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        var accountId = Guid.Parse(subject!);

        Guid.TryParse(user.FindFirstValue("sid"), out var currentSessionId);

        var sessions = await dbContext.Sessions
            .Where(x => x.AccountId == accountId && x.Status == "active")
            .OrderByDescending(x => x.LastSeenAt)
            .Select(x => new CustomerSessionResponseItem(
                x.Id,
                x.CreatedAt,
                x.LastSeenAt,
                x.ClientAgent,
                x.Id == currentSessionId))
            .ToListAsync(cancellationToken);

        return new ListSessionsResponse(sessions);
    }
}

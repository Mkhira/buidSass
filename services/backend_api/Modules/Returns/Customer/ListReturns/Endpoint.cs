using BackendApi.Modules.Returns.Common;
using BackendApi.Modules.Returns.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Returns.Customer.ListReturns;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapListReturnsEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    /// <summary>FR-017. Paginated customer returns list.</summary>
    private static async Task<IResult> HandleAsync(
        HttpContext context,
        ReturnsDbContext db,
        string? status,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        var accountId = ReturnsResponseFactory.ResolveAccountId(context);
        if (accountId is null)
        {
            return ReturnsResponseFactory.Problem(context, 401, "returns.requires_auth", "Auth required");
        }
        var p = Math.Max(1, page ?? 1);
        var ps = Math.Clamp(pageSize ?? 20, 1, 100);

        var q = db.ReturnRequests.AsNoTracking()
            .Where(r => r.AccountId == accountId.Value);
        if (!string.IsNullOrWhiteSpace(status))
        {
            q = q.Where(r => r.State == status);
        }
        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(r => r.SubmittedAt)
            .Skip((p - 1) * ps).Take(ps)
            .Select(r => new
            {
                id = r.Id,
                returnNumber = r.ReturnNumber,
                orderId = r.OrderId,
                state = r.State,
                submittedAt = r.SubmittedAt,
                reasonCode = r.ReasonCode,
                lineCount = r.Lines.Count,
            })
            .ToListAsync(ct);

        return Results.Ok(new
        {
            page = p,
            pageSize = ps,
            total,
            items,
        });
    }
}

using System.Text.Json.Serialization;
using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.B2B.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.B2B.Quotes.Approver.ListPendingApprovals;

/// <summary>
/// Spec 021 contract §3.1 — paginated list of <c>pending-approver</c> quotes
/// scoped to the caller's <c>approver</c>-membership companies.
/// </summary>
public sealed class ListPendingApprovalsHandler
{
    private readonly B2BDbContext _db;
    private readonly TimeProvider _time;

    public ListPendingApprovalsHandler(B2BDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<ListPendingApprovalsResponse> HandleAsync(
        Guid customerId,
        Guid? companyId,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize <= 0 ? 25 : pageSize, 1, 50);

        var approverCompanyIds = await _db.CompanyMemberships
            .AsNoTracking()
            .Where(m => m.UserId == customerId && m.Role == "approver")
            .Select(m => m.CompanyId)
            .Distinct()
            .ToListAsync(ct);

        if (companyId is { } cid)
        {
            approverCompanyIds = approverCompanyIds.Where(c => c == cid).ToList();
        }
        if (approverCompanyIds.Count == 0)
        {
            return new ListPendingApprovalsResponse(Array.Empty<ListPendingApprovalsRow>(), page, pageSize, 0);
        }

        var query = _db.Quotes.AsNoTracking()
            .Where(q => q.State == "pending-approver" && q.CompanyId != null
                && approverCompanyIds.Contains(q.CompanyId.Value));

        var total = await query.CountAsync(ct);
        var nowUtc = _time.GetUtcNow();
        var rows = await query
            .OrderBy(q => q.RequestedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(q => new ListPendingApprovalsRow(
                q.Id,
                q.CompanyId!.Value,
                q.BranchId,
                q.CustomerId,
                q.PoNumber,
                q.RequestedAt,
                q.ExpiresAt))
            .ToListAsync(ct);

        return new ListPendingApprovalsResponse(rows, page, pageSize, total);
    }
}

public sealed record ListPendingApprovalsRow(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("company_id")] Guid CompanyId,
    [property: JsonPropertyName("branch_id")] Guid? BranchId,
    [property: JsonPropertyName("buyer_id")] Guid BuyerId,
    [property: JsonPropertyName("po_number")] string? PoNumber,
    [property: JsonPropertyName("requested_at")] DateTimeOffset RequestedAt,
    [property: JsonPropertyName("expires_at")] DateTimeOffset? ExpiresAt);

public sealed record ListPendingApprovalsResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<ListPendingApprovalsRow> Items,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("page_size")] int PageSize,
    [property: JsonPropertyName("total")] int Total);

public static class ListPendingApprovalsEndpoint
{
    public static IEndpointRouteBuilder MapListPendingApprovalsEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/awaiting-my-approval", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        ListPendingApprovalsHandler handler,
        CancellationToken ct)
    {
        var customerId = B2BResponseFactory.ResolveCustomerId(context);
        if (customerId is null)
        {
            return B2BResponseFactory.Problem(context, 401,
                QuoteReasonCode.QuoteRequiredFieldMissing,
                "Authentication required.");
        }
        var query = context.Request.Query;
        var page = int.TryParse(query["page"], out var p) ? p : 1;
        var pageSize = int.TryParse(query["page_size"], out var ps) ? ps : 25;
        Guid? companyId = Guid.TryParse(query["company_id"], out var c) ? c : null;
        var resp = await handler.HandleAsync(customerId.Value, companyId, page, pageSize, ct);
        return Results.Ok(resp);
    }
}

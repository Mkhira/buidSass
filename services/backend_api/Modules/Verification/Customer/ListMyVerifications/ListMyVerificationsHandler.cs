using BackendApi.Modules.Verification.Persistence;
using BackendApi.Modules.Verification.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Verification.Customer.ListMyVerifications;

/// <summary>
/// Spec 020 contracts §2.3 / tasks T056. Customer-scoped paginated list of every
/// verification the customer has ever submitted. Default sort: most-recent
/// first. Page-size clamped 1..50.
/// </summary>
public sealed class ListMyVerificationsHandler(VerificationDbContext db)
{
    public async Task<ListMyVerificationsResponse> HandleAsync(
        Guid customerId,
        ListMyVerificationsQuery query,
        CancellationToken ct)
    {
        var pageSize = Math.Clamp(query.PageSize, 1, 50);
        var page = Math.Max(query.Page, 1);

        var baseQuery = db.Verifications
            .AsNoTracking()
            .Where(v => v.CustomerId == customerId);

        var totalCount = await baseQuery.CountAsync(ct);

        var items = await baseQuery
            .OrderByDescending(v => v.SubmittedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(v => new ListMyVerificationsRow(
                v.Id,
                v.State.ToWireValue(),
                v.MarketCode,
                v.Profession,
                v.SubmittedAt,
                v.DecidedAt,
                v.ExpiresAt))
            .ToListAsync(ct);

        return new ListMyVerificationsResponse(items, page, pageSize, totalCount);
    }
}

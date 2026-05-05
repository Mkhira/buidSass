using BackendApi.Modules.B2B.Authorization;
using BackendApi.Modules.B2B.Primitives;
using BackendApi.Modules.B2B.Quotes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.B2B.Quotes.Admin.ListQuoteQueue;

/// <summary>
/// Spec 021 contract §4.1 — endpoint for <c>GET /api/admin/quotes</c>. Permission
/// gate: caller must hold <c>quotes.author</c> OR <c>quotes.review</c>.
/// </summary>
public static class ListQuoteQueueEndpoint
{
    public static IEndpointRouteBuilder MapListQuoteQueueEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = AdminAuthorizationDefaults.AuthenticationScheme,
            });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        ListQuoteQueueHandler handler,
        CancellationToken ct)
    {
        if (!HasQuoteQueuePermission(context.User))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var marketCode = B2BResponseFactory.ResolveMarketCode(context);
        if (marketCode is null)
        {
            return B2BResponseFactory.Problem(context, 400,
                QuoteReasonCode.QuoteMarketMismatch,
                "Unknown market claim.");
        }

        var query = context.Request.Query;
        var page = int.TryParse(query["page"], out var p) ? p : 1;
        var pageSize = int.TryParse(query["page_size"], out var ps) ? ps : ListQuoteQueueHandler.DefaultPageSize;
        var ageMin = int.TryParse(query["age_min_business_days"], out var a) ? (int?)a : null;
        Guid? companyId = Guid.TryParse(query["company_id"], out var c) ? c : null;
        Guid? customerId = Guid.TryParse(query["customer_id"], out var cu) ? cu : null;

        var req = new ListQuoteQueueRequest(
            Market: query["market"],
            StatesCsv: query["state"],
            CompanyId: companyId,
            CustomerId: customerId,
            AgeMinBusinessDays: ageMin,
            Search: query["search"],
            Sort: query["sort"],
            Page: page,
            PageSize: pageSize);

        var resp = await handler.HandleAsync(marketCode, req, ct);
        return Results.Ok(resp);
    }

    private static bool HasQuoteQueuePermission(System.Security.Claims.ClaimsPrincipal user) =>
        user.HasClaim("permission", B2BPermissions.QuotesAuthor)
        || user.HasClaim("permissions", B2BPermissions.QuotesAuthor)
        || user.HasClaim("permission", B2BPermissions.QuotesReview)
        || user.HasClaim("permissions", B2BPermissions.QuotesReview);
}

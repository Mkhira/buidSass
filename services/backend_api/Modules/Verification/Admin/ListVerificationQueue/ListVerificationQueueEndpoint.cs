using BackendApi.Modules.Verification.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Verification.Admin.ListVerificationQueue;

/// <summary>HTTP wiring for the reviewer queue per spec 020 contracts §3.1.</summary>
public static class ListVerificationQueueEndpoint
{
    public static IEndpointRouteBuilder MapListVerificationQueueEndpoint(
        this IEndpointRouteBuilder builder)
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
        ListVerificationQueueHandler handler,
        CancellationToken ct,
        string? market = null,
        string? state = null,
        string? profession = null,
        int? age_min_business_days = null,
        string? search = null,
        string sort = "oldest",
        int page = 1,
        int page_size = 25)
    {
        if (!HasReviewPermission(context))
        {
            return AdminVerificationResponseFactory.Problem(
                context, 403,
                "verification.review_permission_required",
                "verification.review permission required.");
        }

        var reviewerMarkets = AdminVerificationResponseFactory.ResolveAssignedMarkets(context);

        var query = new ListVerificationQueueQuery(
            MarketFilter: market,
            StateFilter: state?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            ProfessionFilter: profession?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            AgeMinBusinessDays: age_min_business_days,
            Search: search,
            Sort: sort,
            Page: page,
            PageSize: page_size);

        var result = await handler.HandleAsync(reviewerMarkets, query, ct);
        return Results.Ok(result);
    }

    private static bool HasReviewPermission(HttpContext context)
    {
        return context.User.HasClaim("permission", VerificationPermissions.Review)
            || context.User.HasClaim("permissions", VerificationPermissions.Review);
    }
}

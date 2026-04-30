using BackendApi.Modules.Reviews.Customer.GetReportReasons;
using BackendApi.Modules.Reviews.Customer.ReportReview;
using BackendApi.Modules.Reviews.Hooks;
using BackendApi.Modules.Shared;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BackendApi.Modules.Reviews;

/// <summary>
/// Companion partial implementations for the US3 report-flow surface (Phase 5).
/// Augments <see cref="ReviewsModule.AddSlices"/> via a second partial method
/// declaration — the C# compiler combines all <c>partial void AddSlices</c>
/// bodies into the same DI extension.
/// </summary>
public static partial class ReviewsModule
{
    static partial void AddUs3Slices(IServiceCollection services)
    {
        // Reporter-facts fallback — replaced by spec 004 + 011 composed binding when those PRs land.
        services.TryAddScoped<IReviewReporterFactsQuery, NullReviewReporterFactsQuery>();

        services.AddScoped<ReportReviewHandler>();
        services.AddSingleton<GetReportReasonsHandler>();
    }

    static partial void MapUs3CustomerEndpoints(IEndpointRouteBuilder customer)
    {
        customer.MapReportReviewEndpoint();
        customer.MapGetReportReasonsEndpoint();
    }
}

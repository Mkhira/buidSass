using BackendApi.Modules.Reviews.Aggregate;
using BackendApi.Modules.Reviews.Customer.GetMyReview;
using BackendApi.Modules.Reviews.Customer.ListMyReviews;
using BackendApi.Modules.Reviews.Customer.SubmitReview;
using BackendApi.Modules.Reviews.Customer.UpdateReview;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Reviews;

/// <summary>
/// Companion partial implementations for the customer surface (US1 / US2).
/// Wires the slice handlers + endpoints registered in this PR; later PRs
/// extend the same partial with their additional slices.
/// </summary>
public static partial class ReviewsModule
{
    static partial void AddUs1Slices(IServiceCollection services)
    {
        services.AddScoped<RatingAggregateRecomputer>();

        services.AddScoped<SubmitReviewHandler>();
        services.AddScoped<UpdateReviewHandler>();
        services.AddScoped<ListMyReviewsHandler>();
        services.AddScoped<GetMyReviewHandler>();
    }

    static partial void MapUs1CustomerEndpoints(IEndpointRouteBuilder customer)
    {
        customer.MapSubmitReviewEndpoint();
        customer.MapUpdateReviewEndpoint();
        customer.MapListMyReviewsEndpoint();
        customer.MapGetMyReviewEndpoint();
    }
}

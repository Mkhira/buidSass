using BackendApi.Modules.Reviews.Aggregate;
using BackendApi.Modules.Reviews.Aggregate.ReadProductRating;
using BackendApi.Modules.Shared;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Reviews;

/// <summary>
/// Companion partial implementations for the US6 public-aggregate surface (Phase 8).
/// Wires <see cref="IRatingAggregateReader"/> as the outbound contract consumed by
/// spec 005 product detail + spec 006 search-result decoration, plus the public
/// unauthenticated read endpoints.
/// </summary>
public static partial class ReviewsModule
{
    static partial void AddUs6Slices(IServiceCollection services)
    {
        services.AddScoped<RatingAggregateReader>();
        services.AddScoped<IRatingAggregateReader>(sp => sp.GetRequiredService<RatingAggregateReader>());

        services.AddScoped<ReadProductRatingHandler>();
    }

    static partial void MapUs6PublicEndpoints(IEndpointRouteBuilder publicAggregates)
    {
        publicAggregates.MapReadProductRatingEndpoints();
    }
}

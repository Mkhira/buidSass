using BackendApi.Configuration;
using BackendApi.Features.Seeding;
using BackendApi.Modules.Reviews.Filtering;
using BackendApi.Modules.Reviews.Hooks;
using BackendApi.Modules.Reviews.Persistence;
using BackendApi.Modules.Reviews.Seeding;
using BackendApi.Modules.Shared;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace BackendApi.Modules.Reviews;

/// <summary>
/// DI + endpoint wiring for the Reviews vertical-slice module per spec 022.
/// Slice handlers are registered as they are introduced; this file is the
/// integration point that Program.cs calls.
/// </summary>
public static partial class ReviewsModule
{
    public static IServiceCollection AddReviewsModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment)
    {
        var connectionString = configuration.ResolveRequiredDefaultConnectionString(hostEnvironment);

        services.AddDbContext<ReviewsDbContext>((provider, options) =>
        {
            var dataSource = provider.GetService<NpgsqlDataSource>();
            if (dataSource is not null)
            {
                options.UseNpgsql(dataSource);
            }
            else
            {
                options.UseNpgsql(connectionString);
            }
            // ManyServiceProvidersCreatedWarning suppressed (project-memory rule).
            options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        });

        services.AddDbContextFactory<ReviewsDbContext>((provider, options) =>
        {
            var dataSource = provider.GetService<NpgsqlDataSource>();
            if (dataSource is not null)
            {
                options.UseNpgsql(dataSource);
            }
            else
            {
                options.UseNpgsql(connectionString);
            }
            options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        }, lifetime: ServiceLifetime.Singleton);

        services.AddSingleton<ProfanityFilter>();
        services.AddScoped<ISeeder, ReviewsReferenceDataSeeder>();

        // Cross-module fallback bindings — replaced by their owning specs at runtime.
        // TryAdd lets specs 011 / 005 / 019 / 013 supply production implementations
        // without coordinating registration order. Same loose-coupling pattern as
        // VerificationModule's NullProductRestrictionPolicy / NullRegulatorAssistLookup.
        services.TryAddScoped<IOrderLineDeliveryEligibilityQuery, NullOrderLineDeliveryEligibilityQuery>();

        // Per-phase slice registrations — each US lives in its own companion partial file.
        // C# allows at most ONE body per partial method, so each phase declares its own.
        AddUs1Slices(services);
        AddUs3Slices(services);
        AddUs6Slices(services);

        services.AddSingleton(TimeProvider.System);
        return services;
    }

    public static IEndpointRouteBuilder MapReviewsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var customer = endpoints.MapGroup("/api/customer/reviews");
        MapUs1CustomerEndpoints(customer);
        MapUs3CustomerEndpoints(customer);

        var publicAggregates = endpoints.MapGroup("/api/public/reviews/aggregates");
        MapUs6PublicEndpoints(publicAggregates);

        return endpoints;
    }

    static partial void AddUs1Slices(IServiceCollection services);
    static partial void AddUs3Slices(IServiceCollection services);
    static partial void AddUs6Slices(IServiceCollection services);

    static partial void MapUs1CustomerEndpoints(IEndpointRouteBuilder customer);
    static partial void MapUs3CustomerEndpoints(IEndpointRouteBuilder customer);
    static partial void MapUs6PublicEndpoints(IEndpointRouteBuilder publicAggregates);
}

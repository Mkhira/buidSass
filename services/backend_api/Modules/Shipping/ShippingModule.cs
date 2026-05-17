using BackendApi.Configuration;
using BackendApi.Modules.Shipping.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace BackendApi.Modules.Shipping;

/// <summary>
/// DI + endpoint wiring for the Shipping vertical-slice module per spec 026
/// (Phase 1E · Milestone 8). Phase 0 ships persistence + primitives;
/// per-phase slice handlers, providers, webhooks, workers, and subscribers
/// are registered through the partial files (one per phase).
/// </summary>
public static partial class ShippingModule
{
    public static IServiceCollection AddShippingModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment)
    {
        var connectionString = configuration.ResolveRequiredDefaultConnectionString(hostEnvironment);

        services.AddDbContext<ShippingDbContext>((provider, options) =>
        {
            var dataSource = provider.GetService<NpgsqlDataSource>();
            if (dataSource is not null) options.UseNpgsql(dataSource);
            else options.UseNpgsql(connectionString);
            // ManyServiceProvidersCreatedWarning suppressed (project-memory rule).
            options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        });

        services.AddDbContextFactory<ShippingDbContext>((provider, options) =>
        {
            var dataSource = provider.GetService<NpgsqlDataSource>();
            if (dataSource is not null) options.UseNpgsql(dataSource);
            else options.UseNpgsql(connectionString);
            options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        }, lifetime: ServiceLifetime.Singleton);

        AddSlices(services, configuration, hostEnvironment);
        AddWorkers(services, configuration);
        AddSubscribers(services);

        services.AddSingleton(TimeProvider.System);
        return services;
    }

    public static IEndpointRouteBuilder MapShippingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var customer = endpoints.MapGroup("/v1/shipping");
        MapCustomerEndpoints(customer);

        var admin = endpoints.MapGroup("/v1/admin/shipping");
        MapAdminEndpoints(admin);

        var webhooks = endpoints.MapGroup("/v1/shipping/webhooks");
        MapWebhookEndpoints(webhooks);
        return endpoints;
    }

    // Per-phase partials own their own registrations.
    static partial void AddSlices(IServiceCollection services, IConfiguration configuration, IHostEnvironment hostEnvironment);
    static partial void AddWorkers(IServiceCollection services, IConfiguration configuration);
    static partial void AddSubscribers(IServiceCollection services);
    static partial void MapCustomerEndpoints(IEndpointRouteBuilder customer);
    static partial void MapAdminEndpoints(IEndpointRouteBuilder admin);
    static partial void MapWebhookEndpoints(IEndpointRouteBuilder webhooks);
}

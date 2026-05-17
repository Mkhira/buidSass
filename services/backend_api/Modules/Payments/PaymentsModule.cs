using BackendApi.Configuration;
using BackendApi.Modules.Payments.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace BackendApi.Modules.Payments;

/// <summary>
/// DI + endpoint wiring for the Payments vertical-slice module per spec 027
/// (Phase 1E · Milestone 9). The module owns the 12-table <c>payments</c>
/// schema, the Payment / Refund / ReconciliationException state machines, and
/// the 7-provider abstraction (HyperPay, Tap, Paymob, Kashier, Tabby, Tamara,
/// Valu). Per the project-memory rule, <c>ManyServiceProvidersCreatedWarning</c>
/// is suppressed on both the DbContext and DbContextFactory registrations so
/// multi-WebApplicationFactory integration suites do not break Identity tests.
/// </summary>
public static partial class PaymentsModule
{
    public static IServiceCollection AddPaymentsModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment)
    {
        var connectionString = configuration.ResolveRequiredDefaultConnectionString(hostEnvironment);

        services.AddDbContext<PaymentsDbContext>((provider, options) =>
        {
            var dataSource = provider.GetService<NpgsqlDataSource>();
            if (dataSource is not null) options.UseNpgsql(dataSource);
            else options.UseNpgsql(connectionString);
            // ManyServiceProvidersCreatedWarning suppressed (project-memory rule).
            options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        });

        services.AddDbContextFactory<PaymentsDbContext>((provider, options) =>
        {
            var dataSource = provider.GetService<NpgsqlDataSource>();
            if (dataSource is not null) options.UseNpgsql(dataSource);
            else options.UseNpgsql(connectionString);
            options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        }, lifetime: ServiceLifetime.Singleton);

        AddSlices(services, configuration, hostEnvironment);
        AddProviders(services);
        AddWorkers(services, configuration);
        AddSubscribers(services);

        services.AddSingleton(TimeProvider.System);
        return services;
    }

    public static IEndpointRouteBuilder MapPaymentsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var customer = endpoints.MapGroup("/v1/payments");
        MapCustomerEndpoints(customer);

        var admin = endpoints.MapGroup("/v1/admin/payments");
        MapAdminEndpoints(admin);

        var webhooks = endpoints.MapGroup("/v1/payments/webhooks");
        MapWebhookEndpoints(webhooks);
        return endpoints;
    }

    // Per-phase partials own their own registrations.
    static partial void AddSlices(IServiceCollection services, IConfiguration configuration, IHostEnvironment hostEnvironment);
    static partial void AddProviders(IServiceCollection services);
    static partial void AddWorkers(IServiceCollection services, IConfiguration configuration);
    static partial void AddSubscribers(IServiceCollection services);
    static partial void MapCustomerEndpoints(IEndpointRouteBuilder customer);
    static partial void MapAdminEndpoints(IEndpointRouteBuilder admin);
    static partial void MapWebhookEndpoints(IEndpointRouteBuilder webhooks);
}

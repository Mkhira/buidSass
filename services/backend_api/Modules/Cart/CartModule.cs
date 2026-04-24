using BackendApi.Configuration;
using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Cart.Primitives;
using BackendApi.Modules.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

namespace BackendApi.Modules.Cart;

public static class CartModule
{
    public static IServiceCollection AddCartModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment)
    {
        var connectionString = configuration.ResolveRequiredDefaultConnectionString(hostEnvironment);

        services.AddDbContext<CartDbContext>((provider, options) =>
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
            // The ManyServiceProvidersCreatedWarning is benign test-scaffold churn; suppress so
            // integration suites that spin up many WebApplicationFactories don't blow up.
            options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        });

        services.Configure<CartOptions>(configuration.GetSection(CartOptions.SectionName));
        services.AddSingleton<IValidateOptions<CartOptions>, CartOptionsValidator>();

        services.AddSingleton<CartTokenProvider>();
        services.AddSingleton<CartMerger>();
        services.AddSingleton<EligibilityEvaluator>();
        services.AddSingleton<CartResolver>();
        services.AddSingleton<CartViewBuilder>();
        services.AddScoped<CartInventoryOrchestrator>();
        services.AddScoped<CartReservationRehydrator>();
        services.AddScoped<CustomerContextResolver>();

        // Login-merge hook (FR-003). Scoped so it resolves scoped DbContexts.
        services.AddScoped<ICustomerPostSignInHook, CartLoginMergeHook>();

        // Suppress workers in the Test environment — integration tests tick workers manually
        // to assert exact behaviour, and a live background worker racing the tests produces
        // flakes (M10). Production + Development keep the hosted services live.
        if (!hostEnvironment.IsEnvironment("Test"))
        {
            services.AddHostedService<Workers.AbandonedCartWorker>();
            services.AddHostedService<Workers.GuestCartCleanupWorker>();
            services.AddHostedService<Workers.ArchivedCartReaperWorker>();
        }

        return services;
    }

    public static WebApplication UseCartModuleEndpoints(this WebApplication app)
    {
        var customer = app.MapGroup("/v1/customer/cart");
        Customer.GetCart.Endpoint.MapGetCartEndpoint(customer);
        Customer.AddLine.Endpoint.MapAddLineEndpoint(customer);
        Customer.UpdateLine.Endpoint.MapUpdateLineEndpoint(customer);
        Customer.Merge.Endpoint.MapMergeEndpoint(customer);
        Customer.ApplyCoupon.Endpoint.MapCouponEndpoints(customer);
        Customer.SetB2BMetadata.Endpoint.MapSetB2BMetadataEndpoint(customer);
        Customer.SaveForLater.Endpoint.MapSaveForLaterEndpoints(customer);
        Customer.SwitchMarket.Endpoint.MapSwitchMarketEndpoint(customer);
        Customer.Restore.Endpoint.MapRestoreEndpoint(customer);

        var admin = app.MapGroup("/v1/admin/cart");
        Admin.GetCart.Endpoint.MapAdminGetCartEndpoint(admin);
        Admin.ListAbandoned.Endpoint.MapAdminListAbandonedEndpoint(admin);
        return app;
    }
}

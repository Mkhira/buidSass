using BackendApi.Configuration;
using BackendApi.Features.Seeding;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Primitives;
using BackendApi.Modules.Pricing.Primitives.Caches;
using BackendApi.Modules.Pricing.Seeding;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace BackendApi.Modules.Pricing;

public static class PricingModule
{
    public static IServiceCollection AddPricingModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment)
    {
        var connectionString = configuration.ResolveRequiredDefaultConnectionString(hostEnvironment);

        services.AddSingleton<Persistence.ImmutablePriceExplanationInterceptor>();
        services.AddDbContext<PricingDbContext>((provider, options) =>
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
            options.AddInterceptors(provider.GetRequiredService<Persistence.ImmutablePriceExplanationInterceptor>());
        });

        services.AddMemoryCache();
        services.AddSingleton<TaxRateCache>();
        services.AddSingleton<PromotionCache>();
        services.AddSingleton<IPriceCalculator, PriceCalculator>();

        services.AddScoped<ISeeder, PricingReferenceDataSeeder>();

        return services;
    }

    public static WebApplication UsePricingModuleEndpoints(this WebApplication app)
    {
        var customerPricing = app.MapGroup("/v1/customer/pricing");
        Customer.PriceCart.Endpoint.MapPriceCartEndpoint(customerPricing);

        var internalPricing = app.MapGroup("/v1/internal/pricing");
        Internal.Calculate.Endpoint.MapCalculateEndpoint(internalPricing);

        var adminPricing = app.MapGroup("/v1/admin/pricing");
        Admin.TaxRates.Endpoint.MapTaxRateEndpoints(adminPricing);
        Admin.Promotions.Endpoint.MapPromotionEndpoints(adminPricing);
        Admin.Coupons.Endpoint.MapCouponEndpoints(adminPricing);
        Admin.B2BTiers.Endpoint.MapB2BTierEndpoints(adminPricing);
        Admin.ProductTierPrices.Endpoint.MapProductTierPriceEndpoints(adminPricing);
        Admin.Explanations.Endpoint.MapExplanationEndpoints(adminPricing);

        return app;
    }
}

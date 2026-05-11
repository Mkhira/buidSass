using BackendApi.Configuration;
using BackendApi.Features.Seeding;
using BackendApi.Modules.Pricing.Persistence;
using BackendApi.Modules.Pricing.Primitives;
using BackendApi.Modules.Pricing.Primitives.Caches;
using BackendApi.Modules.Pricing.Seeding;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
            options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        });

        services.AddMemoryCache();
        services.AddSingleton<TaxRateCache>();
        services.AddSingleton<PromotionCache>();
        services.AddSingleton<IPriceCalculator, PriceCalculator>();

        services.AddScoped<ISeeder, PricingReferenceDataSeeder>();

        // Spec 007-b T040 / T041: per-market commercial-threshold seeder. Idempotent;
        // runs in every environment so the high-impact-gate defaults are present even
        // when the 007-b foundation migration's INSERT path was bypassed (rebuilds,
        // bare-DB bootstraps). See Modules/Pricing/Seeding/PricingThresholdsSeeder.cs
        // for the seeded values (research §R8) and idempotency contract.
        services.AddScoped<ISeeder, PricingThresholdsSeeder>();
        // Spec 007-b US6: dev/staging-only synthetic dataset spanning every
        // lifecycle state for Coupons + Promotions + Campaigns. Env-gated
        // inside the seeder itself (no-op in Production).
        services.AddScoped<ISeeder, PromotionsV1DevSeeder>();

        // Spec 007-b US1: commercial-authoring shared services. Per-request
        // (scoped):
        //   - CommercialAuditWriter captures the request's PricingDbContext
        //     change-tracker for atomic dual-writes
        //     (commercial_audit_events + the platform audit_log_entries channel).
        //   - CommercialActorPermissions wraps the IdentityDbContext read for
        //     chord-style RBAC checks (super_admin / commercial.approver) that
        //     the route-level [RequirePermission(...)] gate cannot express.
        services.AddScoped<Admin.Common.CommercialAuditWriter>();
        services.AddScoped<Admin.Common.CommercialActorPermissions>();

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

        // Spec 007-b US1-US4: commercial-authoring surface (contract §1, base
        // path /v1/admin/commercial). Mounted alongside the legacy
        // /v1/admin/pricing routes so the existing 007-a admin endpoints keep
        // working unchanged.
        var adminCommercial = app.MapGroup("/v1/admin/commercial");
        Admin.Coupons.CommercialCouponEndpoints.MapCommercialCouponEndpoints(adminCommercial);
        Admin.Promotions.CommercialPromotionEndpoints.MapCommercialPromotionEndpoints(adminCommercial);
        Admin.BusinessPricing.CommercialBusinessPricingEndpoints.MapCommercialBusinessPricingEndpoints(adminCommercial);
        Admin.PreviewProfiles.CommercialPreviewProfileEndpoints.MapCommercialPreviewProfileEndpoints(adminCommercial);
        Admin.Preview.CommercialPreviewEndpoints.MapCommercialPreviewEndpoints(adminCommercial);
        // US4 — Campaigns (contract §5).
        Admin.Campaigns.CommercialCampaignEndpoints.MapCommercialCampaignEndpoints(adminCommercial);
        // US5 — Approval queue + threshold administration (contract §8, §9).
        Admin.Approvals.CommercialApprovalEndpoints.MapCommercialApprovalEndpoints(adminCommercial);
        Admin.Thresholds.CommercialThresholdEndpoints.MapCommercialThresholdEndpoints(adminCommercial);

        return app;
    }
}

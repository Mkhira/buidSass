using BackendApi.Configuration;
using BackendApi.Modules.Checkout.Persistence;
using BackendApi.Modules.Checkout.Primitives;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

namespace BackendApi.Modules.Checkout;

public static class CheckoutModule
{
    public static IServiceCollection AddCheckoutModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment)
    {
        var connectionString = configuration.ResolveRequiredDefaultConnectionString(hostEnvironment);

        services.AddDbContext<CheckoutDbContext>((provider, options) =>
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
        });

        services.Configure<CheckoutOptions>(configuration.GetSection(CheckoutOptions.SectionName));
        services.AddSingleton<IValidateOptions<CheckoutOptions>, CheckoutOptionsValidator>();

        services.AddSingleton<BackendApi.Modules.Observability.CheckoutMetrics>();
        services.AddSingleton<Primitives.PaymentMethodCatalog>();
        services.AddSingleton<Primitives.DriftDetector>();
        services.AddScoped<Primitives.IdempotencyStore>();

        // Stub gateway / shipping / order-handler are ONLY safe in Development + Test. In any
        // other environment, registering them would silently let checkout finalize fake
        // payments / shipments — release blocker (CR review on PR #30). When real provider
        // modules are introduced (ADR-007 / ADR-008 / spec 011), they register over this gate.
        var allowStubs = hostEnvironment.IsDevelopment() || hostEnvironment.IsEnvironment("Test");
        if (allowStubs)
        {
            services.AddSingleton<Primitives.Payment.IPaymentGateway, Primitives.Payment.StubPaymentGateway>();
            services.AddSingleton<Primitives.Shipping.IShippingProvider, Primitives.Shipping.StubShippingProvider>();
            services.AddScoped<BackendApi.Modules.Shared.IOrderFromCheckoutHandler, Primitives.StubOrderFromCheckoutHandler>();
        }
        else
        {
            // Sentinel registrations — startup succeeds, but resolving any of these in a
            // non-dev host throws loudly so the operator sees the missing wiring at first
            // request rather than silently confirming fake checkouts.
            services.AddSingleton<Primitives.Payment.IPaymentGateway>(_ =>
                throw new InvalidOperationException(
                    "IPaymentGateway has no real implementation registered. Register a provider before using Checkout outside Development/Test."));
            services.AddSingleton<Primitives.Shipping.IShippingProvider>(_ =>
                throw new InvalidOperationException(
                    "IShippingProvider has no real implementation registered. Register a carrier before using Checkout outside Development/Test."));
            services.AddScoped<BackendApi.Modules.Shared.IOrderFromCheckoutHandler>(_ =>
                throw new InvalidOperationException(
                    "IOrderFromCheckoutHandler has no real implementation registered. Spec 011's order handler must register over the stub before this environment ships."));
        }

        if (!hostEnvironment.IsEnvironment("Test"))
        {
            services.AddHostedService<Workers.CheckoutExpiryWorker>();
            services.AddHostedService<Workers.PaymentReconciliationWorker>();
        }

        return services;
    }

    public static WebApplication UseCheckoutModuleEndpoints(this WebApplication app)
    {
        var customer = app.MapGroup("/v1/customer/checkout");
        Customer.StartSession.Endpoint.MapStartSessionEndpoint(customer);
        Customer.SetAddress.Endpoint.MapSetAddressEndpoint(customer);
        Customer.GetShippingQuotes.Endpoint.MapGetShippingQuotesEndpoint(customer);
        Customer.SelectShipping.Endpoint.MapSelectShippingEndpoint(customer);
        Customer.SelectPaymentMethod.Endpoint.MapSelectPaymentMethodEndpoint(customer);
        Customer.Summary.Endpoint.MapSummaryEndpoint(customer);
        Customer.Submit.Endpoint.MapSubmitEndpoint(customer);
        Customer.AcceptDrift.Endpoint.MapAcceptDriftEndpoint(customer);

        var admin = app.MapGroup("/v1/admin/checkout");
        Admin.ListSessions.Endpoint.MapAdminListSessionsEndpoint(admin);
        Admin.ForceExpire.Endpoint.MapAdminForceExpireEndpoint(admin);

        var webhooks = app.MapGroup("/v1");
        Webhooks.PaymentGatewayWebhook.Endpoint.MapPaymentGatewayWebhookEndpoint(webhooks);
        return app;
    }
}

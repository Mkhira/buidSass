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
        services.AddScoped<Primitives.CheckoutAuditEmitter>();

        // Stub gateway / shipping / order-handler are ONLY safe in Development + Test. In any
        // other environment we register NOTHING — DI will then throw at first endpoint
        // invocation that needs the missing service ("unable to resolve IPaymentGateway"),
        // which is the desired fail-fast. Real provider modules (ADR-007 / ADR-008 / spec 011)
        // register their concrete implementations over this gate when introduced.
        //
        // CR review on PR #30 (round 2): an earlier version registered throwing factories.
        // That broke `IEnumerable<IPaymentGateway>` resolution in the webhook endpoint because
        // ASP.NET Core invokes every factory while building the enumerable. Skipping the
        // registration is functionally equivalent (resolve fails) without poisoning enumerables.
        var allowStubs = hostEnvironment.IsDevelopment() || hostEnvironment.IsEnvironment("Test");
        if (allowStubs)
        {
            services.AddSingleton<Primitives.Payment.IPaymentGateway, Primitives.Payment.StubPaymentGateway>();
            services.AddSingleton<Primitives.Shipping.IShippingProvider, Primitives.Shipping.StubShippingProvider>();
            services.AddScoped<BackendApi.Modules.Shared.IOrderFromCheckoutHandler, Primitives.StubOrderFromCheckoutHandler>();
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

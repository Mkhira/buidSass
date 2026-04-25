using BackendApi.Configuration;
using BackendApi.Modules.Orders.Persistence;
using BackendApi.Modules.Orders.Primitives;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace BackendApi.Modules.Orders;

public static class OrdersModule
{
    public static IServiceCollection AddOrdersModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment)
    {
        var connectionString = configuration.ResolveRequiredDefaultConnectionString(hostEnvironment);

        services.AddDbContext<OrdersDbContext>((provider, options) =>
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
            // Project Memory: every new module's AddDbContext must suppress
            // ManyServiceProvidersCreatedWarning or Identity test factory throws.
            options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        });

        services.AddScoped<OrderNumberSequencer>();
        services.AddScoped<CancellationPolicy>();
        services.AddSingleton<ReturnEligibilityEvaluator>();

        // Spec 010 → 011 seam. Replaces StubOrderFromCheckoutHandler — Checkout's submit slice
        // resolves IOrderFromCheckoutHandler via DI, and the LAST registration wins. Spec 010
        // still registers its stub gated on Development/Test; this registration is unconditional
        // so production composes the real handler.
        services.AddScoped<BackendApi.Modules.Shared.IOrderFromCheckoutHandler,
            Internal.CreateFromCheckout.CreateFromCheckoutHandler>();

        if (!hostEnvironment.IsEnvironment("Test"))
        {
            services.AddHostedService<Workers.OutboxDispatcher>();
        }

        return services;
    }

    public static WebApplication UseOrdersModuleEndpoints(this WebApplication app)
    {
        var customer = app.MapGroup("/v1/customer/orders");
        Customer.ListOrders.Endpoint.MapListOrdersEndpoint(customer);
        Customer.GetOrder.Endpoint.MapGetOrderEndpoint(customer);
        Customer.Cancel.Endpoint.MapCancelEndpoint(customer);

        var admin = app.MapGroup("/v1/admin/orders");
        Admin.ListOrders.Endpoint.MapAdminListOrdersEndpoint(admin);
        Admin.GetOrder.Endpoint.MapAdminGetOrderEndpoint(admin);
        Admin.Fulfillment.StartPicking.Endpoint.MapStartPickingEndpoint(admin);
        Admin.Fulfillment.MarkPacked.Endpoint.MapMarkPackedEndpoint(admin);
        Admin.Fulfillment.CreateShipment.Endpoint.MapCreateShipmentEndpoint(admin);
        Admin.Fulfillment.MarkHandedToCarrier.Endpoint.MapMarkHandedToCarrierEndpoint(admin);
        Admin.Fulfillment.MarkDelivered.Endpoint.MapMarkDeliveredEndpoint(admin);
        return app;
    }
}

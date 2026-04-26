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
        services.AddScoped<QuoteNumberSequencer>();
        services.AddScoped<CancellationPolicy>();
        services.AddSingleton<ReturnEligibilityEvaluator>();
        services.AddSingleton<BackendApi.Modules.Observability.OrdersMetrics>();
        services.AddScoped<Internal.CreateFromQuotation.CreateFromQuotationHandler>();

        // Spec 011 ↔ spec 013 refund-state seam — single source of truth used by both the public
        // HTTP endpoint and the in-process adapter.
        services.AddScoped<Internal.AdvanceRefundState.AdvanceRefundStateService>();
        services.AddScoped<BackendApi.Modules.Shared.IOrderRefundStateAdvancer,
            Internal.AdvanceRefundState.OrderRefundStateAdvancerAdapter>();

        // Spec 010 → 011 seam. Replaces StubOrderFromCheckoutHandler — Checkout's submit slice
        // resolves IOrderFromCheckoutHandler via DI, and the LAST registration wins. Spec 010
        // still registers its stub gated on Development/Test; this registration is unconditional
        // so production composes the real handler.
        services.AddScoped<BackendApi.Modules.Shared.IOrderFromCheckoutHandler,
            Internal.CreateFromCheckout.CreateFromCheckoutHandler>();

        // Spec 011 F1 — Checkout's payment-gateway webhook calls this seam after advancing a
        // PaymentAttempt; it advances the corresponding Order's payment_state and emits
        // payment.captured (FR-015) for spec 012's invoice issuance.
        services.AddScoped<BackendApi.Modules.Shared.IOrderPaymentStateHook,
            Internal.PaymentWebhookAdvance.PaymentWebhookAdvanceHandler>();

        if (!hostEnvironment.IsEnvironment("Test"))
        {
            services.AddHostedService<Workers.OutboxDispatcher>();
            services.AddHostedService<Workers.QuotationExpiryWorker>();
            services.AddHostedService<Workers.PaymentFailedRecoveryWorker>();
        }

        return services;
    }

    public static WebApplication UseOrdersModuleEndpoints(this WebApplication app)
    {
        var customer = app.MapGroup("/v1/customer/orders");
        Customer.ListOrders.Endpoint.MapListOrdersEndpoint(customer);
        Customer.GetOrder.Endpoint.MapGetOrderEndpoint(customer);
        Customer.Cancel.Endpoint.MapCancelEndpoint(customer);
        Customer.Reorder.Endpoint.MapReorderEndpoint(customer);
        Customer.ReturnEligibility.Endpoint.MapReturnEligibilityEndpoint(customer);

        var customerQuotes = app.MapGroup("/v1/customer/quotations");
        Customer.Quotations.ListQuotations.Endpoint.MapCustomerListQuotationsEndpoint(customerQuotes);
        Customer.Quotations.GetQuotation.Endpoint.MapCustomerGetQuotationEndpoint(customerQuotes);
        Customer.Quotations.AcceptQuotation.Endpoint.MapCustomerAcceptQuotationEndpoint(customerQuotes);
        Customer.Quotations.RejectQuotation.Endpoint.MapCustomerRejectQuotationEndpoint(customerQuotes);

        var admin = app.MapGroup("/v1/admin/orders");
        Admin.ListOrders.Endpoint.MapAdminListOrdersEndpoint(admin);
        Admin.GetOrder.Endpoint.MapAdminGetOrderEndpoint(admin);
        Admin.GetAudit.Endpoint.MapAdminGetAuditEndpoint(admin);
        Admin.Fulfillment.StartPicking.Endpoint.MapStartPickingEndpoint(admin);
        Admin.Fulfillment.MarkPacked.Endpoint.MapMarkPackedEndpoint(admin);
        Admin.Fulfillment.CreateShipment.Endpoint.MapCreateShipmentEndpoint(admin);
        Admin.Fulfillment.MarkHandedToCarrier.Endpoint.MapMarkHandedToCarrierEndpoint(admin);
        Admin.Fulfillment.MarkDelivered.Endpoint.MapMarkDeliveredEndpoint(admin);
        Admin.Payments.ConfirmBankTransfer.Endpoint.MapAdminConfirmBankTransferEndpoint(admin);
        Admin.Payments.ForceState.Endpoint.MapAdminForceStateEndpoint(admin);
        Admin.FinanceExport.Endpoint.MapAdminFinanceExportEndpoint(admin);

        var adminQuotes = app.MapGroup("/v1/admin/quotations");
        Admin.Quotations.CreateQuotation.Endpoint.MapAdminCreateQuotationEndpoint(adminQuotes);
        Admin.Quotations.SendQuotation.Endpoint.MapAdminSendQuotationEndpoint(adminQuotes);
        Admin.Quotations.ExpireQuotation.Endpoint.MapAdminExpireQuotationEndpoint(adminQuotes);
        Admin.Quotations.ConvertQuotation.Endpoint.MapAdminConvertQuotationEndpoint(adminQuotes);

        var internalOrders = app.MapGroup("/v1/internal/orders");
        Internal.AdvanceRefundState.Endpoint.MapAdvanceRefundStateEndpoint(internalOrders);
        return app;
    }
}

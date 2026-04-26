using BackendApi.Configuration;
using BackendApi.Modules.Returns.Internal.SeedPolicies;
using BackendApi.Modules.Returns.Persistence;
using BackendApi.Modules.Returns.Primitives;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace BackendApi.Modules.Returns;

public static class ReturnsModule
{
    public static IServiceCollection AddReturnsModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment)
    {
        var connectionString = configuration.ResolveRequiredDefaultConnectionString(hostEnvironment);

        services.AddDbContext<ReturnsDbContext>((provider, options) =>
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

        services.AddScoped<ReturnNumberSequencer>();
        services.AddSingleton<ReturnPolicyEvaluator>();
        services.AddSingleton<RefundAmountCalculator>();
        services.AddScoped<ReturnPolicySeeder>();
        // Per-tick outbox dispatch logic — registered scoped so the hosted service can
        // resolve a fresh DbContext per tick AND tests can drive it synchronously (J7, J8).
        services.AddScoped<Workers.ReturnsOutboxDispatchService>();

        if (!hostEnvironment.IsEnvironment("Test"))
        {
            services.AddHostedService<Workers.ReturnsOutboxDispatcher>();
            services.AddHostedService<Workers.RefundRetryWorker>();
        }

        return services;
    }

    public static WebApplication UseReturnsModuleEndpoints(this WebApplication app)
    {
        var customerOrders = app.MapGroup("/v1/customer/orders");
        Customer.SubmitReturn.Endpoint.MapSubmitReturnEndpoint(customerOrders);

        var customerReturns = app.MapGroup("/v1/customer/returns");
        Customer.UploadReturnPhoto.Endpoint.MapUploadReturnPhotoEndpoint(customerReturns);
        Customer.ListReturns.Endpoint.MapListReturnsEndpoint(customerReturns);
        Customer.GetReturn.Endpoint.MapGetReturnEndpoint(customerReturns);

        var adminReturns = app.MapGroup("/v1/admin/returns");
        Admin.ListReturns.Endpoint.MapAdminListReturnsEndpoint(adminReturns);
        Admin.GetReturn.Endpoint.MapAdminGetReturnEndpoint(adminReturns);
        Admin.Approve.Endpoint.MapAdminApproveEndpoint(adminReturns);
        Admin.Reject.Endpoint.MapAdminRejectEndpoint(adminReturns);
        Admin.ApprovePartial.Endpoint.MapAdminApprovePartialEndpoint(adminReturns);
        Admin.MarkReceived.Endpoint.MapAdminMarkReceivedEndpoint(adminReturns);
        Admin.RecordInspection.Endpoint.MapAdminRecordInspectionEndpoint(adminReturns);
        Admin.IssueRefund.Endpoint.MapAdminIssueRefundEndpoint(adminReturns);
        Admin.ForceRefund.Endpoint.MapAdminForceRefundEndpoint(adminReturns);
        Admin.ReturnsExport.Endpoint.MapAdminReturnsExportEndpoint(adminReturns);

        var adminRefunds = app.MapGroup("/v1/admin/refunds");
        Admin.Refunds.Retry.Endpoint.MapAdminRefundRetryEndpoint(adminRefunds);
        Admin.Refunds.ConfirmBankTransfer.Endpoint.MapAdminConfirmBankTransferEndpoint(adminRefunds);

        var adminPolicies = app.MapGroup("/v1/admin/return-policies");
        Admin.ReturnPolicies.Get.Endpoint.MapAdminGetReturnPoliciesEndpoint(adminPolicies);
        Admin.ReturnPolicies.Put.Endpoint.MapAdminPutReturnPolicyEndpoint(adminPolicies);

        return app;
    }
}

using BackendApi.Configuration;
using BackendApi.Modules.TaxInvoices.Persistence;
using BackendApi.Modules.TaxInvoices.Primitives;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace BackendApi.Modules.TaxInvoices;

public static class TaxInvoicesModule
{
    public static IServiceCollection AddTaxInvoicesModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment)
    {
        var connectionString = configuration.ResolveRequiredDefaultConnectionString(hostEnvironment);

        services.AddDbContext<InvoicesDbContext>((provider, options) =>
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

        services.AddScoped<InvoiceNumberSequencer>();
        services.AddScoped<CreditNoteNumberSequencer>();
        services.AddScoped<InvoiceTemplateResolver>();
        services.AddSingleton<BackendApi.Modules.Observability.InvoicesMetrics>();

        // Rendering pipeline + storage adapter (Phase C).
        services.AddScoped<Rendering.HtmlTemplateRenderer>();
        services.AddScoped<Rendering.PdfExporter>();
        services.AddScoped<Rendering.ZatcaQrEmbedder>();
        // LocalFs is dev/test/staging only. Production MUST register the Azure Blob
        // implementation (research R10) so the residency wiring per ADR-010 holds.
        if (hostEnvironment.IsDevelopment() || hostEnvironment.IsEnvironment("Test")
            || hostEnvironment.IsStaging())
        {
            services.AddScoped<Rendering.IInvoiceBlobStore, Rendering.LocalFsInvoiceBlobStore>();
        }
        // CR R2 Major fix — fail-fast at startup in production if no IInvoiceBlobStore is
        // wired. Without this guard the app would happily accept payment.captured events,
        // persist invoices, and queue render jobs that never resolve a blob store; the only
        // sign of trouble would be the worker logs hours later. Throwing here makes the
        // misconfiguration loud at process boot.
        if (hostEnvironment.IsProduction())
        {
            var hasBlobStore = services.Any(d => d.ServiceType == typeof(Rendering.IInvoiceBlobStore));
            if (!hasBlobStore)
            {
                throw new InvalidOperationException(
                    "TaxInvoicesModule requires an IInvoiceBlobStore registration in production. "
                    + "Wire the Azure Blob adapter (research R10 / ADR-010) before AddTaxInvoicesModule.");
            }
        }

        // Issuance handlers (Phase D).
        services.AddScoped<Internal.IssueOnCapture.IssueOnCaptureHandler>();
        services.AddScoped<Internal.IssueCreditNote.IssueCreditNoteHandler>();

        // Spec 012 ↔ spec 013 credit-note seam.
        services.AddScoped<BackendApi.Modules.Shared.ICreditNoteIssuer,
            Internal.IssueCreditNote.CreditNoteIssuerAdapter>();

        if (!hostEnvironment.IsEnvironment("Test"))
        {
            services.AddHostedService<Workers.InvoiceRenderWorker>();
            services.AddHostedService<Workers.PaymentCapturedSubscriber>();
            services.AddHostedService<Workers.InvoicesOutboxDispatcher>();
        }

        return services;
    }

    public static WebApplication UseTaxInvoicesModuleEndpoints(this WebApplication app)
    {
        var customer = app.MapGroup("/v1/customer/orders");
        Customer.GetInvoicePdf.Endpoint.MapGetInvoicePdfEndpoint(customer);
        Customer.GetInvoiceMetadata.Endpoint.MapGetInvoiceMetadataEndpoint(customer);

        var admin = app.MapGroup("/v1/admin/invoices");
        Admin.ListInvoices.Endpoint.MapAdminListInvoicesEndpoint(admin);
        Admin.GetInvoice.Endpoint.MapAdminGetInvoiceEndpoint(admin);
        Admin.GetByNumber.Endpoint.MapAdminGetByNumberEndpoint(admin);
        Admin.PreviewInvoice.Endpoint.MapAdminPreviewInvoiceEndpoint(admin);
        Admin.ResendInvoice.Endpoint.MapAdminResendInvoiceEndpoint(admin);
        Admin.RegenerateInvoice.Endpoint.MapAdminRegenerateInvoiceEndpoint(admin);
        Admin.FinanceExport.Endpoint.MapAdminFinanceExportEndpoint(admin);
        Admin.RenderQueue.ListEndpoint.MapAdminListRenderQueueEndpoint(admin);
        Admin.RenderQueue.RetryEndpoint.MapAdminRetryRenderJobEndpoint(admin);

        var internalGroup = app.MapGroup("/v1/internal");
        Internal.IssueOnCapture.Endpoint.MapIssueOnCaptureEndpoint(internalGroup);
        Internal.IssueCreditNote.Endpoint.MapIssueCreditNoteEndpoint(internalGroup);

        return app;
    }
}

using BackendApi.Configuration;
using BackendApi.Features.Seeding;
using BackendApi.Modules.B2B.Documents;
using BackendApi.Modules.B2B.Documents.PdfTemplates;
using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.B2B.Primitives;
using BackendApi.Modules.B2B.Quotes.Admin.AuthorQuoteDraft;
using BackendApi.Modules.B2B.Quotes.Admin.GetQuoteDetail;
using BackendApi.Modules.B2B.Quotes.Admin.ListQuoteQueue;
using BackendApi.Modules.B2B.Quotes.Admin.PublishQuoteVersion;
using BackendApi.Modules.B2B.Quotes.Customer.DownloadQuoteVersionDocument;
using BackendApi.Modules.B2B.Quotes.Customer.GetMyQuote;
using BackendApi.Modules.B2B.Quotes.Customer.ListMyQuotes;
using BackendApi.Modules.B2B.Quotes.Customer.RequestQuoteFromCart;
using BackendApi.Modules.B2B.Quotes.Customer.RequestQuoteFromProduct;
using BackendApi.Modules.B2B.Quotes.Customer.RequestRevision;
using BackendApi.Modules.B2B.Quotes.Customer.SubmitAcceptance;
using BackendApi.Modules.B2B.Quotes.Customer.WithdrawQuote;
using BackendApi.Modules.B2B.RateLimit;
using BackendApi.Modules.B2B.Seeding;
using BackendApi.Modules.Pdf;
using Microsoft.Extensions.Options;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace BackendApi.Modules.B2B;

/// <summary>
/// Spec 021 Quotes-and-B2B module bootstrap. Phase 1+2 (foundation): DbContext, entities,
/// state machines, cross-module hooks, reference-data seeder. User-story slices (Phase 3+)
/// will register their MediatR handlers + endpoints in follow-up PRs.
/// </summary>
public static class B2BModule
{
    public static IServiceCollection AddB2BModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment)
    {
        var connectionString = configuration.ResolveRequiredDefaultConnectionString(hostEnvironment);

        // Project-memory rule R14: every module's AddDbContext MUST suppress
        // ManyServiceProvidersCreatedWarning. Identity.Tests spins up multiple
        // WebApplicationFactory instances per run; without suppression the warning
        // is upgraded to an error and Identity.Tests breaks. Asserted by
        // scripts/ci/assert-warning-suppressed.sh.
        services.AddDbContext<B2BDbContext>((provider, options) =>
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

        // NOTE: hosted workers (QuoteExpiryWorker, InvitationExpiryWorker per research §R7)
        // will need an IDbContextFactory<B2BDbContext> to construct scopes outside the
        // request pipeline. The registration is deferred to the worker-introducing slice
        // because mixing AddDbContext (scoped) + AddDbContextFactory (singleton) for the
        // same TContext fails ServiceProvider validation at design time (EF Tooling
        // notices the lifetime conflict). The Polish-phase worker task (T041 follow-up)
        // will swap to AddDbContextFactory + a thin scoped wrapper, mirroring the path
        // CMS / Reviews modules will follow when their workers go production.
        services.AddScoped<ISeeder, B2BReferenceDataSeeder>();

        // CompanyInvitation token hashing — plaintext is never persisted; the HMAC-SHA256
        // signing key is bound from configuration (env / Key Vault / user-secrets).
        services.AddOptions<B2BInvitationOptions>()
            .Bind(configuration.GetSection(B2BInvitationOptions.SectionName));
        services.AddSingleton<CompanyInvitationTokenHasher>();

        // Cycle B (US1) — RequestQuoteFromCart slice + the per-customer + per-company
        // rate limiter that backs FR-045. Singleton because the bucket map MUST persist
        // across requests; the limiter has no per-request state.
        // TimeProvider is registered here (rather than in the host) so the module
        // is self-contained — both QuoteRequestRateLimiter and the handler depend on
        // it. Other modules (Verification, Reviews) follow the same pattern. The
        // TryAdd avoids double-registering when more than one module wires it.
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<QuoteRequestRateLimiter>();
        services.AddScoped<RequestQuoteFromCartHandler>();
        services.AddScoped<IValidator<RequestQuoteFromCartRequest>, RequestQuoteFromCartValidator>();

        // Phase 4 (US2) — RequestQuoteFromProduct slice. Reuses the same singleton
        // QuoteRequestRateLimiter as the from-cart slice so per-customer + per-company
        // hourly caps apply to the combined request volume from both surfaces.
        services.AddScoped<RequestQuoteFromProductHandler>();
        services.AddScoped<IValidator<RequestQuoteFromProductRequest>, RequestQuoteFromProductValidator>();

        // Cycle C-1 (US1) — read paths. Both handlers share the visibility helper
        // in Modules/B2B/Quotes/Customer/Visibility/CustomerQuoteVisibility (static —
        // no DI registration needed).
        services.AddScoped<ListMyQuotesHandler>();
        services.AddScoped<IValidator<ListMyQuotesRequest>, ListMyQuotesValidator>();
        services.AddScoped<GetMyQuoteHandler>();

        // Cycle C-2 (US1) — customer-side state mutations. Both reuse
        // CustomerQuoteVisibility for the read-side gate then re-fetch with EF
        // tracking enabled for the actual mutation. Each handler owns its own
        // role-based authority check (§2.5: customer-owner OR companies.admin;
        // §2.6: customer-owner OR buyer).
        services.AddScoped<WithdrawQuoteHandler>();
        services.AddScoped<IValidator<WithdrawQuoteRequest>, WithdrawQuoteValidator>();
        services.AddScoped<RequestRevisionHandler>();
        services.AddScoped<IValidator<RequestRevisionRequest>, RequestRevisionValidator>();

        // Cycle C-3 (US1) — T070 SubmitAcceptance, the launch-MVP linchpin.
        // Routes acceptance to either the approver queue (company.approver_required=true
        // with ≥ 1 approver) or direct-accept + IOrderFromQuoteHandler conversion
        // (per Clarifications Q1). Depends on the cross-module
        // ICustomerVerificationEligibilityQuery (spec 020) for the FR-036 gate and
        // IOrderFromQuoteHandler (spec 011, currently STUBBED until T100). Both
        // shared interfaces are wired by Modules/Shared/ModuleRegistrationExtensions.
        services.AddScoped<SubmitAcceptanceHandler>();
        services.AddScoped<IValidator<SubmitAcceptanceRequest>, SubmitAcceptanceValidator>();

        // Cycle C-tail (US1) — document download. Pure read path; reuses
        // CustomerQuoteVisibility for the visibility gate and IStorageService
        // (registered by Modules/Shared/ModuleRegistrationExtensions) for the
        // signed-URL mint. No separate validator: the only inputs are route
        // parameters (canonicalized in the endpoint).
        services.AddScoped<DownloadQuoteVersionDocumentHandler>();

        // Phase 5 (US3) — admin authoring slices.
        services.AddScoped<ListQuoteQueueHandler>();
        services.AddScoped<GetQuoteDetailHandler>();
        services.AddScoped<AuthorQuoteDraftHandler>();
        services.AddScoped<IValidator<AuthorQuoteDraftRequest>, AuthorQuoteDraftValidator>();
        services.AddScoped<PublishQuoteVersionHandler>();
        services.AddScoped<QuoteVersionPdfRenderer>();

        // Register the spec 021 quote-version PDF template with the platform's
        // PdfTemplateRegistry. The registry is a singleton; we extend it once at
        // module-init via an IHostedService so the registration runs before any
        // request can hit the publish endpoint.
        services.AddHostedService<PdfTemplateRegistrationStartup>();

        return services;
    }

    /// <summary>
    /// Wires the spec 021 HTTP surface. Cycle B (US1) ships only the customer
    /// quote-from-cart slice; subsequent cycles register their slices into the same
    /// MapGroup tree. Route groups follow the same per-audience convention as the
    /// Reviews module (<c>/api/customer/reviews</c>, <c>/api/admin/reviews</c>).
    /// </summary>
    public static WebApplication UseB2BModuleEndpoints(this WebApplication app)
    {
        var customerQuotes = app.MapGroup("/api/customer/quotes");
        customerQuotes.MapRequestQuoteFromCartEndpoint();
        // Phase 4 (US2) — POST /api/customer/quotes/from-product (contract §2.2).
        customerQuotes.MapRequestQuoteFromProductEndpoint();
        // Cycle C-1 — read paths. ListMyQuotes maps to GET / (relative to the group);
        // GetMyQuote maps to GET /{id:guid}. ASP.NET routing prefers the more specific
        // route when both match a request, so the {id:guid} route does not shadow
        // the index list.
        customerQuotes.MapListMyQuotesEndpoint();
        customerQuotes.MapGetMyQuoteEndpoint();
        // Cycle C-2 — customer-side state mutations. Both POST under the same group
        // with route templates that don't collide with the GET handlers.
        customerQuotes.MapWithdrawQuoteEndpoint();
        customerQuotes.MapRequestRevisionEndpoint();
        // Cycle C-3 — T070 SubmitAcceptance. POST on /{id}/submit-acceptance.
        customerQuotes.MapSubmitAcceptanceEndpoint();
        // Cycle C-tail — document download. GET on a deeper route template
        // (/{quoteId}/versions/{versionId}/documents/{locale}) so it doesn't
        // shadow the GET /{id:guid} that GetMyQuote owns.
        customerQuotes.MapDownloadQuoteVersionDocumentEndpoint();

        // Phase 5 (US3) — admin quote endpoints.
        var adminQuotes = app.MapGroup("/api/admin/quotes");
        adminQuotes.MapListQuoteQueueEndpoint();
        adminQuotes.MapGetQuoteDetailEndpoint();
        adminQuotes.MapAuthorQuoteDraftEndpoint();
        adminQuotes.MapPublishQuoteVersionEndpoint();
        return app;
    }
}

/// <summary>
/// Spec 021 — registers the <see cref="QuoteVersionPdfTemplate"/> with the platform's
/// <see cref="PdfTemplateRegistry"/> at host startup. Runs synchronously in
/// <see cref="StartAsync"/>; idempotent (the registry's Register replaces by name).
/// </summary>
internal sealed class PdfTemplateRegistrationStartup : Microsoft.Extensions.Hosting.IHostedService
{
    private readonly PdfTemplateRegistry _registry;

    public PdfTemplateRegistrationStartup(PdfTemplateRegistry registry)
    {
        _registry = registry;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _registry.Register(QuoteVersionPdfTemplate.TemplateName,
            (locale, data) => new QuoteVersionPdfTemplate(locale, data));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

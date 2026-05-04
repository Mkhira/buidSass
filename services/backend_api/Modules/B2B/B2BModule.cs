using BackendApi.Configuration;
using BackendApi.Features.Seeding;
using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.B2B.Primitives;
using BackendApi.Modules.B2B.Quotes.Customer.RequestQuoteFromCart;
using BackendApi.Modules.B2B.RateLimit;
using BackendApi.Modules.B2B.Seeding;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddSingleton<QuoteRequestRateLimiter>();
        services.AddScoped<RequestQuoteFromCartHandler>();
        services.AddScoped<IValidator<RequestQuoteFromCartRequest>, RequestQuoteFromCartValidator>();

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
        return app;
    }
}

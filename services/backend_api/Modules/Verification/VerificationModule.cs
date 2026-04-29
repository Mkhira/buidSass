using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BackendApi.Modules.Verification;

public static class VerificationModule
{
    public static IServiceCollection AddVerificationModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment)
    {
        // Phase 1 (Setup) is intentionally empty.
        //
        // Phase 2 (Foundational) wires:
        //   - AddDbContext<VerificationDbContext>(...) with the
        //     ManyServiceProvidersCreatedWarning suppression (project-memory rule;
        //     mirrors Modules/Cart/CartModule.cs).
        //   - IDbContextFactory<VerificationDbContext> for hosted workers (tasks.md T031).
        //   - VerificationOptions binding via configuration.GetSection(...).
        //   - Cross-module hook implementations from Modules/Shared/.
        //   - Reference-data + dev seeders.
        //
        // Subsequent phases register handlers, validators, workers, authorization
        // policies, and the eligibility-query implementation.
        //
        // See specs/phase-1D/020-verification/tasks.md Phase 2 for the populated state.

        return services;
    }

    public static IEndpointRouteBuilder MapVerificationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        // Phase 1 placeholder. Customer + Admin slice endpoints land in Phases 3-5.
        return endpoints;
    }
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Cms;

/// <summary>
/// DI + endpoint wiring for the CMS vertical-slice module per spec 024.
/// Phase 1 (Setup) ships an empty registration; per-phase slice handlers,
/// workers, subscribers, and persistence land in subsequent phases.
/// </summary>
public static class CmsModule
{
    public static IServiceCollection AddCmsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Phase 1 placeholder. Persistence (Phase 2 T015 ReviewsDbContext analog),
        // ManyServiceProvidersCreatedWarning suppression (project-memory rule),
        // MediatR scan, subscribers, workers, and slice registrations land in
        // later phases per specs/phase-1D/024-cms/tasks.md.
        return services;
    }
}

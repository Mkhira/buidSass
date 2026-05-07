using BackendApi.Modules.Support.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Support;

/// <summary>Hosted workers partial — Phase N (US4 + Phase 10).</summary>
public static partial class SupportModule
{
    static partial void AddSupportWorkers(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(SupportWorkerOptions.SectionName);
        services.Configure<SupportWorkerOptions>(section);

        // Validate at startup so misconfig fails fast.
        var bound = new SupportWorkerOptions();
        section.Bind(bound);
        bound.SlaBreachWatch.Validate($"{SupportWorkerOptions.SectionName}:SlaBreachWatch");
        bound.AutoCloseResolutionWindow.Validate($"{SupportWorkerOptions.SectionName}:AutoCloseResolutionWindow");
        bound.OrphanedAssignmentReclaim.Validate($"{SupportWorkerOptions.SectionName}:OrphanedAssignmentReclaim");

        services.AddHostedService<SlaBreachWatchWorker>();
        services.AddHostedService<AutoCloseResolutionWindowWorker>();
        services.AddHostedService<OrphanedAssignmentReclaimWorker>();
    }
}

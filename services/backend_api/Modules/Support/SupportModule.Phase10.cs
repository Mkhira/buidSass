using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Support;

/// <summary>
/// Phase 10 cross-cutting partial — policy-admin, super-admin redaction,
/// metrics, agent-availability slices, etc. Slice classes register here as
/// they land. Empty by default to keep <see cref="SupportModule.cs"/>'s
/// partial-method calls satisfied.
/// </summary>
public static partial class SupportModule
{
    static partial void AddPolicyAdminSlices(IServiceCollection services)
    {
        // Slices land here as Phase 10 progresses (T116-T120, T136a-T136b, etc.).
    }

    static partial void AddPhase10Slices(IServiceCollection services)
    {
        // Lead force-close (T122), agent-availability toggle (T121),
        // super-admin redaction (T127-T130), retag-category (T124), and
        // metrics (T136a) slices register here.
    }

    static partial void MapPolicyAdminEndpoints(IEndpointRouteBuilder policyAdmin)
    {
        // Endpoint maps land here as Phase 10 progresses.
    }

    static partial void MapPhase10Endpoints(IEndpointRouteBuilder admin)
    {
        // Endpoint maps land here as Phase 10 progresses.
    }
}

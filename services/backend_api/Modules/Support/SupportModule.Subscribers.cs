using BackendApi.Modules.Shared;
using BackendApi.Modules.Support.Subscribers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BackendApi.Modules.Support;

/// <summary>Cross-module subscriber + lifecycle bindings for the Support module.</summary>
public static partial class SupportModule
{
    static partial void AddSupportLifecycleSubscribers(IServiceCollection services)
    {
        // Account-lifecycle handler — registered alongside the existing
        // implementations in B2B / Verification / Reviews. The platform-level
        // lifecycle dispatcher (owned by spec 004 Identity) fans out to every
        // ICustomerAccountLifecycleSubscriber registered, so adding ours does
        // NOT clobber theirs. AddScoped is correct: each event delivery
        // resolves a fresh DbContext-backed handler instance.
        services.AddScoped<CustomerAccountLifecycleHandler>();
        services.AddScoped<ICustomerAccountLifecycleSubscriber>(sp =>
            sp.GetRequiredService<CustomerAccountLifecycleHandler>());

        // Agent-activity fallback — spec 004 Identity replaces this binding
        // when its module loads (TryAdd preserves the production binding).
        services.TryAddScoped<ISupportAgentActivityQuery, NullSupportAgentActivityQuery>();
    }
}

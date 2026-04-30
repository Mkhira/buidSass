using BackendApi.Modules.Reviews.Subscribers;
using BackendApi.Modules.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Reviews;

/// <summary>
/// Companion partial implementations for the US5 cross-module subscriber
/// surface (Phase 7). Wires the refund + account-lifecycle handlers into the
/// in-process bus; the publishers themselves live in specs 013 / 004 — this
/// module only consumes.
/// </summary>
public static partial class ReviewsModule
{
    static partial void AddUs5Slices(IServiceCollection services)
    {
        // Refund subscribers — surfaced as both the typed concrete handler (so
        // tests can resolve directly) and the subscriber interface that the
        // refund publishers fan out to. AddScoped is correct: each event
        // delivery resolves a fresh DbContext-backed handler instance.
        services.AddScoped<RefundCompletedHandler>();
        services.AddScoped<IRefundCompletedSubscriber>(sp =>
            sp.GetRequiredService<RefundCompletedHandler>());

        services.AddScoped<RefundReversedHandler>();
        services.AddScoped<IRefundReversedSubscriber>(sp =>
            sp.GetRequiredService<RefundReversedHandler>());

        // Account-lifecycle handler — registered alongside spec 020's existing
        // AccountLifecycleHandler. The platform's lifecycle bus fans out to
        // every ICustomerAccountLifecycleSubscriber registered, so adding ours
        // does NOT clobber Verification's.
        services.AddScoped<CustomerAccountLifecycleHandler>();
        services.AddScoped<ICustomerAccountLifecycleSubscriber>(sp =>
            sp.GetRequiredService<CustomerAccountLifecycleHandler>());
    }
}

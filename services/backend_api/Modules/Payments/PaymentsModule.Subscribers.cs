using BackendApi.Modules.Payments.Subscribers;
using BackendApi.Modules.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Payments;

public static partial class PaymentsModule
{
    static partial void AddSubscribers(IServiceCollection services)
    {
        // Bridge cross-module hooks (Modules/Shared) into Payments handlers.
        // The hooks intentionally live in Modules/Shared so Orders + Checkout
        // never form a module dependency cycle (project-memory rule).
        services.AddScoped<OrderLifecycleSubscriber>();
        services.AddScoped<IOrderLifecycleSubscriber>(sp => sp.GetRequiredService<OrderLifecycleSubscriber>());
    }
}

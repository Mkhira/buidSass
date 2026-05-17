using BackendApi.Modules.Payments.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Payments;

public static partial class PaymentsModule
{
    // Service registrations factored out so the slice/provider/worker partials
    // stay reviewer-friendly. Call site is wired by AddSlices via AddPhase2Slices
    // (it runs first and pulls these in).
    public static IServiceCollection AddPaymentsServices(this IServiceCollection services)
    {
        services.AddScoped<PaymentTransitionService>();
        services.AddScoped<ReconciliationMatcher>();
        return services;
    }
}

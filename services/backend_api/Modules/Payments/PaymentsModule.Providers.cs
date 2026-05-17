using BackendApi.Modules.Payments.Providers;
using BackendApi.Modules.Payments.Providers.HyperPay;
using BackendApi.Modules.Payments.Providers.Kashier;
using BackendApi.Modules.Payments.Providers.Paymob;
using BackendApi.Modules.Payments.Providers.Tabby;
using BackendApi.Modules.Payments.Providers.Tamara;
using BackendApi.Modules.Payments.Providers.Tap;
using BackendApi.Modules.Payments.Providers.Valu;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Payments;

/// <summary>
/// Provider registration. Each provider is registered both as its concrete
/// type (for direct lookup) and behind <see cref="IPaymentProvider"/> (for
/// iteration in workers like the daily reconciliation job).
/// </summary>
public static partial class PaymentsModule
{
    static partial void AddProviders(IServiceCollection services)
    {
        // Concrete singletons so HttpClient typed-client wiring can attach
        // later without churning the registrations.
        services.AddSingleton<HyperPayProvider>();
        services.AddSingleton<TapProvider>();
        services.AddSingleton<PaymobProvider>();
        services.AddSingleton<KashierProvider>();
        services.AddSingleton<TabbyProvider>();
        services.AddSingleton<TamaraProvider>();
        services.AddSingleton<ValuProvider>();

        // Iteration surface (used by ProviderRegistry + DailyReconciliationJob).
        services.AddSingleton<IPaymentProvider>(sp => sp.GetRequiredService<HyperPayProvider>());
        services.AddSingleton<IPaymentProvider>(sp => sp.GetRequiredService<TapProvider>());
        services.AddSingleton<IPaymentProvider>(sp => sp.GetRequiredService<PaymobProvider>());
        services.AddSingleton<IPaymentProvider>(sp => sp.GetRequiredService<KashierProvider>());
        services.AddSingleton<IPaymentProvider>(sp => sp.GetRequiredService<TabbyProvider>());
        services.AddSingleton<IPaymentProvider>(sp => sp.GetRequiredService<TamaraProvider>());
        services.AddSingleton<IPaymentProvider>(sp => sp.GetRequiredService<ValuProvider>());

        services.AddSingleton<ProviderRegistry>();
        services.AddSingleton<IProviderSecretResolver, ProviderSecretResolver>();
    }
}

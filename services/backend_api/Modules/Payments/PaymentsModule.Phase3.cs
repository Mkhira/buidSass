using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Payments;

/// <summary>
/// Phase 3 — BNPL providers + BNPL flow inside <see cref="Features.CreatePayment"/>
/// (the BNPL path is already handled by the CreatePayment handler since it
/// dispatches on the state machine's initial state). No additional services
/// or endpoints are required.
/// </summary>
public static partial class PaymentsModule
{
    static partial void AddPhase3Slices(IServiceCollection services)
    {
        // No-op: BNPL providers are registered in PaymentsModule.Providers.cs.
        // BNPL flow handled inline by CreatePaymentHandler via the state machine.
    }
}

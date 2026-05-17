using BackendApi.Modules.Payments.Domain;
using BackendApi.Modules.Payments.Domain.StateMachines;
using BackendApi.Modules.Payments.Persistence;
using BackendApi.Modules.Payments.Primitives;
using BackendApi.Modules.Payments.Providers;
using BackendApi.Modules.Payments.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Payments.Workers;

/// <summary>
/// T020 — drives <c>pending_authorization</c> payments through the provider's
/// authorize-and-capture call, applying the BR-5 retry policy: 5xx + network
/// errors retry up to 3 attempts (1s/3s/9s backoff); 4xx never retries.
/// Each iteration is idempotent: only one provider call per Payment row even
/// if the worker restarts mid-iteration (BR-3).
/// </summary>
public sealed class PaymentDispatchWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<PaymentDispatchWorker> logger,
    TimeProvider clock) : BackgroundService
{
    private readonly TimeProvider _clock = clock;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "PaymentDispatchWorker iteration failed");
            }
            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { /* shutdown */ }
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var providers = scope.ServiceProvider.GetRequiredService<ProviderRegistry>();
        var transitions = scope.ServiceProvider.GetRequiredService<PaymentTransitionService>();

        var pending = await db.Payments
            .Where(p => p.State == PaymentsConstants.PaymentStates.PendingAuthorization
                && p.DeletedAt == null
                && p.ProviderId != null)
            .OrderBy(p => p.CreatedAt)
            .Take(25)
            .ToListAsync(ct);

        foreach (var payment in pending)
        {
            if (payment.ProviderId is null) continue;
            if (!providers.TryResolve(payment.ProviderId, out var provider) || provider is null) continue;

            CreatePaymentResult? result = null;
            var attempts = 0;
            var delays = new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(9) };
            while (attempts < 3)
            {
                attempts++;
                try
                {
                    result = await provider.CreatePaymentAsync(new CreatePaymentDispatch(
                        payment.Id, payment.OrderId, payment.MarketCode, payment.Method,
                        payment.Amount, payment.Currency, payment.IdempotencyKey,
                        CustomerRef: payment.CustomerId.ToString("N"),
                        RecipientNameRedacted: "redacted",
                        RecipientPhoneMaskedLast4: "0000"), ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Provider call threw for payment {PaymentId}", payment.Id);
                    result = new CreatePaymentResult(false, false, null, null, null, ex.Message, 500);
                }

                // BR-5 — only retry on 5xx + network. 4xx terminal.
                if (result is null || (result.StatusCode is int sc && sc >= 400 && sc < 500))
                {
                    break;
                }
                if (result.Success)
                {
                    break;
                }
                await Task.Delay(delays[attempts - 1], ct);
            }

            if (result is null)
            {
                await transitions.MarkFailedAsync(db, payment, PaymentsConstants.FailedReasons.ProviderUnavailable, ct);
                continue;
            }

            if (!result.Success)
            {
                var reason = (result.StatusCode is int sc4 && sc4 >= 400 && sc4 < 500)
                    ? PaymentsConstants.FailedReasons.Declined
                    : PaymentsConstants.FailedReasons.ProviderUnavailable;
                await transitions.MarkFailedAsync(db, payment, reason, ct);
                continue;
            }

            // Fail-fast: advancing state without a ProviderMessageId would
            // make the payment invisible to PaymentsWebhookHandler and the
            // daily ReconciliationMatcher (both correlate on this field).
            if (string.IsNullOrWhiteSpace(result.ProviderMessageId))
            {
                logger.LogWarning(
                    "Provider {Provider} returned success with empty ProviderMessageId for payment {PaymentId}; marking failed",
                    payment.ProviderId, payment.Id);
                await transitions.MarkFailedAsync(db, payment, PaymentsConstants.FailedReasons.ProviderUnavailable, ct);
                continue;
            }

            payment.ProviderMessageId = result.ProviderMessageId;
            if (result.CapturedSynchronously)
            {
                await transitions.CaptureAsync(db, payment, ct);
            }
            else
            {
                // For pending_authorization paths that returned non-sync success,
                // mark authorized; the webhook will capture later.
                PaymentStateMachine.EnsureTransition(payment.State, PaymentsConstants.PaymentStates.Authorized);
                payment.State = PaymentsConstants.PaymentStates.Authorized;
                payment.AuthorizedAt = _clock.GetUtcNow();
                payment.UpdatedAt = _clock.GetUtcNow();
                await db.SaveChangesAsync(ct);
            }
        }
    }
}

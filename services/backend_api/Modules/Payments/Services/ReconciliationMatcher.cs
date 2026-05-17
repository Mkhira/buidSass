using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Payments.Domain;
using BackendApi.Modules.Payments.Persistence;
using BackendApi.Modules.Payments.Primitives;
using BackendApi.Modules.Payments.Providers;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Payments.Services;

/// <summary>
/// Matches one provider's settlement-ledger snapshot for one date against the
/// internal payments table, classifying each mismatch into one of four
/// reasons (AC-29 / AC-30 / AC-31): <c>orphan_provider_row</c>,
/// <c>missing_on_provider</c>, <c>amount_mismatch</c>, <c>currency_mismatch</c>.
/// Each exception is persisted + audit-emitted.
/// </summary>
public sealed class ReconciliationMatcher(
    PaymentsDbContext db,
    IAuditEventPublisher auditPublisher,
    TimeProvider clock)
{
    public async Task<(int Matched, int Exceptions)> MatchAsync(
        IPaymentProvider provider, SettlementLedger ledger, Guid runId, CancellationToken ct)
    {
        var date = ledger.Date;
        var internalPayments = await db.Payments
            .Where(p => p.DeletedAt == null
                && p.ProviderId == provider.ProviderId
                && p.State == PaymentsConstants.PaymentStates.Captured
                && p.CapturedAt != null
                && DateOnly.FromDateTime(p.CapturedAt!.Value.UtcDateTime) == date)
            .ToListAsync(ct);

        var internalByMsg = internalPayments
            .Where(p => p.ProviderMessageId != null)
            .GroupBy(p => p.ProviderMessageId!)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var ledgerByMsg = ledger.Rows
            .GroupBy(r => r.ProviderMessageId)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        int matched = 0, exceptions = 0;

        // missing_on_provider — internal captured row with no ledger row.
        foreach (var (msgId, payment) in internalByMsg)
        {
            if (!ledgerByMsg.ContainsKey(msgId))
            {
                await OpenExceptionAsync(runId, provider.ProviderId,
                    PaymentsConstants.ReconciliationExceptionReasons.MissingOnProvider,
                    internalPaymentId: payment.Id,
                    internalAmount: payment.Amount, providerAmount: null,
                    providerLedgerRowJson: null, ct);
                exceptions++;
            }
        }

        // orphan + amount + currency cases driven by ledger rows.
        foreach (var (msgId, row) in ledgerByMsg)
        {
            if (!internalByMsg.TryGetValue(msgId, out var payment))
            {
                await OpenExceptionAsync(runId, provider.ProviderId,
                    PaymentsConstants.ReconciliationExceptionReasons.OrphanProviderRow,
                    internalPaymentId: null,
                    internalAmount: null, providerAmount: row.Amount,
                    providerLedgerRowJson: JsonSerializer.Serialize(row), ct);
                exceptions++;
                continue;
            }
            if (!string.Equals(payment.Currency, row.Currency, StringComparison.Ordinal))
            {
                await OpenExceptionAsync(runId, provider.ProviderId,
                    PaymentsConstants.ReconciliationExceptionReasons.CurrencyMismatch,
                    internalPaymentId: payment.Id,
                    internalAmount: payment.Amount, providerAmount: row.Amount,
                    providerLedgerRowJson: JsonSerializer.Serialize(row), ct);
                exceptions++;
                continue;
            }
            if (payment.Amount != row.Amount)
            {
                await OpenExceptionAsync(runId, provider.ProviderId,
                    PaymentsConstants.ReconciliationExceptionReasons.AmountMismatch,
                    internalPaymentId: payment.Id,
                    internalAmount: payment.Amount, providerAmount: row.Amount,
                    providerLedgerRowJson: JsonSerializer.Serialize(row), ct);
                exceptions++;
                continue;
            }
            matched++;
        }

        return (matched, exceptions);
    }

    private async Task OpenExceptionAsync(
        Guid runId, string providerId, string reason,
        Guid? internalPaymentId, decimal? internalAmount, decimal? providerAmount,
        string? providerLedgerRowJson, CancellationToken ct)
    {
        var ex = new ReconciliationException
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            Reason = reason,
            ProviderId = providerId,
            ProviderLedgerRowJson = providerLedgerRowJson,
            InternalPaymentId = internalPaymentId,
            InternalAmount = internalAmount,
            ProviderAmount = providerAmount,
            State = PaymentsConstants.ReconciliationExceptionStates.Open,
            CreatedAt = clock.GetUtcNow(),
            UpdatedAt = clock.GetUtcNow(),
        };
        db.ReconciliationExceptions.Add(ex);
        await db.SaveChangesAsync(ct);
        await auditPublisher.PublishAsync(new AuditEvent(
            ActorId: Guid.Empty, ActorRole: "system",
            Action: PaymentsConstants.AuditActions.ReconciliationExceptionOpened,
            EntityType: "ReconciliationException", EntityId: ex.Id,
            BeforeState: null,
            AfterState: new { reason, providerId, internalPaymentId, internalAmount, providerAmount },
            Reason: null), ct);
    }
}

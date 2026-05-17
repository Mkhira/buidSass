using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Payments.Domain;
using BackendApi.Modules.Payments.Persistence;
using BackendApi.Modules.Payments.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Payments.Workers;

/// <summary>
/// T013 — nightly PCI scope monitor. Queries <c>information_schema.columns</c>
/// for any column in the <c>payments</c> schema whose name matches a
/// cardholder-shape pattern (PAN, CVV, track1, track2, primary_account_number,
/// card_number, card_pin, card_expiry). Expected result: zero rows. Any
/// non-zero result raises a P0 alert via <c>pci_scope.config_changed</c>.
/// </summary>
public sealed class PciScopeMonitor(
    IServiceScopeFactory scopeFactory,
    ILogger<PciScopeMonitor> logger,
    TimeProvider clock) : BackgroundService
{
    // Run hourly so any schema drift is caught quickly.
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "PciScopeMonitor iteration failed");
            }
            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { /* shutdown */ }
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditEventPublisher>();

        // SQL run via raw connection — EF can't model information_schema natively.
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT table_name, column_name FROM information_schema.columns
WHERE table_schema = 'payments'
AND column_name ~* '(pan|primary_account_number|card_number|cvv|cvc|track1|track2|magstripe|card_pin|card_expiry)';";
        var hits = new List<(string Table, string Column)>();
        using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                hits.Add((reader.GetString(0), reader.GetString(1)));
            }
        }
        if (hits.Count == 0) return;

        var summary = string.Join("; ", hits.Select(h => $"{h.Table}.{h.Column}"));
        logger.LogCritical("PCI scope drift detected: {Summary}", summary);
        db.PciScopeEvents.Add(new PciScopeEvent
        {
            Id = Guid.NewGuid(),
            EventKind = PaymentsConstants.PciScopeEventKinds.SchemaDriftDetected,
            ChangedBy = Guid.Empty,
            ChangeSummary = $"cardholder-shape column detected: {summary}",
            CreatedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
        await audit.PublishAsync(new AuditEvent(
            ActorId: Guid.Empty, ActorRole: "system",
            Action: PaymentsConstants.AuditActions.PciScopeConfigChanged,
            EntityType: "PciScope", EntityId: Guid.Empty,
            BeforeState: null, AfterState: new { hits = hits.Select(h => h.ToString()).ToArray() },
            Reason: "PCI scope schema drift"), ct);
    }
}

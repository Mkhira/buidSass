using BackendApi.Modules.Shipping.Persistence;
using BackendApi.Modules.Shipping.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Shipping.Features.ProviderRouting;

public sealed record SetProviderRoutingCommand(
    string MarketCode,
    Guid MethodId,
    string PrimaryProviderId,
    string? BackupProviderId,
    bool AutoFailoverEnabled,
    int FailoverThresholdPct,
    int FailoverWindowMinutes,
    Guid ActorId) : IRequest;

public sealed class SetProviderRoutingHandler(ShippingDbContext db, TimeProvider clock)
    : IRequestHandler<SetProviderRoutingCommand>
{
    public async Task Handle(SetProviderRoutingCommand cmd, CancellationToken ct)
    {
        if (!ShippingConstants.Markets.IsValid(cmd.MarketCode))
        {
            throw new ArgumentException("MarketCode must be SA or EG", nameof(cmd));
        }
        if (string.Equals(cmd.PrimaryProviderId, cmd.BackupProviderId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Primary and backup providers must differ (V-5).");
        }
        if (cmd.FailoverThresholdPct is < 10 or > 90)
        {
            throw new ArgumentException("FailoverThresholdPct must be in [10,90] (V-5).");
        }
        // Mirror the DB-level CHECK so we fail fast with a useful message
        // instead of bubbling a constraint violation.
        if (cmd.FailoverWindowMinutes is < 1 or > 60)
        {
            throw new ArgumentException("FailoverWindowMinutes must be in [1,60].");
        }
        // BR-11 — auto-failover REQUIRES a backup provider (DB also enforces this).
        if (cmd.AutoFailoverEnabled && string.IsNullOrWhiteSpace(cmd.BackupProviderId))
        {
            throw new ArgumentException(
                "AutoFailoverEnabled=true requires a BackupProviderId (BR-11).");
        }
        if (!ShippingConstants.Providers.SupportsMarket(cmd.PrimaryProviderId, cmd.MarketCode))
        {
            throw new ArgumentException("Primary provider does not support this market.");
        }
        if (cmd.BackupProviderId is not null
            && !ShippingConstants.Providers.SupportsMarket(cmd.BackupProviderId, cmd.MarketCode))
        {
            throw new ArgumentException("Backup provider does not support this market.");
        }

        // Verify the method actually exists for this market — without this,
        // typos in MethodId silently create an orphan routing row.
        var methodOwningMarket = await db.ShippingMethods
            .Where(m => m.Id == cmd.MethodId && m.DeletedAt == null)
            .Select(m => (string?)m.MarketCode)
            .FirstOrDefaultAsync(ct);
        if (methodOwningMarket is null)
        {
            throw new InvalidOperationException(
                $"Shipping method '{cmd.MethodId}' not found.");
        }
        if (!string.Equals(methodOwningMarket, cmd.MarketCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Shipping method '{cmd.MethodId}' belongs to market '{methodOwningMarket}', not '{cmd.MarketCode}'.");
        }

        var existing = await db.ProviderRoutings
            .FirstOrDefaultAsync(r => r.MarketCode == cmd.MarketCode && r.MethodId == cmd.MethodId, ct);

        if (existing is null)
        {
            db.ProviderRoutings.Add(new Domain.ProviderRouting
            {
                MarketCode = cmd.MarketCode,
                MethodId = cmd.MethodId,
                PrimaryProviderId = cmd.PrimaryProviderId,
                BackupProviderId = cmd.BackupProviderId,
                AutoFailoverEnabled = cmd.AutoFailoverEnabled,
                FailoverThresholdPct = cmd.FailoverThresholdPct,
                FailoverWindowMinutes = cmd.FailoverWindowMinutes,
                UpdatedAt = clock.GetUtcNow(),
            });
        }
        else
        {
            existing.PrimaryProviderId = cmd.PrimaryProviderId;
            existing.BackupProviderId = cmd.BackupProviderId;
            existing.AutoFailoverEnabled = cmd.AutoFailoverEnabled;
            existing.FailoverThresholdPct = cmd.FailoverThresholdPct;
            existing.FailoverWindowMinutes = cmd.FailoverWindowMinutes;
            existing.UpdatedAt = clock.GetUtcNow();
        }
        await db.SaveChangesAsync(ct);
    }
}

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
        if (!ShippingConstants.Providers.SupportsMarket(cmd.PrimaryProviderId, cmd.MarketCode))
        {
            throw new ArgumentException("Primary provider does not support this market.");
        }
        if (cmd.BackupProviderId is not null
            && !ShippingConstants.Providers.SupportsMarket(cmd.BackupProviderId, cmd.MarketCode))
        {
            throw new ArgumentException("Backup provider does not support this market.");
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

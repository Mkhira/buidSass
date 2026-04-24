using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Inventory.Internal.Reservations.Release;
using BackendApi.Modules.Inventory.Persistence;
using BackendApi.Modules.Inventory.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BackendApi.Modules.Inventory.Workers;

public sealed class ReservationReleaseWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<InventoryOptions> options,
    ILogger<ReservationReleaseWorker> logger) : BackgroundService
{
    private static readonly Guid WorkerActorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly IOptions<InventoryOptions> _options = options;
    private readonly ILogger<ReservationReleaseWorker> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReleaseExpiredReservationsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "inventory.reservation-release-worker.cycle-failed");
            }

            var intervalSeconds = Math.Max(1, _options.Value.ReservationReleaseWorkerIntervalSeconds);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ReleaseExpiredReservationsAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var atsCalculator = scope.ServiceProvider.GetRequiredService<AtsCalculator>();
        var bucketMapper = scope.ServiceProvider.GetRequiredService<BucketMapper>();
        var availabilityEventEmitter = scope.ServiceProvider.GetRequiredService<AvailabilityEventEmitter>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditEventPublisher>();
        var releaseLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("InventoryReservationReleaseWorker");

        var nowUtc = DateTimeOffset.UtcNow;
        var expiredIds = await db.InventoryReservations
            .AsNoTracking()
            .Where(x => x.Status == "active" && x.ExpiresAt <= nowUtc)
            .OrderBy(x => x.ExpiresAt)
            .Select(x => x.Id)
            .Take(200)
            .ToListAsync(cancellationToken);

        foreach (var reservationId in expiredIds)
        {
            var result = await Handler.HandleAsync(
                reservationId,
                WorkerActorId,
                "inventory.reservation.ttl_expired",
                db,
                atsCalculator,
                bucketMapper,
                availabilityEventEmitter,
                audit,
                releaseLogger,
                cancellationToken);

            if (!result.IsSuccess)
            {
                _logger.LogDebug(
                    "inventory.reservation-release-worker.release-skipped reservationId={ReservationId} reasonCode={ReasonCode}",
                    reservationId,
                    result.ReasonCode);
            }
        }
    }
}

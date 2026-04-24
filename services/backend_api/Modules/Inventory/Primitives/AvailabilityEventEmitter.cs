using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Inventory.Primitives;

public sealed record AvailabilityChangedEvent(
    Guid ProductId,
    Guid WarehouseId,
    string OldBucket,
    string NewBucket,
    DateTimeOffset OccurredAt);

public sealed class AvailabilityEventEmitter(ILogger<AvailabilityEventEmitter> logger)
{
    private readonly ILogger<AvailabilityEventEmitter> _logger = logger;
    private readonly ConcurrentQueue<AvailabilityChangedEvent> _events = new();

    public Task EmitIfChangedAsync(
        Guid productId,
        Guid warehouseId,
        string oldBucket,
        string newBucket,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        if (string.Equals(oldBucket, newBucket, StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        var evt = new AvailabilityChangedEvent(productId, warehouseId, oldBucket, newBucket, occurredAt);
        _events.Enqueue(evt);

        while (_events.Count > 2_000 && _events.TryDequeue(out _))
        {
        }

        // TODO(spec 011/012): replace in-proc logging with real bus publish when event infra lands.
        _logger.LogInformation(
            "inventory.availability_changed warehouseId={WarehouseId} productId={ProductId} oldBucket={OldBucket} newBucket={NewBucket}",
            warehouseId,
            productId,
            oldBucket,
            newBucket);

        _logger.LogInformation(
            "product.availability.changed warehouseId={WarehouseId} productId={ProductId} oldBucket={OldBucket} newBucket={NewBucket}",
            warehouseId,
            productId,
            oldBucket,
            newBucket);

        return Task.CompletedTask;
    }

    public IReadOnlyList<AvailabilityChangedEvent> Snapshot()
    {
        return _events.ToArray();
    }

    public void Clear()
    {
        while (_events.TryDequeue(out _))
        {
        }
    }
}

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace BackendApi.Modules.Observability;

public sealed class InventoryMetrics : IDisposable
{
    private readonly Meter _meter = new("BackendApi.Inventory");
    private readonly Counter<long> _reservationConflicts;
    private readonly Histogram<double> _reservationDurationMs;
    private readonly ConcurrentDictionary<string, int> _atsByWarehouseAndProduct = new(StringComparer.OrdinalIgnoreCase);

    public InventoryMetrics()
    {
        _reservationConflicts = _meter.CreateCounter<long>(
            "inventory_reservation_conflicts_total",
            unit: "conflicts",
            description: "Total number of reservation conflicts grouped by warehouse and product.");

        _reservationDurationMs = _meter.CreateHistogram<double>(
            "inventory_reservation_duration_ms",
            unit: "ms",
            description: "Duration of reservation create operations in milliseconds.");

        _ = _meter.CreateObservableGauge(
            "inventory_ats_gauge",
            ObserveAts,
            unit: "units",
            description: "Current available-to-sell (ATS) by warehouse and product.");
    }

    public void IncrementReservationConflict(Guid warehouseId, Guid productId)
    {
        _reservationConflicts.Add(
            1,
            new KeyValuePair<string, object?>("warehouse_id", warehouseId),
            new KeyValuePair<string, object?>("product_id", productId));
    }

    public void RecordReservationDuration(double durationMs, Guid warehouseId, Guid productId, string outcome)
    {
        _reservationDurationMs.Record(
            Math.Max(0, durationMs),
            new KeyValuePair<string, object?>("warehouse_id", warehouseId),
            new KeyValuePair<string, object?>("product_id", productId),
            new KeyValuePair<string, object?>("outcome", outcome));
    }

    public void ObserveAts(Guid warehouseId, Guid productId, int ats)
    {
        _atsByWarehouseAndProduct[$"{warehouseId:D}:{productId:D}"] = ats;
    }

    public void Dispose()
    {
        _meter.Dispose();
    }

    private IEnumerable<Measurement<int>> ObserveAts()
    {
        foreach (var (key, ats) in _atsByWarehouseAndProduct)
        {
            var parts = key.Split(':', 2, StringSplitOptions.TrimEntries);
            var warehouseId = parts[0];
            var productId = parts.Length > 1 ? parts[1] : string.Empty;

            yield return new Measurement<int>(
                ats,
                new KeyValuePair<string, object?>("warehouse_id", warehouseId),
                new KeyValuePair<string, object?>("product_id", productId));
        }
    }
}

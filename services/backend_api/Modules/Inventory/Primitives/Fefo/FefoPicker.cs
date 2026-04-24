using BackendApi.Modules.Inventory.Entities;

namespace BackendApi.Modules.Inventory.Primitives.Fefo;

public sealed class FefoPicker
{
    public InventoryBatch? PickBatch(IEnumerable<InventoryBatch> batches)
    {
        return batches
            .Where(b => string.Equals(b.Status, "active", StringComparison.OrdinalIgnoreCase) && b.QtyOnHand > 0)
            .OrderBy(b => b.ExpiryDate)
            .ThenBy(b => b.Id)
            .FirstOrDefault();
    }
}

using BackendApi.Modules.Catalog.Entities;

namespace BackendApi.Modules.Cart.Primitives;

/// <summary>
/// Enforces FR-007: min_order_qty + max_per_order from catalog product metadata. A returned
/// reasonCode short-circuits the add/update endpoint; null means the qty is accepted.
/// A zero bound means "no limit" — catalog's agreed sentinel.
/// </summary>
public static class QtyBoundsValidator
{
    public sealed record Result(bool Ok, string? ReasonCode, string? Detail);

    /// <summary>Absolute safety ceiling. Cart cannot be asked to reserve more than this per line regardless of catalog settings.</summary>
    public const int HardCeiling = 10_000;

    public static Result Validate(Product product, int qty)
    {
        if (qty < 1)
        {
            return new Result(false, "cart.below_min_qty", "qty must be at least 1.");
        }
        if (qty > HardCeiling)
        {
            return new Result(false, "cart.above_max_qty", $"qty exceeds the cart's hard ceiling of {HardCeiling}.");
        }
        if (product.MinOrderQty > 0 && qty < product.MinOrderQty)
        {
            return new Result(false, "cart.below_min_qty",
                $"Product requires a minimum quantity of {product.MinOrderQty}.");
        }
        if (product.MaxPerOrder > 0 && qty > product.MaxPerOrder)
        {
            return new Result(false, "cart.above_max_qty",
                $"Product caps per-order quantity at {product.MaxPerOrder}.");
        }
        // Defence-in-depth: the catalog CHECK constraint already rejects min > max, but if an
        // inconsistent row sneaks through (migration rollback, manual SQL, etc.) the cart layer
        // must still surface a coherent error rather than quietly succeed.
        if (product.MaxPerOrder > 0 && product.MinOrderQty > product.MaxPerOrder)
        {
            return new Result(false, "cart.invalid_qty_bounds",
                $"Product qty bounds are inconsistent (min={product.MinOrderQty}, max={product.MaxPerOrder}).");
        }
        return new Result(true, null, null);
    }
}

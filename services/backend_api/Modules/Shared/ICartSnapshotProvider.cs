namespace BackendApi.Modules.Shared;

/// <summary>
/// Spec 021 → spec 009 cart-snapshot hook (research §R4). Used by quote-from-cart (US1)
/// to atomically snapshot the buyer's current cart and clear it so subsequent cart edits
/// do NOT modify the in-flight quote (FR-010).
///
/// Atomicity model:
/// <list type="bullet">
///   <item>The snapshot read + cart clear happen inside the cart's own transaction (the
///         implementer's). The returned snapshot is then persisted into
///         <c>quotes.originating_cart_snapshot</c> by a separate quote-side transaction.</item>
///   <item>If the quote-side persistence fails after the cart was cleared, the buyer's
///         cart is empty and the quote does not exist — accepted minor failure mode
///         per research §R4. The customer-facing error message MUST be localized.</item>
///   <item>An empty cart returns an empty snapshot (no error — caller validates with
///         <c>quote.cart_empty</c>).</item>
/// </list>
/// </summary>
public interface ICartSnapshotProvider
{
    ValueTask<CartSnapshot> SnapshotAndClearAsync(
        Guid customerId,
        CancellationToken cancellationToken);
}

public sealed record CartSnapshot(
    IReadOnlyList<CartSnapshotLine> Lines,
    DateTimeOffset SnapshottedAt);

public sealed record CartSnapshotLine(
    string Sku,
    int Quantity,
    string? LineNote);

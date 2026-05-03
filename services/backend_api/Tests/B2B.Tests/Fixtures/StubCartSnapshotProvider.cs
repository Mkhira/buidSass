using BackendApi.Modules.Shared;

namespace B2B.Tests.Fixtures;

/// <summary>
/// Spec 021 T053 — test-only stub for <see cref="ICartSnapshotProvider"/>.
///
/// Tests pre-seed <see cref="SnapshotsByCustomer"/> with the cart contents the call
/// should "snapshot and clear". The stub returns the seeded snapshot and removes it
/// from the dictionary (simulating cart-clear). An unseeded customer returns an empty
/// snapshot — matching the production contract that "empty cart returns empty snapshot,
/// caller validates with quote.cart_empty".
/// </summary>
public sealed class StubCartSnapshotProvider : ICartSnapshotProvider
{
    public Dictionary<Guid, IReadOnlyList<CartSnapshotLine>> SnapshotsByCustomer { get; } = new();

    public List<Guid> ClearedCustomerIds { get; } = new();

    public ValueTask<CartSnapshot> SnapshotAndClearAsync(Guid customerId, CancellationToken ct)
    {
        IReadOnlyList<CartSnapshotLine> lines = Array.Empty<CartSnapshotLine>();
        if (SnapshotsByCustomer.Remove(customerId, out var seeded))
        {
            lines = seeded;
        }
        ClearedCustomerIds.Add(customerId);
        return ValueTask.FromResult(new CartSnapshot(lines, DateTimeOffset.UtcNow));
    }
}

using BackendApi.Modules.Inventory.Entities;
using BackendApi.Modules.Inventory.Primitives.Fefo;
using FluentAssertions;
using Inventory.Tests.Infrastructure;

namespace Inventory.Tests.Unit;

[Collection("inventory-fixture")]
public sealed class FefoPickerTests
{
    [Fact]
    public void PickBatch_ReturnsNearestExpiryActiveBatch()
    {
        var sut = new FefoPicker();
        var firstId = Guid.NewGuid();
        var laterId = Guid.NewGuid();
        var batches = new[]
        {
            new InventoryBatch { Id = laterId, Status = "active", QtyOnHand = 5, ExpiryDate = new DateOnly(2028, 1, 1) },
            new InventoryBatch { Id = firstId, Status = "active", QtyOnHand = 5, ExpiryDate = new DateOnly(2027, 1, 1) },
        };

        var picked = sut.PickBatch(batches);

        picked.Should().NotBeNull();
        picked!.Id.Should().Be(firstId);
    }

    [Fact]
    public void PickBatch_TieBreakerUsesLowestBatchIdForSameExpiry()
    {
        var sut = new FefoPicker();
        var lowerId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var higherId = Guid.Parse("00000000-0000-0000-0000-0000000000ff");
        var expiry = new DateOnly(2027, 1, 1);

        var picked = sut.PickBatch(
        [
            new InventoryBatch { Id = higherId, Status = "active", QtyOnHand = 10, ExpiryDate = expiry },
            new InventoryBatch { Id = lowerId, Status = "active", QtyOnHand = 10, ExpiryDate = expiry },
        ]);

        picked.Should().NotBeNull();
        picked!.Id.Should().Be(lowerId);
    }

    [Fact]
    public void PickBatch_SkipsInactiveOrDepletedBatches()
    {
        var sut = new FefoPicker();
        var expectedId = Guid.NewGuid();

        var picked = sut.PickBatch(
        [
            new InventoryBatch { Id = Guid.NewGuid(), Status = "expired", QtyOnHand = 9, ExpiryDate = new DateOnly(2026, 1, 1) },
            new InventoryBatch { Id = Guid.NewGuid(), Status = "active", QtyOnHand = 0, ExpiryDate = new DateOnly(2026, 1, 2) },
            new InventoryBatch { Id = expectedId, Status = "active", QtyOnHand = 3, ExpiryDate = new DateOnly(2026, 1, 3) },
        ]);

        picked.Should().NotBeNull();
        picked!.Id.Should().Be(expectedId);
    }
}

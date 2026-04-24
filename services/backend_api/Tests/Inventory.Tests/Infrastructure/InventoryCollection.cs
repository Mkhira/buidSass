namespace Inventory.Tests.Infrastructure;

[CollectionDefinition("inventory-fixture", DisableParallelization = true)]
public sealed class InventoryCollection : ICollectionFixture<InventoryTestFactory>
{
}

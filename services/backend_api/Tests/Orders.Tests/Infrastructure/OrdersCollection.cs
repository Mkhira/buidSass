namespace Orders.Tests.Infrastructure;

[CollectionDefinition("orders-fixture", DisableParallelization = true)]
public sealed class OrdersCollection : ICollectionFixture<OrdersTestFactory> { }

namespace Cart.Tests.Infrastructure;

[CollectionDefinition("cart-fixture", DisableParallelization = true)]
public sealed class CartCollection : ICollectionFixture<CartTestFactory>
{
}

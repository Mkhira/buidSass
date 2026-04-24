namespace Checkout.Tests.Infrastructure;

[CollectionDefinition("checkout-fixture", DisableParallelization = true)]
public sealed class CheckoutCollection : ICollectionFixture<CheckoutTestFactory> { }

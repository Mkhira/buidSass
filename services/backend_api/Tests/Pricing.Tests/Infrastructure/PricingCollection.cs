namespace Pricing.Tests.Infrastructure;

[CollectionDefinition("pricing-fixture", DisableParallelization = true)]
public sealed class PricingCollection : ICollectionFixture<PricingTestFactory>
{
}

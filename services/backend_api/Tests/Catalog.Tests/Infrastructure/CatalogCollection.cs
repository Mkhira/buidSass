namespace Catalog.Tests.Infrastructure;

/// <summary>
/// Every integration/contract test class that needs the shared Postgres + full host fixture joins
/// this collection so xUnit instantiates exactly one <see cref="CatalogTestFactory"/> across the
/// whole run. Running many fixtures in parallel saturates Docker and also makes stuck transient
/// worker queries flake tests.
/// </summary>
[CollectionDefinition("catalog-fixture", DisableParallelization = true)]
public sealed class CatalogCollection : ICollectionFixture<CatalogTestFactory>
{
}

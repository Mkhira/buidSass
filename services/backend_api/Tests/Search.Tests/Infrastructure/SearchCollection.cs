namespace Search.Tests.Infrastructure;

[CollectionDefinition("search-fixture", DisableParallelization = true)]
public sealed class SearchCollection : ICollectionFixture<SearchTestFactory>
{
}

namespace backend_api.Tests.Infrastructure;

[CollectionDefinition("PostgresCollection", DisableParallelization = true)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
}

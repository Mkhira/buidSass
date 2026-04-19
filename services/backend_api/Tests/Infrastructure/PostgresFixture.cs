using BackendApi.Modules.Shared;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using Docker.DotNet;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;

namespace backend_api.Tests.Infrastructure;

public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private DockerClient? _dockerClient;
    private Respawner? _respawner;
    private NpgsqlConnection? _respawnConnection;
    private string? _baselineExternalConnectionString;

    public string ConnectionString { get; private set; } = string.Empty;

    public bool UsingExternalConnectionString { get; private set; }

    public async Task InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        UsingExternalConnectionString = !string.IsNullOrWhiteSpace(external);

        if (UsingExternalConnectionString)
        {
            _baselineExternalConnectionString = external!;
            ConnectionString = external!;
        }
        else
        {
            _container = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("dental_commerce_test")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .WithCleanUp(true)
                .Build();

            await _container.StartAsync();
            _dockerClient = new DockerClientConfiguration().CreateClient();
            ConnectionString = _container.GetConnectionString();
        }

        await MigrateAsync(ConnectionString);
        await InitializeRespawnerAsync();
    }

    public async Task DisposeAsync()
    {
        if (_respawnConnection is not null)
        {
            await _respawnConnection.DisposeAsync();
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }

        _dockerClient?.Dispose();
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        if (_container is not null)
        {
            if (_dockerClient is null)
            {
                throw new InvalidOperationException("Docker client is not initialized.");
            }

            await _dockerClient.Containers.PauseContainerAsync(_container.Id, cancellationToken);
            return;
        }

        // External DB path (CI service container): simulate outage without controlling runner-level container lifecycle.
        if (UsingExternalConnectionString && !string.IsNullOrWhiteSpace(_baselineExternalConnectionString))
        {
            var builder = new NpgsqlConnectionStringBuilder(_baselineExternalConnectionString)
            {
                Host = "127.0.0.1",
                Port = 1,
                Timeout = 1,
                CommandTimeout = 1,
                Pooling = false,
            };
            ConnectionString = builder.ToString();
        }
    }

    public async Task UnpauseAsync(CancellationToken cancellationToken = default)
    {
        if (_container is not null)
        {
            if (_dockerClient is null)
            {
                throw new InvalidOperationException("Docker client is not initialized.");
            }

            await _dockerClient.Containers.UnpauseContainerAsync(_container.Id, cancellationToken);
            return;
        }

        if (UsingExternalConnectionString && !string.IsNullOrWhiteSpace(_baselineExternalConnectionString))
        {
            ConnectionString = _baselineExternalConnectionString;
        }
    }

    public async Task ResetDatabaseAsync()
    {
        if (_respawner is null || _respawnConnection is null)
        {
            return;
        }

        await _respawner.ResetAsync(_respawnConnection);
    }

    private async Task MigrateAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.MigrateAsync();
    }

    private async Task InitializeRespawnerAsync()
    {
        _respawnConnection = new NpgsqlConnection(ConnectionString);
        await _respawnConnection.OpenAsync();

        _respawner = await Respawner.CreateAsync(_respawnConnection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            TablesToIgnore = ["__EFMigrationsHistory"],
        });
    }
}

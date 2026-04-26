using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Shared;
using backend_api.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace backend_api.Tests.AuditLog;

[Collection("PostgresCollection")]
public sealed class AuditEventPublisherTests
{
    private readonly PostgresFixture _fixture;

    public AuditEventPublisherTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PublishAsync_Persists_All_Fields()
    {
        await _fixture.ResetDatabaseAsync();

        var correlationId = Guid.NewGuid();
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext!.Items["CorrelationId"] = correlationId.ToString();

        var auditEvent = CreateValidEvent();
        using var provider = BuildServiceProvider(_fixture.ConnectionString, accessor);
        await using var scope = provider.CreateAsyncScope();

        var publisher = scope.ServiceProvider.GetRequiredService<IAuditEventPublisher>();
        await publisher.PublishAsync(auditEvent, CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = await db.AuditLogEntries.SingleAsync();
        Assert.Equal(auditEvent.ActorId, entry.ActorId);
        Assert.Equal(auditEvent.ActorRole, entry.ActorRole);
        Assert.Equal(auditEvent.Action, entry.Action);
        Assert.Equal(auditEvent.EntityType, entry.EntityType);
        Assert.Equal(auditEvent.EntityId, entry.EntityId);
        Assert.NotNull(entry.BeforeState);
        Assert.NotNull(entry.AfterState);
        Assert.Equal(correlationId, entry.CorrelationId);
        Assert.Equal(auditEvent.Reason, entry.Reason);
        Assert.True(entry.OccurredAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task PublishAsync_WhenDatabasePaused_ThrowsNpgsqlException()
    {
        await _fixture.ResetDatabaseAsync();
        await _fixture.PauseAsync();

        try
        {
            var auditEvent = CreateValidEvent();
            using var provider = BuildServiceProvider(_fixture.ConnectionString, new HttpContextAccessor());
            await using var scope = provider.CreateAsyncScope();

            var publisher = scope.ServiceProvider.GetRequiredService<IAuditEventPublisher>();
            // EF Core's RelationalConnection wraps the underlying Npgsql failure in
            // InvalidOperationException ("An exception has been raised that is likely due to a
            // transient failure"). The contract being asserted is "DB outage propagates as a
            // database-level exception" — verify that by walking the InnerException chain.
            var thrown = await Assert.ThrowsAnyAsync<Exception>(
                () => publisher.PublishAsync(auditEvent, CancellationToken.None));
            Assert.True(
                ContainsNpgsqlException(thrown),
                $"Expected an NpgsqlException in the exception chain, got {thrown.GetType().FullName}: {thrown.Message}");
        }
        finally
        {
            await _fixture.UnpauseAsync();
        }
    }

    private static bool ContainsNpgsqlException(Exception? ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is NpgsqlException)
            {
                return true;
            }
        }
        return false;
    }

    private static AuditEvent CreateValidEvent()
    {
        return new AuditEvent(
            ActorId: Guid.NewGuid(),
            ActorRole: "admin_write",
            Action: "catalog.product.updated",
            EntityType: "Product",
            EntityId: Guid.NewGuid(),
            BeforeState: new { name = "old" },
            AfterState: new { name = "new" },
            Reason: "synchronization"
        );
    }

    private static ServiceProvider BuildServiceProvider(string connectionString, IHttpContextAccessor accessor)
    {
        var services = new ServiceCollection();
        services.AddSingleton(accessor);
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IAuditEventPublisher, AuditEventPublisher>();
        return services.BuildServiceProvider();
    }
}

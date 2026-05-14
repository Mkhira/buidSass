using System.Text.Json;
using BackendApi.AdminTool.Commands;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Shared;
using backend_api.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace backend_api.Tests.AdminTool.Tests;

/// <summary>
/// Pure-unit tests for the E1 Phase 0 (T008) `audit-emit` CLI verb. These cover cases
/// (b), (c), (d) plus a defense-in-depth missing-flag case. They do NOT spin up Postgres,
/// so they run cleanly in any environment.
/// </summary>
public sealed class AuditEmitCommandUnitTests
{
    [Fact]
    public void B_UnknownEventType_IsPermitted()
    {
        // (b) unknown --event-type is permitted (the column is a free string per data-model §3).
        var parsed = AuditEmitCommand.ParseArgs(new[] { "--event-type", "totally.made.up.event", "--payload", "{}" });
        Assert.Null(parsed.Error);
        Assert.Equal("totally.made.up.event", parsed.EventType);
    }

    [Fact]
    public void C_MalformedPayload_IsRejectedWithError()
    {
        // (c) malformed --payload JSON parse-fails and surfaces a non-null Error (exits non-zero).
        var parsed = AuditEmitCommand.ParseArgs(new[] { "--event-type", "deploy.attempted", "--payload", "{not-json" });
        Assert.NotNull(parsed.Error);
        Assert.Contains("not valid JSON", parsed.Error);
    }

    [Fact]
    public void D_MissingCorrelationId_DefaultsToFreshUuidV4()
    {
        // (d) when --correlation-id is omitted, a fresh UUID is generated (non-empty, non-equal
        // across two invocations with overwhelming probability — collisions are vanishingly rare).
        var first = AuditEmitCommand.ParseArgs(new[] { "--event-type", "deploy.attempted" });
        var second = AuditEmitCommand.ParseArgs(new[] { "--event-type", "deploy.attempted" });
        Assert.Null(first.Error);
        Assert.Null(second.Error);
        Assert.NotEqual(Guid.Empty, first.CorrelationId);
        Assert.NotEqual(Guid.Empty, second.CorrelationId);
        Assert.NotEqual(first.CorrelationId, second.CorrelationId);
    }

    [Fact]
    public void E_MissingEventType_IsRejected()
    {
        // Defense-in-depth: a required-flag omission surfaces a non-null Error.
        var parsed = AuditEmitCommand.ParseArgs(new[] { "--payload", "{}" });
        Assert.NotNull(parsed.Error);
        Assert.Contains("--event-type is required", parsed.Error);
    }

    [Fact]
    public void F_EqualsSyntax_IsAcceptedForAllFlags()
    {
        // Flag parsing accepts both `--flag value` and `--flag=value` forms — workflow callers
        // tend to use the latter when interpolating shell variables.
        var parsed = AuditEmitCommand.ParseArgs(new[]
        {
            "--event-type=deploy.attempted",
            "--payload={\"ok\":true}",
            "--correlation-id=11111111-2222-3333-4444-555555555555",
        });
        Assert.Null(parsed.Error);
        Assert.Equal("deploy.attempted", parsed.EventType);
        Assert.Equal(Guid.Parse("11111111-2222-3333-4444-555555555555"), parsed.CorrelationId);
    }
}

/// <summary>
/// Database-backed happy-path test (case a from T008). Runs against the shared Postgres fixture
/// and verifies one row lands in `audit_log_entries` with all seven mandatory fields populated.
/// Gated on the PostgresCollection so it shares the same testcontainer with the rest of the suite.
/// </summary>
[Collection("PostgresCollection")]
public sealed class AuditEmitCommandIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public AuditEmitCommandIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task A_HappyPath_WritesOneRowWithSevenMandatoryFields()
    {
        // (a) happy path — writes one row with seven mandatory fields populated.
        await _fixture.ResetDatabaseAsync();

        var args = new[]
        {
            "--event-type", "infra.iac.applied",
            "--payload", "{\"bicep_template_sha\":\"abc\",\"environment\":\"staging\",\"resource_changes_count\":17,\"actor_oid\":\"00000000-0000-0000-0000-000000000001\"}",
            "--correlation-id", "11111111-2222-3333-4444-555555555555",
        };

        var parsed = AuditEmitCommand.ParseArgs(args);
        Assert.Null(parsed.Error);

        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext!.Items["CorrelationId"] = parsed.CorrelationId.ToString();
        using var provider = BuildServiceProvider(_fixture.ConnectionString, accessor);
        await using var scope = provider.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IAuditEventPublisher>();
        await publisher.PublishAsync(AuditEmitCommand.BuildEvent(parsed, Guid.NewGuid()), CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = await db.AuditLogEntries.SingleAsync();
        Assert.NotEqual(Guid.Empty, entry.Id);
        Assert.NotEqual(Guid.Empty, entry.ActorId);
        Assert.Equal(AuditEmitCommand.SystemActorRole, entry.ActorRole);
        Assert.Equal("infra.iac.applied", entry.Action);
        Assert.Equal(AuditEmitCommand.InfraEntityType, entry.EntityType);
        Assert.Equal(parsed.CorrelationId, entry.EntityId);
        Assert.Equal(parsed.CorrelationId, entry.CorrelationId);
        Assert.NotNull(entry.AfterState);
        Assert.True(entry.OccurredAt <= DateTimeOffset.UtcNow);
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

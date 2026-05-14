using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.AdminTool.Commands;

/// <summary>
/// E1 spec — Phase 0 (T007). `audit-emit` CLI verb writes infrastructure-domain events
/// (`infra.iac.applied`, `deploy.*`, `infra.drift.detected`, `secret.*`) into the existing
/// <c>audit_log_entries</c> table via the shared <see cref="IAuditEventPublisher"/> from spec 003.
/// </summary>
public static class AuditEmitCommand
{
    public const string Verb = "audit-emit";
    internal const string SystemActorRole = "azure-managed-identity";
    internal const string InfraEntityType = "infrastructure";

    public static async Task<int> RunAsync(WebApplication app, string[] args, CancellationToken ct)
    {
        if (args.Any(a => a is "--help" or "-h")) { PrintUsage(); return 0; }
        var parsed = ParseArgs(args);
        if (parsed.Error is { } err) { Console.Error.WriteLine(err); PrintUsage(); return 2; }

        var actorId = await AuditEmitImdsClient.GetManagedIdentityObjectIdAsync(ct).ConfigureAwait(false);
        await using var scope = app.Services.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IAuditEventPublisher>();
        var http = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        http.HttpContext ??= new Microsoft.AspNetCore.Http.DefaultHttpContext();
        http.HttpContext.Items["CorrelationId"] = parsed.CorrelationId.ToString();
        await publisher.PublishAsync(BuildEvent(parsed, actorId), ct).ConfigureAwait(false);
        Console.Out.WriteLine($"{{\"audit_emit\":\"ok\",\"event_type\":\"{parsed.EventType}\",\"correlation_id\":\"{parsed.CorrelationId}\"}}");
        return 0;
    }

    internal static AuditEvent BuildEvent(ParsedArgs p, Guid actorId) => new(
        ActorId: actorId,
        ActorRole: SystemActorRole,
        Action: p.EventType!,
        EntityType: InfraEntityType,
        EntityId: p.CorrelationId,
        BeforeState: null,
        AfterState: p.Payload,
        Reason: null);

    internal record ParsedArgs(string? EventType, JsonElement Payload, Guid CorrelationId, string? Error);

    internal static ParsedArgs ParseArgs(string[] args)
    {
        var et = ReadFlag(args, "--event-type");
        var pl = ReadFlag(args, "--payload") ?? "{}";
        var cid = ReadFlag(args, "--correlation-id");
        if (string.IsNullOrWhiteSpace(et))
            return new ParsedArgs(null, default, Guid.Empty, "audit-emit: --event-type is required.");
        JsonElement payload;
        try { payload = JsonDocument.Parse(pl).RootElement.Clone(); }
        catch (JsonException ex) { return new ParsedArgs(et, default, Guid.Empty, $"audit-emit: --payload is not valid JSON ({ex.Message})."); }
        var correlation = Guid.TryParse(cid, out var p) ? p : Guid.NewGuid();
        return new ParsedArgs(et, payload, correlation, null);
    }

    private static string? ReadFlag(string[] args, string flag)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == flag && i + 1 < args.Length) return args[i + 1];
            if (args[i].StartsWith(flag + "=", StringComparison.Ordinal)) return args[i][(flag.Length + 1)..];
        }
        return null;
    }

    private static void PrintUsage() => Console.Out.WriteLine(
        "Usage: audit-emit --event-type <type> [--payload <json>] [--correlation-id <uuid>]");
}

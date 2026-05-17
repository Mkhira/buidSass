using BackendApi.Modules.Notifications.Features.DeadLetter;
using BackendApi.Modules.Notifications.Features.ProviderRouting;
using BackendApi.Modules.Notifications.Workers;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BackendApi.Modules.Notifications;

/// <summary>
/// Phase 5 — operator surfaces (T045, T046, T047). DeadLetterArchiver and
/// ProviderHealthMonitor join the hosted-service set; dead-letter and
/// provider-routing admin endpoints layer onto MapAdminEndpoints via Phase 5
/// helpers invoked from Phase 2's partial.
/// </summary>
public static partial class NotificationsModule
{
    internal static IServiceCollection AddPhase5Services(IServiceCollection services)
    {
        services.AddHostedService<DeadLetterArchiver>();
        services.AddHostedService<ProviderHealthMonitor>();
        services.AddHostedService<DeliveriesRetentionEnforcer>();
        services.AddScoped<Audit.INotificationsAuditEmitter, Audit.NotificationsAuditEmitter>();
        services.AddScoped<Seeding.NotificationsV1Seeder>();
        return services;
    }

    internal static void MapPhase5AdminEndpoints(IEndpointRouteBuilder admin)
    {
        var dl = admin.MapGroup("/dead-letter");
        dl.MapGet("", async (int skip, int take, IMediator m, CancellationToken ct) =>
        {
            // Clamp to a sensible page size — prevents arbitrarily-large
            // requests from pinning memory or driving a long-running query
            // (CodeRabbit pass-2 Minor).
            const int defaultPageSize = 50;
            const int maxPageSize = 200;
            var effectiveTake = take <= 0 ? defaultPageSize : Math.Min(take, maxPageSize);
            var effectiveSkip = Math.Max(0, skip);
            return Results.Ok(await m.Send(new ListDeadLetterQuery(effectiveSkip, effectiveTake), ct));
        });
        dl.MapPost("/{notificationId:guid}:retry", async (Guid notificationId, HttpContext ctx, IMediator m, CancellationToken ct) =>
        {
            var actor = ResolveAuthenticatedActorId(ctx);
            if (actor is null) return Results.Unauthorized();
            var ok = await m.Send(new RetryDeadLetterCommand(notificationId, actor.Value), ct);
            return ok ? Results.NoContent() : Results.NotFound();
        });
        dl.MapPost("/{notificationId:guid}:discard", async (Guid notificationId, HttpContext ctx, DiscardDeadLetterRequest body, IMediator m, CancellationToken ct) =>
        {
            var actor = ResolveAuthenticatedActorId(ctx);
            if (actor is null) return Results.Unauthorized();
            var ok = await m.Send(new DiscardDeadLetterCommand(notificationId, actor.Value, body.ReasonNote), ct);
            return ok ? Results.NoContent() : Results.NotFound();
        });

        var routing = admin.MapGroup("/provider-routing");
        routing.MapGet("/{market}/{channel}", async (string market, string channel, IMediator m, CancellationToken ct) =>
        {
            var view = await m.Send(new GetProviderRoutingQuery(market, channel), ct);
            return view is null ? Results.NotFound() : Results.Ok(view);
        });
        routing.MapPut("/{market}/{channel}", async (string market, string channel, HttpContext ctx, SetProviderRoutingRequest body, IMediator m, CancellationToken ct) =>
        {
            var actor = ResolveAuthenticatedActorId(ctx);
            if (actor is null) return Results.Unauthorized();
            await m.Send(new SetProviderRoutingCommand(
                market, channel, body.PrimaryProviderId, body.BackupProviderId,
                body.AutoFailoverEnabled, body.FailoverThresholdPct, body.FailoverWindowMinutes,
                actor.Value), ct);
            return Results.NoContent();
        });
        routing.MapPost("/{market}/{channel}:failover", async (string market, string channel, HttpContext ctx, IMediator m, CancellationToken ct) =>
        {
            var actor = ResolveAuthenticatedActorId(ctx);
            if (actor is null) return Results.Unauthorized();
            var ok = await m.Send(new FailoverProviderRoutingCommand(market, channel, actor.Value), ct);
            return ok ? Results.NoContent() : Results.BadRequest(new { error = "no_backup_configured" });
        });
    }
}

// Operator identity is intentionally absent from these DTOs — derived from
// the authenticated principal via ResolveAuthenticatedActorId.
public sealed record DiscardDeadLetterRequest(string ReasonNote);
public sealed record SetProviderRoutingRequest(
    string PrimaryProviderId,
    string? BackupProviderId,
    bool AutoFailoverEnabled,
    int FailoverThresholdPct,
    int FailoverWindowMinutes);

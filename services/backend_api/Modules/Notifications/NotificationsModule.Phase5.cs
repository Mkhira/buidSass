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
        return services;
    }

    internal static void MapPhase5AdminEndpoints(IEndpointRouteBuilder admin)
    {
        var dl = admin.MapGroup("/dead-letter");
        dl.MapGet("", async (int skip, int take, IMediator m, CancellationToken ct)
            => Results.Ok(await m.Send(new ListDeadLetterQuery(skip, take == 0 ? 50 : take), ct)));
        dl.MapPost("/{notificationId:guid}:retry", async (Guid notificationId, RetryDeadLetterRequest body, IMediator m, CancellationToken ct) =>
        {
            var ok = await m.Send(new RetryDeadLetterCommand(notificationId, body.OperatorId), ct);
            return ok ? Results.NoContent() : Results.NotFound();
        });
        dl.MapPost("/{notificationId:guid}:discard", async (Guid notificationId, DiscardDeadLetterRequest body, IMediator m, CancellationToken ct) =>
        {
            var ok = await m.Send(new DiscardDeadLetterCommand(notificationId, body.OperatorId, body.ReasonNote), ct);
            return ok ? Results.NoContent() : Results.NotFound();
        });

        var routing = admin.MapGroup("/provider-routing");
        routing.MapGet("/{market}/{channel}", async (string market, string channel, IMediator m, CancellationToken ct) =>
        {
            var view = await m.Send(new GetProviderRoutingQuery(market, channel), ct);
            return view is null ? Results.NotFound() : Results.Ok(view);
        });
        routing.MapPut("/{market}/{channel}", async (string market, string channel, SetProviderRoutingRequest body, IMediator m, CancellationToken ct) =>
        {
            await m.Send(new SetProviderRoutingCommand(
                market, channel, body.PrimaryProviderId, body.BackupProviderId,
                body.AutoFailoverEnabled, body.FailoverThresholdPct, body.FailoverWindowMinutes,
                body.OperatorId), ct);
            return Results.NoContent();
        });
        routing.MapPost("/{market}/{channel}:failover", async (string market, string channel, FailoverRequest body, IMediator m, CancellationToken ct) =>
        {
            var ok = await m.Send(new FailoverProviderRoutingCommand(market, channel, body.OperatorId), ct);
            return ok ? Results.NoContent() : Results.BadRequest(new { error = "no_backup_configured" });
        });
    }
}

public sealed record RetryDeadLetterRequest(Guid OperatorId);
public sealed record DiscardDeadLetterRequest(Guid OperatorId, string ReasonNote);
public sealed record SetProviderRoutingRequest(
    string PrimaryProviderId,
    string? BackupProviderId,
    bool AutoFailoverEnabled,
    int FailoverThresholdPct,
    int FailoverWindowMinutes,
    Guid OperatorId);
public sealed record FailoverRequest(Guid OperatorId);

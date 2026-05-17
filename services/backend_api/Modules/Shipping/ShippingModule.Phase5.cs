using BackendApi.Modules.Shipping.Features.ProviderRouting;
using BackendApi.Modules.Shipping.Features.Shipments;
using BackendApi.Modules.Shipping.Workers;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Shipping;

/// <summary>
/// Phase 5 — disputes, re-delivery, failover. Wires the shipment-action
/// slices (mark-handed-over, dispute, create-re-delivery, void-label),
/// provider-routing CRUD + manual failover, and three background workers
/// (reattempt-queued-labels, provider-health-monitor, sla-breach-monitor).
/// </summary>
public static partial class ShippingModule
{
    /// <summary>Registers Phase 5 slices + workers. Invoked from <see cref="AddSlices"/>.</summary>
    internal static void AddPhase5(IServiceCollection services)
    {
        // Slice handlers.
        services.AddScoped<MarkHandedOverHandler>();
        services.AddScoped<DisputeShipmentHandler>();
        services.AddScoped<CreateReDeliveryHandler>();
        services.AddScoped<VoidLabelHandler>();
        services.AddScoped<SetProviderRoutingHandler>();
        services.AddScoped<ManualFailoverHandler>();

        // Workers.
        services.AddHostedService<ReattemptQueuedLabelsWorker>();
        services.AddHostedService<ProviderHealthMonitor>();
        services.AddHostedService<SlaBreachMonitor>();
    }

    internal static void MapPhase5AdminEndpoints(IEndpointRouteBuilder admin)
    {
        admin.MapPost("/shipments/{id:guid}/mark-handed-over", async (
            Guid id, [FromBody] LifecycleBody body,
            [FromServices] IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(new MarkHandedOverCommand(id, body.ActorId), ct);
            return Results.NoContent();
        })
        .WithName("shipping_admin_mark_handed_over")
        .WithTags("shipping-admin");

        admin.MapPost("/shipments/{id:guid}/dispute", async (
            Guid id, [FromBody] DisputeBody body,
            [FromServices] IMediator mediator, CancellationToken ct) =>
        {
            var disputeId = await mediator.Send(new DisputeShipmentCommand(
                id, body.ActorId, body.ReportedBy ?? "support_agent", body.Notes), ct);
            return Results.Created($"/v1/admin/shipping/shipments/{id}/disputes/{disputeId}", new { disputeId });
        })
        .WithName("shipping_admin_dispute_shipment")
        .WithTags("shipping-admin");

        admin.MapPost("/shipments/{id:guid}/create-re-delivery", async (
            Guid id, [FromBody] LifecycleBody body,
            [FromServices] IMediator mediator, CancellationToken ct) =>
        {
            var newId = await mediator.Send(new CreateReDeliveryCommand(id, body.ActorId), ct);
            return Results.Created($"/v1/admin/shipping/shipments/{newId}", new { ShipmentId = newId });
        })
        .WithName("shipping_admin_create_re_delivery")
        .WithTags("shipping-admin");

        admin.MapPost("/shipments/{id:guid}/void-label", async (
            Guid id, [FromBody] VoidLabelBody body,
            [FromServices] IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(new VoidLabelCommand(id, body.ActorId, body.Reason ?? "operator_void"), ct);
            return Results.NoContent();
        })
        .WithName("shipping_admin_void_label")
        .WithTags("shipping-admin");

        admin.MapPut("/provider-routing", async (
            [FromBody] SetRoutingBody body,
            [FromServices] IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(new SetProviderRoutingCommand(
                body.MarketCode, body.MethodId,
                body.PrimaryProviderId, body.BackupProviderId,
                body.AutoFailoverEnabled, body.FailoverThresholdPct,
                body.FailoverWindowMinutes, body.ActorId), ct);
            return Results.NoContent();
        })
        .WithName("shipping_admin_set_provider_routing")
        .WithTags("shipping-admin");

        admin.MapPost("/provider-routing/{market}/{methodId:guid}/failover", async (
            string market, Guid methodId, [FromBody] LifecycleBody body,
            [FromServices] IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(new ManualFailoverCommand(market, methodId, body.ActorId), ct);
            return Results.NoContent();
        })
        .WithName("shipping_admin_manual_failover")
        .WithTags("shipping-admin");
    }
}

public sealed record DisputeBody(Guid ActorId, string? ReportedBy, string? Notes);
public sealed record VoidLabelBody(Guid ActorId, string? Reason);
public sealed record SetRoutingBody(
    string MarketCode,
    Guid MethodId,
    string PrimaryProviderId,
    string? BackupProviderId,
    bool AutoFailoverEnabled,
    int FailoverThresholdPct,
    int FailoverWindowMinutes,
    Guid ActorId);

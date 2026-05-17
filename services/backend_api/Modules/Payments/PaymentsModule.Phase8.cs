using BackendApi.Modules.Payments.Features.Chargebacks;
using BackendApi.Modules.Payments.Features.ProviderRouting;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Payments;

/// <summary>Phase 8 — provider routing + failover + chargebacks slices + endpoints.</summary>
public static partial class PaymentsModule
{
    static partial void AddPhase8Slices(IServiceCollection services)
    {
        services.AddScoped<ListProviderRoutingHandler>();
        services.AddScoped<UpdateProviderRoutingHandler>();
        services.AddScoped<FailoverProviderHandler>();
        services.AddScoped<ListChargebacksHandler>();
        services.AddScoped<UpdateChargebackHandler>();
    }

    static partial void MapAdminEndpointsPhase8(IEndpointRouteBuilder admin)
    {
        admin.MapGet("/provider-routing", async (
            [FromServices] IMediator mediator,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(new ListProviderRoutingQuery(), ct);
            return Results.Ok(new { routing = result });
        });

        admin.MapPut("/provider-routing", async (
            [FromBody] UpdateProviderRoutingRequest body,
            [FromServices] IMediator mediator,
            CancellationToken ct) =>
        {
            try
            {
                await mediator.Send(new UpdateProviderRoutingCommand(
                    body.MarketCode, body.Method, body.PrimaryProviderId, body.BackupProviderId,
                    body.AutoFailoverEnabled, body.FailoverThresholdPct, body.FailoverWindowMinutes,
                    body.OperatorId), ct);
                return Results.Ok(new { ok = true });
            }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        admin.MapPost("/provider-routing/{market}/{method}:failover", async (
            string market,
            string method,
            [FromBody] FailoverRequest body,
            [FromServices] IMediator mediator,
            CancellationToken ct) =>
        {
            try
            {
                await mediator.Send(new FailoverProviderCommand(market, method, body.OperatorId), ct);
                return Results.Ok(new { ok = true });
            }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        admin.MapGet("/chargebacks", async (
            [FromQuery] int take,
            [FromServices] IMediator mediator,
            CancellationToken ct) =>
        {
            var t = take <= 0 ? 100 : take;
            var result = await mediator.Send(new ListChargebacksQuery(t), ct);
            return Results.Ok(new { chargebacks = result });
        });

        admin.MapPatch("/chargebacks/{id:guid}", async (
            Guid id,
            [FromBody] UpdateChargebackRequest body,
            [FromServices] IMediator mediator,
            CancellationToken ct) =>
        {
            try
            {
                await mediator.Send(new UpdateChargebackCommand(id, body.Status, body.ResolutionNotes, body.OperatorId), ct);
                return Results.Ok(new { ok = true });
            }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });
    }
}

public sealed record UpdateProviderRoutingRequest(
    string MarketCode, string Method, string PrimaryProviderId, string? BackupProviderId,
    bool AutoFailoverEnabled, int FailoverThresholdPct, int FailoverWindowMinutes, Guid OperatorId);

public sealed record FailoverRequest(Guid OperatorId);

public sealed record UpdateChargebackRequest(string Status, string? ResolutionNotes, Guid OperatorId);

using BackendApi.Modules.Payments.Features.Reconciliation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Payments;

/// <summary>Phase 7 — reconciliation slices + endpoints.</summary>
public static partial class PaymentsModule
{
    static partial void AddPhase7Slices(IServiceCollection services)
    {
        services.AddScoped<ListReconciliationRunsHandler>();
        services.AddScoped<ListReconciliationExceptionsHandler>();
        services.AddScoped<ResolveReconciliationExceptionHandler>();
    }

    static partial void MapAdminEndpointsPhase7(IEndpointRouteBuilder admin)
    {
        admin.MapGet("/reconciliation/runs", async (
            [FromQuery] int take,
            [FromServices] IMediator mediator,
            CancellationToken ct) =>
        {
            var t = take <= 0 ? 50 : take;
            var result = await mediator.Send(new ListReconciliationRunsQuery(t), ct);
            return Results.Ok(new { runs = result });
        });

        admin.MapGet("/reconciliation/exceptions", async (
            [FromQuery] string? state,
            [FromQuery] int take,
            [FromServices] IMediator mediator,
            CancellationToken ct) =>
        {
            var t = take <= 0 ? 100 : take;
            var result = await mediator.Send(new ListReconciliationExceptionsQuery(state, t), ct);
            return Results.Ok(new { exceptions = result });
        });

        admin.MapPost("/reconciliation/exceptions/{id:guid}:resolve", async (
            Guid id,
            [FromBody] ResolveExceptionRequest body,
            [FromServices] IMediator mediator,
            CancellationToken ct) =>
        {
            try
            {
                await mediator.Send(new ResolveReconciliationExceptionCommand(id, body.Action, body.Notes, body.OperatorId), ct);
                return Results.Ok(new { ok = true });
            }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });
    }
}

public sealed record ResolveExceptionRequest(string Action, string? Notes, Guid OperatorId);

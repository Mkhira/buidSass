using BackendApi.Modules.Payments.Features.BankTransferMarkCaptured;
using BackendApi.Modules.Payments.Features.CodMarkCaptured;
using BackendApi.Modules.Payments.Features.CodMarkFailed;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Payments;

/// <summary>Phase 4 — COD + bank-transfer admin actions.</summary>
public static partial class PaymentsModule
{
    static partial void AddPhase4Slices(IServiceCollection services)
    {
        services.AddScoped<CodMarkCapturedHandler>();
        services.AddScoped<CodMarkFailedHandler>();
        services.AddScoped<BankTransferMarkCapturedHandler>();
    }

    static partial void MapCustomerEndpointsPhase4(IEndpointRouteBuilder customer)
    {
        // No customer-facing COD / bank-transfer endpoints — admin-only.
    }

    static partial void MapAdminEndpointsPhase4(IEndpointRouteBuilder admin)
    {
        // POST /v1/admin/payments/{id}/cod:mark-captured
        admin.MapPost("/{id:guid}/cod:mark-captured", async (
            Guid id,
            [FromBody] CodMarkCapturedRequest body,
            [FromServices] IMediator mediator,
            CancellationToken ct) =>
        {
            try
            {
                await mediator.Send(new CodMarkCapturedCommand(id, body.OperatorId, body.AmountCollected), ct);
                return Results.Ok(new { ok = true });
            }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        // POST /v1/admin/payments/{id}/cod:mark-failed
        admin.MapPost("/{id:guid}/cod:mark-failed", async (
            Guid id,
            [FromBody] CodMarkFailedRequest body,
            [FromServices] IMediator mediator,
            CancellationToken ct) =>
        {
            try
            {
                await mediator.Send(new CodMarkFailedCommand(id, body.OperatorId, body.Outcome), ct);
                return Results.Ok(new { ok = true });
            }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        // POST /v1/admin/payments/{id}/bank-transfer:mark-captured
        admin.MapPost("/{id:guid}/bank-transfer:mark-captured", async (
            Guid id,
            [FromBody] BankTransferMarkCapturedRequest body,
            [FromServices] IMediator mediator,
            CancellationToken ct) =>
        {
            try
            {
                await mediator.Send(new BankTransferMarkCapturedCommand(id, body.OperatorId, body.BankStatementEntryJson), ct);
                return Results.Ok(new { ok = true });
            }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });
    }
}

public sealed record CodMarkCapturedRequest(Guid OperatorId, decimal AmountCollected);
public sealed record CodMarkFailedRequest(Guid OperatorId, string Outcome);
public sealed record BankTransferMarkCapturedRequest(Guid OperatorId, string BankStatementEntryJson);

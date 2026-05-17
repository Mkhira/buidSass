using BackendApi.Modules.Payments.Features.Refund;
using BackendApi.Modules.Payments.Features.RetryPayment;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Payments;

/// <summary>Phase 5 — retry + refund slices and endpoints.</summary>
public static partial class PaymentsModule
{
    static partial void AddPhase5Slices(IServiceCollection services)
    {
        services.AddScoped<RetryPaymentHandler>();
        services.AddScoped<RefundHandler>();
    }

    static partial void MapCustomerEndpointsPhase5(IEndpointRouteBuilder customer)
    {
        // POST /v1/payments/{id}/retry
        customer.MapPost("/{id:guid}/retry", async (
            Guid id,
            [FromBody] RetryPaymentRequest body,
            [FromServices] IMediator mediator,
            CancellationToken ct) =>
        {
            try
            {
                var result = await mediator.Send(new RetryPaymentCommand(id, body.AttemptId, body.MethodOverride), ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });
    }

    static partial void MapAdminEndpointsPhase5(IEndpointRouteBuilder admin)
    {
        // POST /v1/admin/payments/{id}:refund
        admin.MapPost("/{id:guid}:refund", async (
            Guid id,
            [FromBody] RefundRequest body,
            [FromServices] IMediator mediator,
            CancellationToken ct) =>
        {
            try
            {
                var result = await mediator.Send(new RefundCommand(id, body.Amount, body.Reason, body.InitiatedBy), ct);
                return Results.Ok(result);
            }
            catch (RefundExceedsCapturedAmountException ex)
            {
                return Results.UnprocessableEntity(new { error = "refund_exceeds_captured", message = ex.Message });
            }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });
    }
}

public sealed record RetryPaymentRequest(Guid AttemptId, string? MethodOverride);
public sealed record RefundRequest(decimal Amount, string? Reason, Guid InitiatedBy);

using BackendApi.Modules.Payments.Features.CreatePayment;
using BackendApi.Modules.Payments.Features.GetPaymentMethods;
using BackendApi.Modules.Payments.Features.GetPayment;
using BackendApi.Modules.Payments.Services;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Payments;

/// <summary>Phase 2 — card payments + create + get + methods slices and endpoints.</summary>
public static partial class PaymentsModule
{
    static partial void AddPhase2Slices(IServiceCollection services)
    {
        services.AddPaymentsServices();
        services.AddScoped<IValidator<CreatePaymentCommand>, CreatePaymentValidator>();
        services.AddScoped<CreatePaymentHandler>();
        services.AddScoped<GetPaymentMethodsHandler>();
        services.AddScoped<GetPaymentHandler>();
        services.AddScoped<GetPaymentHistoryHandler>();
    }

    static partial void MapCustomerEndpointsPhase2(IEndpointRouteBuilder customer)
    {
        // GET /v1/payments/methods
        customer.MapGet("/methods", async (
            [FromQuery] string market,
            [FromQuery] decimal cart_total,
            [FromServices] IMediator mediator,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetPaymentMethodsQuery(market, cart_total), ct);
            return Results.Ok(new { methods = result });
        });

        // POST /v1/payments
        customer.MapPost("/", async (
            [FromBody] CreatePaymentRequest body,
            [FromServices] IMediator mediator,
            CancellationToken ct) =>
        {
            if (body is null) return Results.BadRequest(new { error = "request_body_required" });
            try
            {
                var result = await mediator.Send(new CreatePaymentCommand(
                    body.OrderId, body.CustomerId, body.MarketCode, body.Method,
                    body.Amount, body.Currency, body.AttemptId), ct);
                return Results.Ok(result);
            }
            catch (FluentValidation.ValidationException vex)
            {
                return Results.BadRequest(new { error = "validation_failed", details = vex.Errors.Select(e => e.ErrorMessage) });
            }
            catch (InvalidOperationException iex)
            {
                return Results.BadRequest(new { error = "invalid_request", message = iex.Message });
            }
        });

        // GET /v1/payments/{id}
        customer.MapGet("/{id:guid}", async (
            Guid id,
            [FromQuery] Guid customer_id,
            [FromServices] IMediator mediator,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetPaymentQuery(id, customer_id), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        // GET /v1/payments/me/history
        customer.MapGet("/me/history", async (
            [FromQuery] Guid customer_id,
            [FromQuery] int take,
            [FromServices] IMediator mediator,
            CancellationToken ct) =>
        {
            var t = take <= 0 ? 50 : take;
            var result = await mediator.Send(new GetPaymentHistoryQuery(customer_id, t), ct);
            return Results.Ok(new { payments = result });
        });
    }
}

public sealed record CreatePaymentRequest(
    Guid OrderId, Guid CustomerId, string MarketCode, string Method,
    decimal Amount, string Currency, Guid AttemptId);

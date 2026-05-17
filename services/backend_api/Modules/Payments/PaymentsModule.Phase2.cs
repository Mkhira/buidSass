using System.Security.Claims;
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
        // Customer identity is derived from the authenticated principal (sub
        // claim or NameIdentifier). Client-supplied customer_id is NEVER
        // trusted here — accepting it would allow cross-account read access.
        customer.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext httpContext,
            [FromServices] IMediator mediator,
            CancellationToken ct) =>
        {
            var customerId = ResolveAuthenticatedCustomerId(httpContext);
            if (customerId is null) return Results.Unauthorized();
            var result = await mediator.Send(new GetPaymentQuery(id, customerId.Value), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        // GET /v1/payments/me/history
        customer.MapGet("/me/history", async (
            HttpContext httpContext,
            [FromQuery] int take,
            [FromServices] IMediator mediator,
            CancellationToken ct) =>
        {
            var customerId = ResolveAuthenticatedCustomerId(httpContext);
            if (customerId is null) return Results.Unauthorized();
            const int maxTake = 200;
            var t = take <= 0 ? 50 : Math.Min(take, maxTake);
            var result = await mediator.Send(new GetPaymentHistoryQuery(customerId.Value, t), ct);
            return Results.Ok(new { payments = result });
        });
    }

    /// <summary>
    /// Resolves the authenticated customer id from the request's principal —
    /// `sub` claim first, then <see cref="ClaimTypes.NameIdentifier"/> as
    /// fallback. Matches the lookup pattern used by Reviews + Cart.
    /// </summary>
    private static Guid? ResolveAuthenticatedCustomerId(HttpContext httpContext)
    {
        var raw = httpContext.User.FindFirst("sub")?.Value
            ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}

public sealed record CreatePaymentRequest(
    Guid OrderId, Guid CustomerId, string MarketCode, string Method,
    decimal Amount, string Currency, Guid AttemptId);

using BackendApi.Modules.Support.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Support.Customer.ConvertToReturnRequest;

public static class ConvertToReturnRequestEndpoint
{
    public sealed record ConvertRequest(string? Narrative, IReadOnlyList<Guid>? AttachmentIds);

    public static IEndpointRouteBuilder MapConvertToReturnRequestEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{ticketId:guid}/convert-to-return", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid ticketId,
        [FromBody] ConvertRequest? body,
        HttpContext context,
        [FromServices] ConvertToReturnRequestHandler handler,
        CancellationToken ct)
    {
        var customerId = SupportResponseFactory.ResolveCustomerId(context);
        if (customerId is null)
        {
            return SupportResponseFactory.Problem(context, 401,
                TicketReasonCode.CustomerForbidden, "Authentication required.");
        }

        if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var idemRaw)
            || string.IsNullOrWhiteSpace(idemRaw.ToString()))
        {
            return SupportResponseFactory.Problem(context, 400,
                TicketReasonCode.IdempotencyKeyRequired,
                "Idempotency-Key header required.");
        }

        var result = await handler.HandleAsync(new ConvertToReturnRequestCommand(
            TicketId: ticketId,
            CustomerId: customerId.Value,
            IdempotencyKey: idemRaw.ToString(),
            Narrative: body?.Narrative,
            AttachmentIds: body?.AttachmentIds), ct);

        if (!result.Success)
        {
            var status = result.ReasonCode switch
            {
                TicketReasonCode.LinkedEntityNotFound => 404,
                TicketReasonCode.ConversionForbidden => 403,
                TicketReasonCode.ConversionCategoryNotEligible => 400,
                TicketReasonCode.ClosedTerminal => 409,
                TicketReasonCode.ConversionAlreadyConverted => 409,
                _ => 400,
            };
            return SupportResponseFactory.Problem(context, status,
                result.ReasonCode!, "Conversion rejected.", result.Detail);
        }

        return Results.Ok(new
        {
            return_request_id = result.ReturnRequestId,
            idempotent = result.Idempotent,
        });
    }
}

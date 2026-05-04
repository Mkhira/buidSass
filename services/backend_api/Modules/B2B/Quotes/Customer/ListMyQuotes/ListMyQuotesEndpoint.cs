using BackendApi.Modules.B2B.Primitives;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.B2B.Quotes.Customer.ListMyQuotes;

/// <summary>
/// Spec 021 contract §2.3 — endpoint for <c>GET /api/customer/quotes</c>.
/// Wire-shape concerns only:
/// <list type="bullet">
///   <item>Authentication: <c>CustomerJwt</c> scheme required (401 otherwise).</item>
///   <item>Query-string binding via <c>[AsParameters]</c>-equivalent manual binding.</item>
///   <item>FluentValidation pre-flight on the bound DTO.</item>
///   <item>Translates handler results into <see cref="Results.Ok"/> or problem-details.</item>
/// </list>
///
/// Read-only — no <c>Idempotency-Key</c> header gate (only state-transitioning
/// POSTs require it per contract §1).
/// </summary>
public static class ListMyQuotesEndpoint
{
    public static IEndpointRouteBuilder MapListMyQuotesEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        [FromQuery] string? state,
        [FromQuery(Name = "company_id")] Guid? companyId,
        [FromQuery] int? page,
        [FromQuery(Name = "page_size")] int? pageSize,
        [FromQuery] string? sort,
        HttpContext context,
        [FromServices] ListMyQuotesHandler handler,
        [FromServices] IValidator<ListMyQuotesRequest> validator,
        CancellationToken ct)
    {
        var customerId = B2BResponseFactory.ResolveCustomerId(context);
        if (customerId is null)
        {
            // Defensive — RequireAuthorization will normally have already rejected.
            return B2BResponseFactory.Problem(context, 401,
                QuoteReasonCode.QuoteRequiredFieldMissing,
                "Authentication required.");
        }

        var request = new ListMyQuotesRequest(
            State: state,
            CompanyId: companyId,
            Page: page,
            PageSize: pageSize,
            Sort: sort);

        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            var firstFailure = validation.Errors[0];
            var detail = string.Join("; ", validation.Errors.Select(e => e.ErrorMessage));
            return Results.Json(new
            {
                type = $"https://errors.dental-commerce/quotes/{firstFailure.ErrorCode}",
                title = "Quote list query validation failed.",
                status = 400,
                detail,
                instance = context.Request.Path.ToString(),
                reasonCode = firstFailure.ErrorCode,
            }, statusCode: 400, contentType: "application/problem+json");
        }

        var response = await handler.HandleAsync(customerId.Value, request, ct);
        return Results.Ok(response);
    }
}

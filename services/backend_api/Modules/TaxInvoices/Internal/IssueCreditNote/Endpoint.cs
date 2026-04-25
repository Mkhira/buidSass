using BackendApi.Modules.Identity.Authorization.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.TaxInvoices.Internal.IssueCreditNote;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapIssueCreditNoteEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/credit-notes/issue", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("invoices.credit_note.issue");
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        IssueCreditNoteRequest body,
        HttpContext context,
        IssueCreditNoteHandler handler,
        CancellationToken ct)
    {
        var result = await handler.IssueAsync(body, ct);
        if (!result.IsSuccess)
        {
            var status = result.ErrorCode switch
            {
                "credit_note.invalid_request" => 400,
                "invoice.not_found" => 404,
                "credit_note.line_not_found" => 404,
                "credit_note.line_exceeds_invoice" => 409,
                "invoice.template.missing" => 500,
                _ => 500,
            };
            return Results.Json(new ProblemDetails
            {
                Status = status,
                Title = "Credit note issuance failed",
                Detail = result.Detail,
                Type = $"https://errors.dental-commerce/invoices/{result.ErrorCode}",
                Instance = context.Request.Path,
                Extensions = { ["reasonCode"] = result.ErrorCode! },
            }, statusCode: status, contentType: "application/problem+json");
        }
        return Results.Ok(new { creditNoteId = result.CreditNoteId, creditNoteNumber = result.CreditNoteNumber });
    }
}

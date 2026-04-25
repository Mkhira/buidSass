using BackendApi.Modules.Identity.Authorization.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.TaxInvoices.Internal.IssueOnCapture;

public sealed record IssueOnCaptureRequest(Guid OrderId);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapIssueOnCaptureEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/invoices/issue-on-capture", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("invoices.issue_on_capture");
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        IssueOnCaptureRequest body,
        HttpContext context,
        IssueOnCaptureHandler handler,
        CancellationToken ct)
    {
        var result = await handler.IssueAsync(body.OrderId, ct);
        if (!result.IsSuccess)
        {
            var status = result.ErrorCode switch
            {
                "invoice.invalid_order_id" => 400,
                "invoice.order_not_found" => 404,
                "invoice.payment_not_captured" => 409,
                "invoice.no_lines" => 409,
                "invoice.template.missing" => 500,
                _ => 500,
            };
            return Results.Json(new ProblemDetails
            {
                Status = status,
                Title = "Invoice issuance failed",
                Detail = result.Detail,
                Type = $"https://errors.dental-commerce/invoices/{result.ErrorCode}",
                Instance = context.Request.Path,
                Extensions = { ["reasonCode"] = result.ErrorCode! },
            }, statusCode: status, contentType: "application/problem+json");
        }
        return Results.Ok(new { invoiceId = result.InvoiceId, invoiceNumber = result.InvoiceNumber });
    }
}

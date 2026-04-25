using System.Text.Json;
using BackendApi.Modules.Orders.Customer.Common;
using BackendApi.Modules.Orders.Entities;
using BackendApi.Modules.Orders.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Orders.Customer.Quotations.RejectQuotation;

public sealed record RejectRequest(string? Reason);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapCustomerRejectQuotationEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/reject", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        RejectRequest? body,
        HttpContext context,
        OrdersDbContext db,
        CancellationToken ct)
    {
        var accountId = CustomerOrdersResponseFactory.ResolveAccountId(context);
        if (accountId is null)
        {
            return CustomerOrdersResponseFactory.Problem(context, 401, "orders.requires_auth", "Auth required", "");
        }
        var quote = await db.Quotations.FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quote is null || quote.AccountId != accountId)
        {
            return CustomerOrdersResponseFactory.Problem(context, 404, "order.quote.not_found", "Quotation not found", "");
        }
        if (string.Equals(quote.Status, Quotation.StatusRejected, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Ok(new { quotationId = quote.Id, status = quote.Status });
        }
        if (!string.Equals(quote.Status, Quotation.StatusActive, StringComparison.OrdinalIgnoreCase))
        {
            return CustomerOrdersResponseFactory.Problem(context, 409, "order.quote.invalid_status",
                $"Cannot reject from status '{quote.Status}'", "");
        }

        var nowUtc = DateTimeOffset.UtcNow;
        quote.Status = Quotation.StatusRejected;
        quote.UpdatedAt = nowUtc;
        db.Outbox.Add(new OrdersOutboxEntry
        {
            EventType = "quote.rejected",
            AggregateId = quote.Id,
            PayloadJson = JsonSerializer.Serialize(new { quotationId = quote.Id, reason = body?.Reason }),
            CommittedAt = nowUtc,
        });
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { quotationId = quote.Id, status = quote.Status });
    }
}

using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Orders.Admin.Common;
using BackendApi.Modules.Orders.Admin.Fulfillment.Common;
using BackendApi.Modules.Orders.Entities;
using BackendApi.Modules.Orders.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Orders.Admin.Quotations.CreateQuotation;

public sealed record CreateQuotationLineRequest(
    Guid ProductId,
    string Sku,
    string NameAr,
    string NameEn,
    int Qty,
    long UnitPriceMinor,
    long LineDiscountMinor,
    long LineTaxMinor,
    long LineTotalMinor,
    bool Restricted);

public sealed record CreateQuotationRequest(
    Guid AccountId,
    string MarketCode,
    Guid PriceExplanationId,
    DateTimeOffset ValidUntil,
    IReadOnlyList<CreateQuotationLineRequest> Lines);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAdminCreateQuotationEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("orders.quotations.write");
        return builder;
    }

    /// <summary>FR-011. Admin-authored draft quotation. The number is generated from a
    /// dedicated quote sequence (mirrors order numbering); status starts <c>draft</c> and
    /// flips to <c>active</c> via the <c>send</c> endpoint.</summary>
    private static async Task<IResult> HandleAsync(
        CreateQuotationRequest body,
        HttpContext context,
        OrdersDbContext db,
        IAuditEventPublisher auditPublisher,
        CancellationToken ct)
    {
        var actor = AdminOrdersResponseFactory.ResolveActorAccountId(context);
        if (actor is null || actor == Guid.Empty)
        {
            return AdminOrdersResponseFactory.Problem(context, 401, "orders.actor_required", "Actor required", "");
        }
        if (body.AccountId == Guid.Empty || body.Lines.Count == 0
            || string.IsNullOrWhiteSpace(body.MarketCode) || body.PriceExplanationId == Guid.Empty)
        {
            return AdminOrdersResponseFactory.Problem(context, 400, "order.quote.invalid_request",
                "accountId, marketCode, priceExplanationId, and at least one line are required", "");
        }
        if (body.ValidUntil <= DateTimeOffset.UtcNow)
        {
            return AdminOrdersResponseFactory.Problem(context, 400, "order.quote.invalid_validity",
                "validUntil must be in the future", "");
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var quote = new Quotation
        {
            Id = Guid.NewGuid(),
            // Quote number format mirrors order number (research R3 / data-model.md §6).
            // For MVP we stamp a deterministic suffix; spec follow-up wires a dedicated
            // QuoteNumberSequencer parallel to OrderNumberSequencer.
            QuoteNumber = $"QUO-{body.MarketCode.ToUpperInvariant()}-{nowUtc:yyyyMM}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            AccountId = body.AccountId,
            MarketCode = body.MarketCode,
            Status = Quotation.StatusDraft,
            PriceExplanationId = body.PriceExplanationId,
            ValidUntil = body.ValidUntil,
            CreatedByAccountId = actor.Value,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };
        foreach (var line in body.Lines)
        {
            quote.Lines.Add(new QuotationLine
            {
                Id = Guid.NewGuid(),
                QuotationId = quote.Id,
                ProductId = line.ProductId,
                Sku = line.Sku,
                NameAr = line.NameAr,
                NameEn = line.NameEn,
                Qty = line.Qty,
                UnitPriceMinor = line.UnitPriceMinor,
                LineDiscountMinor = line.LineDiscountMinor,
                LineTaxMinor = line.LineTaxMinor,
                LineTotalMinor = line.LineTotalMinor,
                Restricted = line.Restricted,
                AttributesJson = "{}",
            });
        }
        db.Quotations.Add(quote);
        db.Outbox.Add(new OrdersOutboxEntry
        {
            EventType = "quote.created",
            AggregateId = quote.Id,
            PayloadJson = JsonSerializer.Serialize(new { quotationId = quote.Id, accountId = body.AccountId }),
            CommittedAt = nowUtc,
        });
        await db.SaveChangesAsync(ct);

        await FulfillmentOps.EmitAdminAuditAsync(auditPublisher, quote.Id, actor.Value,
            "orders.quotation.create", null, new { status = quote.Status, lines = quote.Lines.Count }, null, ct);

        return Results.Ok(new
        {
            quotationId = quote.Id,
            quoteNumber = quote.QuoteNumber,
            status = quote.Status,
        });
    }
}

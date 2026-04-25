using BackendApi.Modules.Orders.Entities;
using BackendApi.Modules.Orders.Internal.CreateFromQuotation;
using BackendApi.Modules.Orders.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orders.Tests.Infrastructure;

namespace Orders.Tests.Integration;

/// <summary>
/// H6 / SC-006 — Quotation conversion preserves the price explanation id byte-identically.
/// The order created from an active quotation MUST carry the SAME PriceExplanationId Guid;
/// no reissue, no rehash.
/// </summary>
[Collection("orders-fixture")]
public sealed class QuotationConversionHashTests(OrdersTestFactory factory)
{
    [Fact]
    public async Task Convert_PreservesPriceExplanationId()
    {
        await factory.ResetDatabaseAsync();
        var (_, accountId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");

        var explanationId = Guid.NewGuid();
        var quotationId = Guid.NewGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var quote = new Quotation
            {
                Id = quotationId,
                QuoteNumber = "QUO-KSA-202604-ABCDEF",
                AccountId = accountId,
                MarketCode = "KSA",
                Status = Quotation.StatusActive,
                PriceExplanationId = explanationId,
                ValidUntil = DateTimeOffset.UtcNow.AddDays(7),
                CreatedByAccountId = accountId,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            quote.Lines.Add(new QuotationLine
            {
                Id = Guid.NewGuid(),
                QuotationId = quotationId,
                ProductId = Guid.NewGuid(),
                Sku = "Q-SKU",
                NameAr = "اختبار",
                NameEn = "Quote Item",
                Qty = 3,
                UnitPriceMinor = 200_00,
                LineTotalMinor = 600_00,
                AttributesJson = "{}",
            });
            db.Quotations.Add(quote);
            await db.SaveChangesAsync();
        }

        await using var convertScope = factory.Services.CreateAsyncScope();
        var handler = convertScope.ServiceProvider.GetRequiredService<CreateFromQuotationHandler>();
        var result = await handler.CreateAsync(quotationId, accountId, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();

        var verifyDb = convertScope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var order = await verifyDb.Orders.AsNoTracking().SingleAsync(o => o.Id == result.OrderId);
        var quoteAfter = await verifyDb.Quotations.AsNoTracking().SingleAsync(q => q.Id == quotationId);

        // Byte-identical hash preservation: same Guid, no rehash.
        order.PriceExplanationId.Should().Be(explanationId);
        quoteAfter.PriceExplanationId.Should().Be(explanationId);
        quoteAfter.Status.Should().Be(Quotation.StatusConverted);
        quoteAfter.ConvertedOrderId.Should().Be(order.Id);
    }
}

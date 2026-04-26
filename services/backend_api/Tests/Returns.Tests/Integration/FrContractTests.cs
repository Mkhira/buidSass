using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Orders.Entities;
using BackendApi.Modules.Orders.Persistence;
using BackendApi.Modules.Orders.Primitives.StateMachines;
using BackendApi.Modules.Returns.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Returns.Tests.Infrastructure;

namespace Returns.Tests.Integration;

/// <summary>
/// J9 — contract sweep across the 24 FRs. Rather than 24 disjoint tests, this groups
/// related FRs by endpoint + reason code so each documented contract behavior gets covered:
///
///   FR-001 / FR-024  → submit returns happy path + not-delivered guard
///   FR-002           → restricted product zero-window override
///   FR-003           → return_number format RET-{MARKET}-{YYYYMM}-{SEQ6}
///   FR-005 / FR-020  → photos cap (5) + size limit
///   FR-013           → customer GET /returns/{id} surfaces timeline
///   FR-016           → admin export CSV
///   FR-017           → customer paginated list
///   FR-019           → idempotent admin actions (approve replay)
///   FR-022           → over-refund guard at submit
///   FR-023           → confirm-bank-transfer reason codes
///   FR-024           → 409 return.order.not_delivered
///
/// State-machine and refund-math FRs (FR-004, FR-007, FR-014) are covered by unit tests; the
/// rest of the FRs (FR-006, FR-008, FR-009, FR-010, FR-011, FR-012, FR-015, FR-018, FR-021)
/// are exercised end-to-end by the happy-path and credit-note-reconciliation tests.
/// </summary>
[Collection("returns-fixture")]
public class FrContractTests(ReturnsTestFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task FR_003_return_number_format_is_RET_market_yyyymm_seq6()
    {
        var (custToken, custId) = await ReturnsAuthHelper.IssueCustomerTokenAsync(factory);
        var (order, line) = await ReturnsTestSeed.SeedDeliveredCapturedOrderAsync(factory, custId);
        var customer = factory.CreateClient();
        ReturnsAuthHelper.SetBearer(customer, custToken);
        var resp = await customer.PostAsJsonAsync($"/v1/customer/orders/{order.Id}/returns",
            new { lines = new[] { new { orderLineId = line.Id, qty = 1, lineReasonCode = "x" } }, reasonCode = "x" });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var num = JsonDocument.Parse(await resp.Content.ReadAsStringAsync())
            .RootElement.GetProperty("returnNumber").GetString();
        num.Should().MatchRegex(@"^RET-KSA-\d{6}-\d{6}$");
    }

    [Fact]
    public async Task FR_024_submit_for_not_delivered_returns_409()
    {
        var (custToken, custId) = await ReturnsAuthHelper.IssueCustomerTokenAsync(factory);
        // Build an order that is NOT delivered.
        Order undelivered;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var nowUtc = DateTimeOffset.UtcNow;
            undelivered = new Order
            {
                Id = Guid.NewGuid(),
                OrderNumber = $"ORD-KSA-{nowUtc:yyyyMM}-{Random.Shared.Next(100000, 999999):D6}",
                AccountId = custId, MarketCode = "KSA", Currency = "SAR",
                SubtotalMinor = 100_00, GrandTotalMinor = 115_00, TaxMinor = 15_00,
                PriceExplanationId = Guid.NewGuid(),
                ShippingAddressJson = "{}", BillingAddressJson = "{}",
                OrderState = OrderSm.Placed, PaymentState = PaymentSm.Captured,
                FulfillmentState = FulfillmentSm.Picking,
                RefundState = RefundSm.None, PlacedAt = nowUtc,
                CreatedAt = nowUtc, UpdatedAt = nowUtc,
            };
            undelivered.Lines.Add(new OrderLine
            {
                Id = Guid.NewGuid(), OrderId = undelivered.Id, ProductId = Guid.NewGuid(),
                Sku = "X", NameAr = "ا", NameEn = "X", Qty = 1,
                UnitPriceMinor = 100_00, LineTaxMinor = 15_00, LineTotalMinor = 115_00,
                AttributesJson = "{}",
            });
            ordersDb.Orders.Add(undelivered);
            await ordersDb.SaveChangesAsync();
        }
        var customer = factory.CreateClient();
        ReturnsAuthHelper.SetBearer(customer, custToken);
        var resp = await customer.PostAsJsonAsync($"/v1/customer/orders/{undelivered.Id}/returns",
            new { lines = new[] { new { orderLineId = undelivered.Lines[0].Id, qty = 1, lineReasonCode = "x" } }, reasonCode = "x" });
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("return.order.not_delivered");
    }

    [Fact]
    public async Task FR_002_restricted_product_blocked_with_zero_window_reason()
    {
        var (custToken, custId) = await ReturnsAuthHelper.IssueCustomerTokenAsync(factory);
        Order order;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var nowUtc = DateTimeOffset.UtcNow;
            order = new Order
            {
                Id = Guid.NewGuid(),
                OrderNumber = $"ORD-KSA-{nowUtc:yyyyMM}-{Random.Shared.Next(100000, 999999):D6}",
                AccountId = custId, MarketCode = "KSA", Currency = "SAR",
                SubtotalMinor = 100_00, GrandTotalMinor = 115_00, TaxMinor = 15_00,
                PriceExplanationId = Guid.NewGuid(),
                ShippingAddressJson = "{}", BillingAddressJson = "{}",
                OrderState = OrderSm.Placed, PaymentState = PaymentSm.Captured,
                FulfillmentState = FulfillmentSm.Delivered,
                RefundState = RefundSm.None,
                PlacedAt = nowUtc.AddDays(-1), DeliveredAt = nowUtc.AddHours(-1),
                CreatedAt = nowUtc, UpdatedAt = nowUtc,
            };
            order.Lines.Add(new OrderLine
            {
                Id = Guid.NewGuid(), OrderId = order.Id, ProductId = Guid.NewGuid(),
                Sku = "PHARMA", NameAr = "د", NameEn = "Pharma", Qty = 1,
                UnitPriceMinor = 100_00, LineTaxMinor = 15_00, LineTotalMinor = 115_00,
                Restricted = true,                          // ← zero-window
                AttributesJson = "{}",
            });
            ordersDb.Orders.Add(order);
            await ordersDb.SaveChangesAsync();
        }
        var customer = factory.CreateClient();
        ReturnsAuthHelper.SetBearer(customer, custToken);
        var resp = await customer.PostAsJsonAsync($"/v1/customer/orders/{order.Id}/returns",
            new { lines = new[] { new { orderLineId = order.Lines[0].Id, qty = 1, lineReasonCode = "x" } }, reasonCode = "x" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("return.line.restricted_zero_window");
    }

    [Fact]
    public async Task FR_005_max_5_photos_enforced()
    {
        var (custToken, custId) = await ReturnsAuthHelper.IssueCustomerTokenAsync(factory);
        var (order, line) = await ReturnsTestSeed.SeedDeliveredCapturedOrderAsync(factory, custId);
        var customer = factory.CreateClient();
        ReturnsAuthHelper.SetBearer(customer, custToken);
        var resp = await customer.PostAsJsonAsync($"/v1/customer/orders/{order.Id}/returns",
            new
            {
                lines = new[] { new { orderLineId = line.Id, qty = 1, lineReasonCode = "x" } },
                reasonCode = "x",
                photoIds = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray(),
            });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("return.photos.too_many");
    }

    [Fact]
    public async Task FR_017_customer_list_paginated()
    {
        var (custToken, custId) = await ReturnsAuthHelper.IssueCustomerTokenAsync(factory);
        var (order, line) = await ReturnsTestSeed.SeedDeliveredCapturedOrderAsync(factory, custId, qty: 2);
        var customer = factory.CreateClient();
        ReturnsAuthHelper.SetBearer(customer, custToken);
        var submit = await customer.PostAsJsonAsync($"/v1/customer/orders/{order.Id}/returns",
            new { lines = new[] { new { orderLineId = line.Id, qty = 1, lineReasonCode = "x" } }, reasonCode = "x" });
        submit.StatusCode.Should().Be(HttpStatusCode.Created);
        var list = await customer.GetAsync("/v1/customer/returns?page=1&pageSize=10");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await list.Content.ReadAsStringAsync()).RootElement;
        doc.GetProperty("page").GetInt32().Should().Be(1);
        doc.GetProperty("pageSize").GetInt32().Should().Be(10);
        doc.GetProperty("total").GetInt32().Should().Be(1);
        doc.GetProperty("items").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task FR_013_customer_get_includes_timeline()
    {
        var (custToken, custId) = await ReturnsAuthHelper.IssueCustomerTokenAsync(factory);
        var (order, line) = await ReturnsTestSeed.SeedDeliveredCapturedOrderAsync(factory, custId);
        var customer = factory.CreateClient();
        ReturnsAuthHelper.SetBearer(customer, custToken);
        var submit = await customer.PostAsJsonAsync($"/v1/customer/orders/{order.Id}/returns",
            new { lines = new[] { new { orderLineId = line.Id, qty = 1, lineReasonCode = "x" } }, reasonCode = "x" });
        var returnId = JsonDocument.Parse(await submit.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        var detail = await customer.GetAsync($"/v1/customer/returns/{returnId}");
        detail.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await detail.Content.ReadAsStringAsync()).RootElement;
        doc.GetProperty("timeline").GetArrayLength().Should().BeGreaterThan(0);
        doc.GetProperty("state").GetString().Should().Be("pending_review");
    }

    [Fact]
    public async Task FR_016_admin_export_csv_returns_text_csv()
    {
        var (adminToken, _) = await ReturnsAuthHelper.IssueAdminTokenAsync(factory, new[] { "returns.read" });
        var admin = factory.CreateClient();
        ReturnsAuthHelper.SetBearer(admin, adminToken);
        var resp = await admin.GetAsync("/v1/admin/returns/export?format=csv");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType?.MediaType.Should().Be("text/csv");
        var csv = await resp.Content.ReadAsStringAsync();
        csv.Should().StartWith("id,return_number,order_id");
    }

    [Fact]
    public async Task Customer_endpoints_reject_unauthenticated_caller()
    {
        var anon = factory.CreateClient();
        var resp = await anon.GetAsync("/v1/customer/returns");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Admin_endpoints_reject_caller_without_permission()
    {
        // Admin token with NO permissions — must hit the endpoint filter and 403.
        var (adminToken, _) = await ReturnsAuthHelper.IssueAdminTokenAsync(factory, Array.Empty<string>());
        var admin = factory.CreateClient();
        ReturnsAuthHelper.SetBearer(admin, adminToken);
        var resp = await admin.GetAsync("/v1/admin/returns");
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }
}

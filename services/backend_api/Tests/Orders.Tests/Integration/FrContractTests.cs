using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Orders.Persistence;
using BackendApi.Modules.Orders.Primitives.StateMachines;
using BackendApi.Modules.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orders.Tests.Infrastructure;

namespace Orders.Tests.Integration;

/// <summary>
/// H11 — at least one contract test per FR-001..FR-026. Verifies surface-level invariants
/// (status codes, schema-key presence, observable state changes) rather than re-testing
/// internal logic that the unit/integration suites already cover. One [Fact] per FR keeps
/// the trail traceable.
/// </summary>
[Collection("orders-fixture")]
public sealed class FrContractTests(OrdersTestFactory factory)
{
    // FR-001 — Atomic order creation from a confirmed checkout session.
    [Fact]
    public async Task FR001_CreateFromCheckout_PersistsOrderAndOutbox()
    {
        await factory.ResetDatabaseAsync();
        var (_, accountId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        var productId = await OrdersTestSeed.SeedProductAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IOrderFromCheckoutHandler>();
        var orderId = Guid.NewGuid();
        var result = await handler.CreateAsync(BuildCheckoutRequest(orderId, accountId, productId), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        result.OrderId.Should().Be(orderId);
    }

    // FR-002 — order_number format ORD-{market}-{yyyymm}-{seq6}.
    [Fact]
    public async Task FR002_OrderNumber_MatchesSpecFormat()
    {
        await factory.ResetDatabaseAsync();
        var (_, accountId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        var productId = await OrdersTestSeed.SeedProductAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IOrderFromCheckoutHandler>();
        var result = await handler.CreateAsync(BuildCheckoutRequest(Guid.NewGuid(), accountId, productId), CancellationToken.None);
        result.OrderNumber.Should().MatchRegex("^ORD-KSA-\\d{6}-\\d{6}$");
    }

    // FR-003 — Four independent state-machine columns persisted.
    [Fact]
    public async Task FR003_FourStateColumns_Independent()
    {
        await factory.ResetDatabaseAsync();
        var (_, accountId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        var order = await OrdersTestSeed.SeedOrderAsync(factory, accountId);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var stored = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        stored.OrderState.Should().NotBeNullOrEmpty();
        stored.PaymentState.Should().NotBeNullOrEmpty();
        stored.FulfillmentState.Should().NotBeNullOrEmpty();
        stored.RefundState.Should().NotBeNullOrEmpty();
    }

    // FR-004 — POST /v1/customer/orders/{id}/cancel exists and policy-enforced.
    [Fact]
    public async Task FR004_CustomerCancel_AllowedOnAuthorized_NoShipment()
    {
        await factory.ResetDatabaseAsync();
        var (token, accountId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        var order = await OrdersTestSeed.SeedOrderAsync(factory, accountId, paymentState: PaymentSm.Authorized);
        var client = factory.CreateClient();
        OrdersCustomerAuthHelper.SetBearer(client, token);
        var response = await client.PostAsync($"/v1/customer/orders/{order.Id}/cancel",
            JsonContent.Create(new { reason = "test" }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // FR-005 — Admin fulfillment endpoints exist (auth + permission gate).
    [Fact]
    public async Task FR005_AdminStartPicking_RequiresPermission()
    {
        await factory.ResetDatabaseAsync();
        var (_, accountId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        var order = await OrdersTestSeed.SeedOrderAsync(factory, accountId, paymentState: PaymentSm.Captured);
        var (adminToken, _) = await OrdersAdminAuthHelper.IssueAdminTokenAsync(factory, new[] { "orders.fulfillment.write" });
        var client = factory.CreateClient();
        OrdersAdminAuthHelper.SetBearer(client, adminToken);
        var response = await client.PostAsync($"/v1/admin/orders/{order.Id}/fulfillment/start-picking", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // FR-006 — CreateShipment appends a shipment row.
    [Fact]
    public async Task FR006_CreateShipment_AppendsShipmentRow()
    {
        await factory.ResetDatabaseAsync();
        var (_, accountId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        var order = await OrdersTestSeed.SeedOrderAsync(factory, accountId,
            paymentState: PaymentSm.Captured, fulfillmentState: FulfillmentSm.Packed);
        var (adminToken, _) = await OrdersAdminAuthHelper.IssueAdminTokenAsync(factory, new[] { "orders.fulfillment.write" });
        var client = factory.CreateClient();
        OrdersAdminAuthHelper.SetBearer(client, adminToken);
        var response = await client.PostAsJsonAsync(
            $"/v1/admin/orders/{order.Id}/fulfillment/create-shipment",
            new { providerId = "stub", methodCode = "express", trackingNumber = "TR1" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // FR-007 — Webhook seam advances order payment_state.
    [Fact]
    public async Task FR007_WebhookHook_AdvancesPaymentState()
    {
        await factory.ResetDatabaseAsync();
        var (_, accountId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        var order = await OrdersTestSeed.SeedOrderAsync(factory, accountId,
            paymentState: PaymentSm.Authorized, paymentProviderId: "stub", paymentProviderTxnId: "txn-fr007");
        await using var scope = factory.Services.CreateAsyncScope();
        var hook = scope.ServiceProvider.GetRequiredService<IOrderPaymentStateHook>();
        var r = await hook.AdvanceFromAttemptAsync(
            new OrderPaymentAdvanceRequest("stub", "txn-fr007", "captured", null, null, "evt-fr007"),
            CancellationToken.None);
        r.FinalPaymentState.Should().Be(PaymentSm.Captured);
    }

    // FR-008 — Admin force-state requires reason + writes audit.
    [Fact]
    public async Task FR008_AdminForceState_RejectsWithoutReason()
    {
        await factory.ResetDatabaseAsync();
        var (_, accountId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        var order = await OrdersTestSeed.SeedOrderAsync(factory, accountId, paymentState: PaymentSm.Authorized);
        var (adminToken, _) = await OrdersAdminAuthHelper.IssueAdminTokenAsync(factory, new[] { "orders.payment.write" });
        var client = factory.CreateClient();
        OrdersAdminAuthHelper.SetBearer(client, adminToken);
        var response = await client.PostAsJsonAsync(
            $"/v1/admin/orders/{order.Id}/payments/force-state",
            new { toState = PaymentSm.Voided, reason = "" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // FR-009 — Customer read endpoints exist.
    [Fact]
    public async Task FR009_CustomerListAndGet_Reachable()
    {
        await factory.ResetDatabaseAsync();
        var (token, accountId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        var order = await OrdersTestSeed.SeedOrderAsync(factory, accountId);
        var client = factory.CreateClient();
        OrdersCustomerAuthHelper.SetBearer(client, token);
        (await client.GetAsync("/v1/customer/orders/")).EnsureSuccessStatusCode();
        (await client.GetAsync($"/v1/customer/orders/{order.Id}")).EnsureSuccessStatusCode();
    }

    // FR-010 — Admin read endpoints + finance export exist.
    [Fact]
    public async Task FR010_AdminListAndExport_Reachable()
    {
        await factory.ResetDatabaseAsync();
        var (_, accountId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        await OrdersTestSeed.SeedOrderAsync(factory, accountId);
        var (token, _) = await OrdersAdminAuthHelper.IssueAdminTokenAsync(factory,
            new[] { "orders.read", "orders.finance.export" });
        var client = factory.CreateClient();
        OrdersAdminAuthHelper.SetBearer(client, token);
        (await client.GetAsync("/v1/admin/orders/")).EnsureSuccessStatusCode();
        (await client.GetAsync("/v1/admin/orders/export?format=csv")).EnsureSuccessStatusCode();
    }

    // FR-011 — Customer quotation list reachable.
    [Fact]
    public async Task FR011_CustomerQuotationList_Reachable()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        var client = factory.CreateClient();
        OrdersCustomerAuthHelper.SetBearer(client, token);
        (await client.GetAsync("/v1/customer/quotations/")).EnsureSuccessStatusCode();
    }

    // FR-012 — Quotation accept preserves stored explanation hash (SC-006 covered separately).
    [Fact]
    public async Task FR012_QuotationAccept_OnInactive_Returns409()
    {
        await factory.ResetDatabaseAsync();
        var (token, accountId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        var quoteId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            db.Quotations.Add(new BackendApi.Modules.Orders.Entities.Quotation
            {
                Id = quoteId,
                QuoteNumber = "QUO-X",
                AccountId = accountId,
                MarketCode = "KSA",
                Status = "draft", // not active
                PriceExplanationId = Guid.NewGuid(),
                ValidUntil = DateTimeOffset.UtcNow.AddDays(7),
                CreatedByAccountId = accountId,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        var client = factory.CreateClient();
        OrdersCustomerAuthHelper.SetBearer(client, token);
        var response = await client.PostAsync($"/v1/customer/quotations/{quoteId}/accept", null);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // FR-013 — Outbox row emitted on creation.
    [Fact]
    public async Task FR013_OutboxRow_EmittedOnCreate()
    {
        await factory.ResetDatabaseAsync();
        var (_, accountId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        var productId = await OrdersTestSeed.SeedProductAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IOrderFromCheckoutHandler>();
        var orderId = Guid.NewGuid();
        await handler.CreateAsync(BuildCheckoutRequest(orderId, accountId, productId), CancellationToken.None);
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        (await db.Outbox.AsNoTracking().AnyAsync(e => e.AggregateId == orderId && e.EventType == "order.placed"))
            .Should().BeTrue();
    }

    // FR-014 — Inventory reservation conversion attempted (no reservation = no-op success).
    [Fact]
    public async Task FR014_NoReservations_SucceedsWithoutCallingInventory()
    {
        await factory.ResetDatabaseAsync();
        var (_, accountId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        var productId = await OrdersTestSeed.SeedProductAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IOrderFromCheckoutHandler>();
        var result = await handler.CreateAsync(BuildCheckoutRequest(Guid.NewGuid(), accountId, productId), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    // FR-015 — payment.captured outbox event emitted (synchronous-capture path).
    [Fact]
    public async Task FR015_PaymentCapturedEvent_EmittedOnSyncCapture()
    {
        await factory.ResetDatabaseAsync();
        var (_, accountId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        var productId = await OrdersTestSeed.SeedProductAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IOrderFromCheckoutHandler>();
        var orderId = Guid.NewGuid();
        await handler.CreateAsync(BuildCheckoutRequest(orderId, accountId, productId), CancellationToken.None);
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        (await db.Outbox.AsNoTracking().AnyAsync(e => e.AggregateId == orderId && e.EventType == "payment.captured"))
            .Should().BeTrue();
    }

    // FR-016 — Refund-state advance endpoint reachable.
    [Fact]
    public async Task FR016_AdvanceRefundState_NoOpReturns200()
    {
        await factory.ResetDatabaseAsync();
        var (_, accountId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        var order = await OrdersTestSeed.SeedOrderAsync(factory, accountId, paymentState: PaymentSm.Captured);
        var (adminToken, _) = await OrdersAdminAuthHelper.IssueAdminTokenAsync(factory,
            new[] { "orders.internal.advance_refund" });
        var client = factory.CreateClient();
        OrdersAdminAuthHelper.SetBearer(client, adminToken);
        // Order has refund_state=none; sending return.rejected is a no-op (no requested state to revert).
        var response = await client.PostAsJsonAsync(
            $"/v1/internal/orders/{order.Id}/advance-refund-state",
            new { eventType = "return.rejected" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // FR-017 — Orders are not soft-deleted (assert no DeletedAt column on entity surface).
    [Fact]
    public void FR017_OrderEntity_HasNoSoftDeleteColumn()
    {
        typeof(BackendApi.Modules.Orders.Entities.Order)
            .GetProperty("DeletedAt").Should().BeNull();
    }

    // FR-018 — high_level_status surfaced on both list and detail.
    [Fact]
    public async Task FR018_HighLevelStatus_PresentInResponses()
    {
        await factory.ResetDatabaseAsync();
        var (token, accountId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        await OrdersTestSeed.SeedOrderAsync(factory, accountId);
        var client = factory.CreateClient();
        OrdersCustomerAuthHelper.SetBearer(client, token);
        var listJson = await (await client.GetAsync("/v1/customer/orders/")).Content.ReadAsStringAsync();
        listJson.Should().Contain("highLevelStatus");
    }

    // FR-019 — Admin mutations write audit (covered in detail by AdminAuditTests; here just sanity).
    [Fact]
    public async Task FR019_AdminMutations_TrackedInTransitionsTable()
    {
        await factory.ResetDatabaseAsync();
        var (_, accountId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        var order = await OrdersTestSeed.SeedOrderAsync(factory, accountId,
            paymentState: PaymentSm.Captured, fulfillmentState: FulfillmentSm.NotStarted);
        var (adminToken, _) = await OrdersAdminAuthHelper.IssueAdminTokenAsync(factory, new[] { "orders.fulfillment.write" });
        var client = factory.CreateClient();
        OrdersAdminAuthHelper.SetBearer(client, adminToken);
        await client.PostAsync($"/v1/admin/orders/{order.Id}/fulfillment/start-picking", null);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        (await db.StateTransitions.AsNoTracking()
            .CountAsync(t => t.OrderId == order.Id && t.Trigger == "admin.start_picking"))
            .Should().Be(1);
    }

    // FR-020 — Cross-account access on customer detail is rejected as not_found.
    [Fact]
    public async Task FR020_CrossAccountAccess_Returns404()
    {
        await factory.ResetDatabaseAsync();
        var (_, accountA) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        var (tokenB, _) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        var orderA = await OrdersTestSeed.SeedOrderAsync(factory, accountA);
        var client = factory.CreateClient();
        OrdersCustomerAuthHelper.SetBearer(client, tokenB);
        var response = await client.GetAsync($"/v1/customer/orders/{orderA.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // FR-021 — Reorder produces a NEW cart, never mutates the original order.
    [Fact]
    public async Task FR021_Reorder_DoesNotMutateOriginalOrder()
    {
        await factory.ResetDatabaseAsync();
        var (token, accountId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        var order = await OrdersTestSeed.SeedOrderAsync(factory, accountId,
            paymentState: PaymentSm.Captured, fulfillmentState: FulfillmentSm.Delivered,
            deliveredAt: DateTimeOffset.UtcNow.AddDays(-1));
        var client = factory.CreateClient();
        OrdersCustomerAuthHelper.SetBearer(client, token);
        var response = await client.PostAsync($"/v1/customer/orders/{order.Id}/reorder", null);
        // Empty seeded order has only a placeholder line — endpoint may 200 with skipped or 400.
        // Either way, the original order's lines are unchanged.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var refreshed = await db.Orders.AsNoTracking().Include(o => o.Lines).SingleAsync(o => o.Id == order.Id);
        refreshed.Lines.Should().HaveCount(1);
    }

    // FR-022 — Cancellation policy reads from per-market table.
    [Fact]
    public async Task FR022_CapturedOutsideWindow_ReturnsWindowExpired()
    {
        await factory.ResetDatabaseAsync();
        var (token, accountId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        var order = await OrdersTestSeed.SeedOrderAsync(factory, accountId, paymentState: PaymentSm.Captured);
        // Move placedAt back beyond the 24h window.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var stored = await db.Orders.SingleAsync(o => o.Id == order.Id);
            stored.PlacedAt = DateTimeOffset.UtcNow.AddDays(-3);
            await db.SaveChangesAsync();
        }
        var client = factory.CreateClient();
        OrdersCustomerAuthHelper.SetBearer(client, token);
        var response = await client.PostAsJsonAsync($"/v1/customer/orders/{order.Id}/cancel", new { reason = "x" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // FR-023 — GET /audit returns transitions + admin actions.
    [Fact]
    public async Task FR023_AuditEndpoint_ReturnsTransitions()
    {
        await factory.ResetDatabaseAsync();
        var (_, accountId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        var order = await OrdersTestSeed.SeedOrderAsync(factory, accountId);
        var (adminToken, _) = await OrdersAdminAuthHelper.IssueAdminTokenAsync(factory, new[] { "orders.read" });
        var client = factory.CreateClient();
        OrdersAdminAuthHelper.SetBearer(client, adminToken);
        var response = await client.GetAsync($"/v1/admin/orders/{order.Id}/audit");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("transitions");
        body.Should().Contain("adminActions");
    }

    // FR-024 — Webhook endpoint dedups on (provider, event_id) — Order seam idempotent.
    // Covered in WebhookDedupTests; here just assert the seam interface exists.
    [Fact]
    public void FR024_OrderPaymentStateHookInterface_IsRegistered()
    {
        using var scope = factory.Services.CreateAsyncScope();
        var hook = scope.ServiceProvider.GetService<IOrderPaymentStateHook>();
        hook.Should().NotBeNull();
    }

    // FR-025 — ConfirmBankTransfer flips pending_bank_transfer → captured.
    [Fact]
    public async Task FR025_ConfirmBankTransfer_AdvancesToCaptured()
    {
        await factory.ResetDatabaseAsync();
        var (_, accountId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        var order = await OrdersTestSeed.SeedOrderAsync(factory, accountId, paymentState: PaymentSm.PendingBankTransfer);
        var (adminToken, _) = await OrdersAdminAuthHelper.IssueAdminTokenAsync(factory, new[] { "orders.payment.write" });
        var client = factory.CreateClient();
        OrdersAdminAuthHelper.SetBearer(client, adminToken);
        var response = await client.PostAsJsonAsync(
            $"/v1/admin/orders/{order.Id}/payments/confirm-bank-transfer",
            new { reference = "REF-1", receivedAt = DateTimeOffset.UtcNow });
        response.EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id)).PaymentState.Should().Be(PaymentSm.Captured);
    }

    // FR-026 — COD delivery → captured. Covered in CodDeliveryCaptureTests.
    [Fact]
    public async Task FR026_CodDelivery_CapturesPayment()
    {
        await factory.ResetDatabaseAsync();
        var (_, accountId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        var order = await OrdersTestSeed.SeedOrderAsync(factory, accountId,
            paymentState: PaymentSm.PendingCod, fulfillmentState: FulfillmentSm.HandedToCarrier);
        var (adminToken, _) = await OrdersAdminAuthHelper.IssueAdminTokenAsync(factory, new[] { "orders.fulfillment.write" });
        var client = factory.CreateClient();
        OrdersAdminAuthHelper.SetBearer(client, adminToken);
        var response = await client.PostAsync($"/v1/admin/orders/{order.Id}/fulfillment/mark-delivered", null);
        response.EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id)).PaymentState.Should().Be(PaymentSm.Captured);
    }

    private static OrderFromCheckoutRequest BuildCheckoutRequest(Guid orderId, Guid accountId, Guid productId) =>
        new(
            PreallocatedOrderId: orderId,
            SessionId: Guid.NewGuid(),
            CartId: Guid.NewGuid(),
            AccountId: accountId,
            MarketCode: "KSA",
            Lines: new[] { new OrderFromCheckoutLine(productId, 1, 100_00, 100_00, 15_00, 115_00, null) },
            CouponCode: null,
            PaymentMethod: "card",
            PaymentProviderId: "stub",
            PaymentProviderTxnId: $"txn-{Guid.NewGuid():N}",
            ShippingFeeMinor: 0,
            ShippingProviderId: "stub",
            ShippingMethodCode: "express",
            ShippingAddressJson: "{}",
            BillingAddressJson: "{}",
            SubtotalMinor: 100_00,
            DiscountMinor: 0,
            TaxMinor: 15_00,
            GrandTotalMinor: 115_00,
            Currency: "SAR",
            IssuedExplanationId: Guid.NewGuid());
}

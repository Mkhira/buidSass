using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Shared;
using Checkout.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Checkout.Tests.Integration;

/// <summary>
/// FR-015: every state transition (session + payment_attempt + webhook) writes an audit row.
/// This test walks a happy-path checkout end-to-end and asserts the expected audit chain
/// landed in `audit_log_entries`.
/// </summary>
[Collection("checkout-fixture")]
public sealed class AuditEmissionTests(CheckoutTestFactory factory)
{
    [Fact]
    public async Task HappyPath_EmitsCreatedAddressedShippingPaymentSubmittedConfirmedAndPaymentCaptured()
    {
        await factory.ResetDatabaseAsync();
        var (token, accountId) = await CheckoutCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");

        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CheckoutTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-AUD", ["ksa"]);
        var warehouseId = await CheckoutTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-aud", "ksa");
        await CheckoutTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, 10);
        await CheckoutTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-AUD",
            DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), 10);
        await CheckoutTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");
        var cartId = await CheckoutTestSeedHelper.SeedReadyCartAsync(seedScope.ServiceProvider, accountId, "ksa", productId, qty: 2);

        var client = factory.CreateClient();
        CheckoutCustomerAuthHelper.SetBearer(client, token);

        var startResp = await client.PostAsJsonAsync("/v1/customer/checkout/sessions", new { cartId, marketCode = "ksa" });
        startResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var sessionId = Guid.Parse((await startResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sessionId").GetString()!);

        await client.PatchAsJsonAsync($"/v1/customer/checkout/sessions/{sessionId}/address",
            new { shipping = new { fullName = "Dr Test", phoneE164 = "+966501234567", line1 = "1 Test", city = "Riyadh", countryCode = "SA" } });
        var quote = (await (await client.GetAsync($"/v1/customer/checkout/sessions/{sessionId}/shipping-quotes")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("quotes")[0];
        await client.PatchAsJsonAsync($"/v1/customer/checkout/sessions/{sessionId}/shipping",
            new { providerId = quote.GetProperty("providerId").GetString(), methodCode = quote.GetProperty("methodCode").GetString() });
        await client.PatchAsJsonAsync($"/v1/customer/checkout/sessions/{sessionId}/payment-method", new { method = "card" });

        using var submit = new HttpRequestMessage(HttpMethod.Post, $"/v1/customer/checkout/sessions/{sessionId}/submit");
        submit.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        submit.Content = JsonContent.Create(new { });
        (await client.SendAsync(submit)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var verify = factory.Services.CreateAsyncScope();
        var auditDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await auditDb.AuditLogEntries.AsNoTracking()
            .Where(a => a.EntityId == sessionId)
            .Select(a => a.Action)
            .ToListAsync();

        rows.Should().Contain("checkout.session.created");
        rows.Should().Contain("checkout.session.addressed");
        rows.Should().Contain("checkout.session.shipping_selected");
        rows.Should().Contain("checkout.session.payment_selected");
        rows.Should().Contain("checkout.session.submitted");
        rows.Should().Contain("checkout.session.confirmed");

        // Payment attempt audit row uses the attempt id, not the session id. CR review on
        // PR #31: scope strictly to THIS session's attempts so the assertion is not
        // satisfied by a leftover row from another fixture.
        var checkoutDb = verify.ServiceProvider.GetRequiredService<BackendApi.Modules.Checkout.Persistence.CheckoutDbContext>();
        var attemptIds = await checkoutDb.PaymentAttempts.AsNoTracking()
            .Where(a => a.SessionId == sessionId)
            .Select(a => a.Id)
            .ToListAsync();
        attemptIds.Should().NotBeEmpty(because: "Submit must have created at least one payment attempt");

        var paymentRows = await auditDb.AuditLogEntries.AsNoTracking()
            .Where(a => a.EntityType == "PaymentAttempt"
                     && attemptIds.Contains(a.EntityId)
                     && (a.Action == "checkout.payment.captured"
                      || a.Action == "checkout.payment.authorized"
                      || a.Action == "checkout.payment.pending_webhook"))
            .CountAsync();
        paymentRows.Should().BeGreaterThan(0,
            because: "the authorize-success path emits a payment.<state> audit row for this session's attempt");
    }
}

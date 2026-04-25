using System.Net;
using BackendApi.Modules.Checkout.Persistence;
using Checkout.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Checkout.Tests.Integration;

/// <summary>SC-007 — 100 duplicate webhook deliveries → 1 state mutation.</summary>
[Collection("checkout-fixture")]
public sealed class WebhookDedupTests(CheckoutTestFactory factory)
{
    [Fact]
    public async Task Webhook_100Duplicates_OneMutation()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();
        const string eventId = "evt-test-dedup-001";
        var successes = 0;
        for (var i = 0; i < 100; i++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/webhooks/payment-gateway/stub");
            req.Headers.Add("X-Signature", "sig-test");
            req.Headers.Add("X-Event-Type", "payment.captured");
            req.Headers.Add("X-Event-Id", eventId);
            req.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            var resp = await client.SendAsync(req);
            // All should return 2xx (R7) — duplicates are acknowledged, not rejected.
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            if (resp.StatusCode == HttpStatusCode.OK) successes++;
        }
        successes.Should().Be(100);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CheckoutDbContext>();
        var rows = await db.PaymentWebhookEvents.AsNoTracking()
            .Where(e => e.ProviderEventId == eventId).CountAsync();
        rows.Should().Be(1, because: "unique (provider_id, provider_event_id) dedupes duplicate deliveries");
    }
}

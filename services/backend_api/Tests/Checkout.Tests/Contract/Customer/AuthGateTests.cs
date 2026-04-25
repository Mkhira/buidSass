using System.Net;
using System.Net.Http.Json;
using Checkout.Tests.Infrastructure;
using FluentAssertions;

namespace Checkout.Tests.Contract.Customer;

/// <summary>US2 — guest auth gate on Submit (FR-019).</summary>
[Collection("checkout-fixture")]
public sealed class AuthGateTests(CheckoutTestFactory factory)
{
    [Fact]
    public async Task Submit_Guest_Returns401_RequiresAuth()
    {
        await factory.ResetDatabaseAsync();
        var client = factory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/v1/customer/checkout/sessions/{Guid.NewGuid()}/submit");
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        req.Content = JsonContent.Create(new { });
        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

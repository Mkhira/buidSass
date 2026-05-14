using System.Text.Json;
using BackendApi.Modules.Shipping.Providers;
using FluentAssertions;

namespace Shipping.Tests.Unit;

public sealed class WebhookPayloadRedactorTests
{
    // AC-23 — payload sent to providers carries only required fields;
    // any inbound payload is stripped of PII before persistence.
    [Fact]
    public void Redacts_recipient_name_phone_email_and_address()
    {
        var input = JsonDocument.Parse("""
        {
          "awb": "X",
          "recipient_name": "Ahmed",
          "recipient_phone": "+966500000000",
          "recipient_email": "a@b.com",
          "ship_to_address": "Riyadh"
        }
        """);

        var redacted = JsonDocument.Parse(WebhookPayloadRedactor.Redact(input.RootElement));
        redacted.RootElement.GetProperty("awb").GetString().Should().Be("X");
        redacted.RootElement.GetProperty("recipient_name").GetString().Should().Be("[REDACTED]");
        redacted.RootElement.GetProperty("recipient_phone").GetString().Should().Be("[REDACTED]");
        redacted.RootElement.GetProperty("recipient_email").GetString().Should().Be("[REDACTED]");
        redacted.RootElement.GetProperty("ship_to_address").GetString().Should().Be("[REDACTED]");
    }

    [Fact]
    public void Redacts_fuzzy_address_phone_fields()
    {
        var input = JsonDocument.Parse("""
        {
          "billing_phone": "+201234567890",
          "consignee_address": "Cairo",
          "customer_name_full": "Mona"
        }
        """);
        var redacted = JsonDocument.Parse(WebhookPayloadRedactor.Redact(input.RootElement));
        redacted.RootElement.GetProperty("billing_phone").GetString().Should().Be("[REDACTED]");
        redacted.RootElement.GetProperty("consignee_address").GetString().Should().Be("[REDACTED]");
        redacted.RootElement.GetProperty("customer_name_full").GetString().Should().Be("[REDACTED]");
    }

    [Fact]
    public void Preserves_safe_keys()
    {
        var input = JsonDocument.Parse("""
        { "awb": "X", "event_kind": "in_transit", "city_code": "RUH" }
        """);
        var redacted = JsonDocument.Parse(WebhookPayloadRedactor.Redact(input.RootElement));
        redacted.RootElement.GetProperty("awb").GetString().Should().Be("X");
        redacted.RootElement.GetProperty("event_kind").GetString().Should().Be("in_transit");
        redacted.RootElement.GetProperty("city_code").GetString().Should().Be("RUH");
    }

    [Fact]
    public void Handles_nested_objects_and_arrays()
    {
        var input = JsonDocument.Parse("""
        {
          "awb": "X",
          "events": [
            { "kind": "created", "city": "RUH", "recipient_name": "Ali" }
          ]
        }
        """);
        var redacted = JsonDocument.Parse(WebhookPayloadRedactor.Redact(input.RootElement));
        var first = redacted.RootElement.GetProperty("events")[0];
        first.GetProperty("kind").GetString().Should().Be("created");
        first.GetProperty("city").GetString().Should().Be("RUH");
        first.GetProperty("recipient_name").GetString().Should().Be("[REDACTED]");
    }
}

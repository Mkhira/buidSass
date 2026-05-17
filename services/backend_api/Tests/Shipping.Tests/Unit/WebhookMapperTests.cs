using System.Text;
using BackendApi.Modules.Shipping.Providers;
using BackendApi.Modules.Shipping.Providers.Aramex;
using BackendApi.Modules.Shipping.Providers.Bosta;
using BackendApi.Modules.Shipping.Providers.Smsa;
using FluentAssertions;

namespace Shipping.Tests.Unit;

public sealed class WebhookMapperTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-14T12:00:00Z");

    [Theory]
    [InlineData("created", CanonicalEventKinds.LabelPurchased)]
    [InlineData("picked_up", CanonicalEventKinds.HandedToCarrier)]
    [InlineData("in_transit", CanonicalEventKinds.InTransit)]
    [InlineData("out_for_delivery", CanonicalEventKinds.OutForDelivery)]
    [InlineData("delivered", CanonicalEventKinds.Delivered)]
    [InlineData("delivery_failed", CanonicalEventKinds.DeliveryAttempted)]
    [InlineData("returned", CanonicalEventKinds.ReturnedToSender)]
    public void Smsa_maps_known_event_kinds(string providerKind, string canonical)
    {
        var body = Encoding.UTF8.GetBytes($"{{\"awb_number\":\"X1\",\"event_kind\":\"{providerKind}\"}}");
        var ev = SmsaWebhookMapper.Parse(body, Now);
        ev.ProviderTrackingId.Should().Be("X1");
        ev.CanonicalEventKind.Should().Be(canonical);
    }

    [Theory]
    [InlineData("shipment_created", CanonicalEventKinds.LabelPurchased)]
    [InlineData("attempt_failed", CanonicalEventKinds.DeliveryAttempted)]
    [InlineData("returned_to_sender", CanonicalEventKinds.ReturnedToSender)]
    public void Aramex_maps_known_event_kinds(string providerKind, string canonical)
    {
        var body = Encoding.UTF8.GetBytes($"{{\"waybill_number\":\"A1\",\"event\":\"{providerKind}\"}}");
        var ev = AramexWebhookMapper.Parse(body, Now);
        ev.ProviderTrackingId.Should().Be("A1");
        ev.CanonicalEventKind.Should().Be(canonical);
    }

    [Theory]
    [InlineData("pickup_requested", CanonicalEventKinds.LabelPurchased)]
    [InlineData("delivery_failed", CanonicalEventKinds.DeliveryAttempted)]
    public void Bosta_maps_known_event_kinds(string providerKind, string canonical)
    {
        var body = Encoding.UTF8.GetBytes($"{{\"tracking_number\":\"B1\",\"state\":\"{providerKind}\"}}");
        var ev = BostaWebhookMapper.Parse(body, Now);
        ev.ProviderTrackingId.Should().Be("B1");
        ev.CanonicalEventKind.Should().Be(canonical);
    }

    [Fact]
    public void Unknown_event_kind_falls_through()
    {
        var body = Encoding.UTF8.GetBytes("{\"awb_number\":\"X\",\"event_kind\":\"some_new_kind\"}");
        var ev = SmsaWebhookMapper.Parse(body, Now);
        ev.CanonicalEventKind.Should().Be("some_new_kind");
    }

    [Fact]
    public void Mapper_redacts_pii_in_raw_payload()
    {
        var body = Encoding.UTF8.GetBytes("""
        { "awb_number":"X","event_kind":"in_transit","recipient_name":"Ahmed","recipient_phone":"+9665"}
        """);
        var ev = SmsaWebhookMapper.Parse(body, Now);
        ev.RawPayloadRedactedJson.Should().Contain("[REDACTED]");
        ev.RawPayloadRedactedJson.Should().NotContain("Ahmed");
        ev.RawPayloadRedactedJson.Should().NotContain("+9665");
    }
}

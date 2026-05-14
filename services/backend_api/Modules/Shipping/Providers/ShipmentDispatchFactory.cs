using System.Text.Json;
using BackendApi.Modules.Shipping.Domain;
using BackendApi.Modules.Shipping.Primitives;

namespace BackendApi.Modules.Shipping.Providers;

/// <summary>
/// Builds a <see cref="CreateShipmentDispatch"/> from a persisted
/// <see cref="Shipment"/>. The retry workers
/// (<c>LabelDispatchWorker</c>, <c>ReattemptQueuedLabelsWorker</c>) call this
/// instead of fabricating placeholder data — the OrderConfirmedSubscriber
/// snapshots the weight and declared value onto the row, and the redacted
/// ship-to JSON is the canonical source for the address fields.
/// </summary>
public static class ShipmentDispatchFactory
{
    public static CreateShipmentDispatch FromShipment(Shipment shipment)
    {
        ArgumentNullException.ThrowIfNull(shipment);
        var address = DeserializeAddress(shipment.ShipToAddressRedactedJson, shipment.MarketCode);
        var (recipientName, phoneLast4) = ExtractRecipient(shipment.ShipToAddressRedactedJson);
        return new CreateShipmentDispatch(
            ShipmentId: shipment.Id,
            MarketCode: shipment.MarketCode,
            MethodKey: shipment.MethodVersionId.ToString(),
            RecipientNameRedacted: recipientName,
            RecipientPhoneMaskedLast4: phoneLast4,
            ShipTo: address,
            WeightKg: shipment.WeightKgSnapshot,
            CurrencyCode: ShippingConstants.Currencies.For(shipment.MarketCode),
            DeclaredValueAmount: shipment.DeclaredValueAmountSnapshot);
    }

    private static AddressMinimized DeserializeAddress(string json, string marketCode)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new AddressMinimized(
                City: ReadString(root, "city") ?? string.Empty,
                PostalCode: ReadString(root, "postal_code"),
                Line1: ReadString(root, "line1") ?? string.Empty,
                Line2: ReadString(root, "line2"),
                CountryCode: ReadString(root, "country") ?? marketCode);
        }
        catch (JsonException)
        {
            return new AddressMinimized(string.Empty, null, string.Empty, null, marketCode);
        }
    }

    private static (string Name, string PhoneLast4) ExtractRecipient(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return (
                ReadString(root, "recipient_name") ?? "REDACTED",
                ReadString(root, "recipient_phone_last4") ?? "****");
        }
        catch (JsonException)
        {
            return ("REDACTED", "****");
        }
    }

    private static string? ReadString(JsonElement el, string name)
    {
        if (el.ValueKind == JsonValueKind.Object
            && el.TryGetProperty(name, out var p)
            && p.ValueKind == JsonValueKind.String)
        {
            return p.GetString();
        }
        return null;
    }
}

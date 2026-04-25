using System.Text.Json;

namespace BackendApi.Modules.Orders.Primitives;

/// <summary>
/// Safely parses an order's <c>shipping_address_json</c> / <c>billing_address_json</c> column
/// into a <see cref="JsonElement"/> ready to embed in a response. CR review round 2 (Major):
/// returning the element from a disposed <see cref="JsonDocument"/> is undefined behaviour;
/// malformed JSON would also throw an uncaught <see cref="JsonException"/> straight to the
/// 500 path. We clone the root onto a stable element backed by JsonNode so the caller can
/// serialise it without holding the parse buffer.
/// </summary>
public static class AddressJson
{
    public static JsonElement Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return EmptyObject();
        }
        try
        {
            using var doc = JsonDocument.Parse(raw);
            // Clone() detaches from the parse buffer so the returned element is safe to
            // serialise after the JsonDocument is disposed.
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return EmptyObject();
        }
    }

    private static JsonElement EmptyObject()
    {
        using var empty = JsonDocument.Parse("{}");
        return empty.RootElement.Clone();
    }
}

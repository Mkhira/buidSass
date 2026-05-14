using System.Text.Json;

namespace BackendApi.Modules.Shipping.Providers;

/// <summary>
/// BR-15 — strips PII from provider webhook payloads before persistence.
/// Removes <c>recipient_name</c>, <c>recipient_phone</c>, <c>recipient_email</c>,
/// <c>ship_to_address</c>, <c>billing_address</c>, and any property whose
/// name contains <c>phone</c> / <c>email</c> / <c>name</c> / <c>address</c>.
/// The redacted JSON preserves shape so downstream consumers can still
/// reason about the event structurally.
/// </summary>
public static class WebhookPayloadRedactor
{
    private static readonly HashSet<string> ExactRedactKeys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "recipient_name",
            "recipient_phone",
            "recipient_email",
            "ship_to_address",
            "billing_address",
            "ship_to",
            "billing_to",
            "customer_name",
            "customer_phone",
        };

    private static readonly string[] FuzzyRedactSubstrings =
        new[] { "phone", "email", "address", "national_id", "passport", "_name" };

    public static string Redact(JsonElement element)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteRedacted(writer, element);
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteRedacted(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var prop in element.EnumerateObject())
                {
                    if (ShouldRedact(prop.Name))
                    {
                        writer.WriteString(prop.Name, "[REDACTED]");
                    }
                    else
                    {
                        writer.WritePropertyName(prop.Name);
                        WriteRedacted(writer, prop.Value);
                    }
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteRedacted(writer, item);
                }
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static bool ShouldRedact(string name)
    {
        if (ExactRedactKeys.Contains(name)) return true;
        foreach (var s in FuzzyRedactSubstrings)
        {
            if (name.Contains(s, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}

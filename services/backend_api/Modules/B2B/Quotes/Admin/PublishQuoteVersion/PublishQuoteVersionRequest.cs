using System.Text.Json.Serialization;

namespace BackendApi.Modules.B2B.Quotes.Admin.PublishQuoteVersion;

/// <summary>Spec 021 contract §4.4.</summary>
public sealed record PublishQuoteVersionRequest(
    [property: JsonPropertyName("validity_extends")] bool? ValidityExtends);

public sealed record PublishQuoteVersionResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("current_version_id")] Guid CurrentVersionId,
    [property: JsonPropertyName("version_number")] int VersionNumber,
    [property: JsonPropertyName("expires_at")] DateTimeOffset? ExpiresAt);

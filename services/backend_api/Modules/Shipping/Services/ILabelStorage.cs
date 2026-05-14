namespace BackendApi.Modules.Shipping.Services;

/// <summary>
/// Abstraction over the label-PDF blob storage (research §2). Production
/// impl points at Azure Blob container <c>shipping-labels-{env}</c> with
/// 90-day Hot + 180-day Cool lifecycle policy. SAS URLs returned by
/// <see cref="GetReadUrlAsync"/> carry a 5-minute TTL so they cannot be
/// shared post-issuance.
/// </summary>
public interface ILabelStorage
{
    /// <summary>
    /// Persists the label PDF bytes and returns the canonical blob URL
    /// (not the signed read URL). The blob URL is stored on the shipment;
    /// the signed read URL is minted on demand by the admin / customer endpoints.
    /// </summary>
    Task<string> StoreLabelAsync(Guid shipmentId, byte[] pdfBytes, CancellationToken cancellationToken);

    /// <summary>Returns a short-lived SAS-style read URL (5-minute TTL by default).</summary>
    Task<string> GetReadUrlAsync(string blobUrl, TimeSpan? ttl, CancellationToken cancellationToken);
}

/// <summary>
/// Production-safe placeholder implementation. Returns a deterministic but
/// non-fetchable URL. Real Azure Blob impl swaps this binding when the
/// <c>AzureStorage:LabelContainer</c> connection string is configured.
/// </summary>
public sealed class PlaceholderLabelStorage : ILabelStorage
{
    public Task<string> StoreLabelAsync(Guid shipmentId, byte[] pdfBytes, CancellationToken cancellationToken)
    {
        // The placeholder never persists bytes; production binding handles the upload.
        return Task.FromResult($"https://placeholder.invalid/labels/{shipmentId:N}.pdf");
    }

    public Task<string> GetReadUrlAsync(string blobUrl, TimeSpan? ttl, CancellationToken cancellationToken)
    {
        // No SAS minting in the placeholder — return the input untouched so
        // callers can recognise the dev/staging surface vs. a signed URL.
        return Task.FromResult(blobUrl);
    }
}

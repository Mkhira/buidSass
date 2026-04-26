using System.Globalization;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.TaxInvoices.Rendering;

/// <summary>
/// Dev/CI <see cref="IInvoiceBlobStore"/> implementation that writes blobs under
/// <c>${TempPath}/buidSass-invoices/</c>. Production must use the Azure Blob implementation
/// (research R10); <c>TaxInvoicesModule</c> registers this only when the host is
/// Development / Test / Staging.
///
/// CR4 hardening — every filesystem access canonicalises the resolved path and rejects keys
/// that escape <c>Root</c> (rooted paths, <c>..</c> segments). <c>Compose</c> also sanitises
/// the marketCode + invoice number aggressively so even internally-generated keys can't slip
/// past the canonical-path check.
/// </summary>
public sealed class LocalFsInvoiceBlobStore(ILogger<LocalFsInvoiceBlobStore> logger) : IInvoiceBlobStore
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "buidSass-invoices");
    private static readonly string RootFullPath =
        Path.GetFullPath(Root) + Path.DirectorySeparatorChar;

    public async Task<string> PutAsync(string blobKey, ReadOnlyMemory<byte> bytes, string contentType, CancellationToken ct)
    {
        var fullPath = ResolveAndGuard(blobKey);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllBytesAsync(fullPath, bytes.ToArray(), ct);
        logger.LogInformation("invoices.blob.put key={Key} size={Size} contentType={Ct}", blobKey, bytes.Length, contentType);
        return blobKey;
    }

    public async Task<byte[]?> GetAsync(string blobKey, CancellationToken ct)
    {
        var fullPath = ResolveAndGuard(blobKey);
        if (!File.Exists(fullPath))
        {
            return null;
        }
        return await File.ReadAllBytesAsync(fullPath, ct);
    }

    public string ResolveInvoiceKey(string marketCode, DateTimeOffset issuedAt, string invoiceNumber, string kind = "pdf")
        => Compose("invoices", marketCode, issuedAt, invoiceNumber, kind);

    public string ResolveCreditNoteKey(string marketCode, DateTimeOffset issuedAt, string creditNoteNumber, string kind = "pdf")
        => Compose("credit-notes", marketCode, issuedAt, creditNoteNumber, kind);

    /// <summary>
    /// Canonicalise the candidate path and reject anything that doesn't sit under
    /// <see cref="RootFullPath"/>. <c>Path.Combine(Root, ".." + something)</c> happily yields
    /// a path outside the root; <c>Path.GetFullPath</c> normalises it so a substring check
    /// against the prefix is reliable.
    /// </summary>
    private static string ResolveAndGuard(string blobKey)
    {
        if (string.IsNullOrWhiteSpace(blobKey))
        {
            throw new ArgumentException("blobKey is required.", nameof(blobKey));
        }
        if (Path.IsPathRooted(blobKey))
        {
            throw new ArgumentException($"blobKey must be relative (got rooted '{blobKey}').", nameof(blobKey));
        }
        var candidate = Path.GetFullPath(Path.Combine(Root, blobKey));
        // Compare with trailing separator so /tmp/buidSass-invoices-evil doesn't match.
        if (!(candidate + Path.DirectorySeparatorChar).StartsWith(RootFullPath, StringComparison.Ordinal)
            && !candidate.Equals(RootFullPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"blobKey '{blobKey}' resolves outside the invoice root.", nameof(blobKey));
        }
        return candidate;
    }

    private static string Compose(string root, string marketCode, DateTimeOffset issuedAt, string number, string kind)
    {
        var yyyymm = issuedAt.UtcDateTime.ToString("yyyyMM", CultureInfo.InvariantCulture);
        var ext = kind switch { "pdf" or "credit_note_pdf" => "pdf", "xml" => "xml", _ => "bin" };
        // CR4 — sanitise aggressively. Accept only [A-Za-z0-9_-] in market + number; replace
        // anything else with '_' so even internally-generated keys pass ResolveAndGuard.
        var marketSafe = SanitiseSegment(marketCode.ToUpperInvariant());
        var numberSafe = SanitiseSegment(number);
        if (marketSafe.Length == 0 || numberSafe.Length == 0)
        {
            throw new ArgumentException("marketCode and number must contain at least one alphanumeric character.");
        }
        return Path.Combine(root, marketSafe, yyyymm, $"{numberSafe}.{ext}");
    }

    private static string SanitiseSegment(string s)
    {
        var buf = new char[s.Length];
        var len = 0;
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
            {
                buf[len++] = c;
            }
            else
            {
                buf[len++] = '_';
            }
        }
        return new string(buf, 0, len);
    }
}

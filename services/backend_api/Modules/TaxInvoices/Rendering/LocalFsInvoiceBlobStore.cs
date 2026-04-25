using System.Globalization;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.TaxInvoices.Rendering;

/// <summary>
/// Dev/CI <see cref="IInvoiceBlobStore"/> implementation that writes blobs under
/// <c>${TempPath}/buidSass-invoices/</c>. The Azure Blob implementation lives in a Phase 1.5
/// follow-up (research R10) once the storage residency wiring (ADR-010) is finalised.
/// </summary>
public sealed class LocalFsInvoiceBlobStore(ILogger<LocalFsInvoiceBlobStore> logger) : IInvoiceBlobStore
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "buidSass-invoices");

    public async Task<string> PutAsync(string blobKey, ReadOnlyMemory<byte> bytes, string contentType, CancellationToken ct)
    {
        var fullPath = Path.Combine(Root, blobKey);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllBytesAsync(fullPath, bytes.ToArray(), ct);
        logger.LogInformation("invoices.blob.put key={Key} size={Size} contentType={Ct}", blobKey, bytes.Length, contentType);
        return blobKey;
    }

    public async Task<byte[]?> GetAsync(string blobKey, CancellationToken ct)
    {
        var fullPath = Path.Combine(Root, blobKey);
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

    private static string Compose(string root, string marketCode, DateTimeOffset issuedAt, string number, string kind)
    {
        var yyyymm = issuedAt.UtcDateTime.ToString("yyyyMM", CultureInfo.InvariantCulture);
        var ext = kind switch { "pdf" or "credit_note_pdf" => "pdf", "xml" => "xml", _ => "bin" };
        var safe = number.Replace('/', '-').Replace('\\', '-');
        return Path.Combine(root, marketCode.ToUpperInvariant(), yyyymm, $"{safe}.{ext}");
    }
}

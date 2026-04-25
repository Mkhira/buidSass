namespace BackendApi.Modules.TaxInvoices.Rendering;

/// <summary>
/// FR-011 — invoice blob abstraction. Production uses Azure Blob Storage (research R10);
/// dev/CI uses a local-filesystem implementation. Storage is keyed by (invoiceId, kind)
/// where <c>kind ∈ {"pdf","xml","credit_note_pdf"}</c>.
/// </summary>
public interface IInvoiceBlobStore
{
    Task<string> PutAsync(string blobKey, ReadOnlyMemory<byte> bytes, string contentType, CancellationToken ct);
    Task<byte[]?> GetAsync(string blobKey, CancellationToken ct);
    string ResolveInvoiceKey(string marketCode, DateTimeOffset issuedAt, string invoiceNumber, string kind = "pdf");
    string ResolveCreditNoteKey(string marketCode, DateTimeOffset issuedAt, string creditNoteNumber, string kind = "pdf");
}

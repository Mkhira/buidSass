namespace BackendApi.Modules.Payments.Domain;

/// <summary>
/// Append-only PCI-scope audit-of-record per <c>data-model.md §pci_scope_events</c>.
/// Every event that could affect the SAQ-A boundary (KV slot add/remove,
/// hosted-fields domain change, provider add/remove) is recorded here for
/// AC-4 + AC-37 evidence. Append-only by convention; deletes forbidden.
/// </summary>
public sealed class PciScopeEvent
{
    public Guid Id { get; set; }
    public string EventKind { get; set; } = string.Empty;
    public Guid ChangedBy { get; set; }
    public string ChangeSummary { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

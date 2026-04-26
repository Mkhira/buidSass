namespace BackendApi.Modules.Returns.Entities;

/// <summary>data-model.md table 4. PK <c>(InspectionId, ReturnLineId)</c>.</summary>
public sealed class InspectionLine
{
    public Guid InspectionId { get; set; }
    public Guid ReturnLineId { get; set; }
    /// <summary>Per-market partitioning (Principle 5 / ADR-010).</summary>
    public string MarketCode { get; set; } = string.Empty;
    public int SellableQty { get; set; }
    public int DefectiveQty { get; set; }
    public string? PhotosJson { get; set; }

    public Inspection? Inspection { get; set; }
}

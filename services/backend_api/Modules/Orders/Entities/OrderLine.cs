namespace BackendApi.Modules.Orders.Entities;

/// <summary>
/// Order-line snapshot. Catalog can change after an order is placed; the snapshot guarantees
/// reproducibility for invoices and refunds (research R6).
/// </summary>
public sealed class OrderLine
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public int Qty { get; set; }
    public long UnitPriceMinor { get; set; }
    public long LineDiscountMinor { get; set; }
    public long LineTaxMinor { get; set; }
    public long LineTotalMinor { get; set; }
    public bool Restricted { get; set; }
    public string AttributesJson { get; set; } = "{}";
    public int CancelledQty { get; set; }
    public int ReturnedQty { get; set; }
    public Guid? ReservationId { get; set; }

    public Order? Order { get; set; }
}

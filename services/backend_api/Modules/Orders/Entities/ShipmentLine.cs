namespace BackendApi.Modules.Orders.Entities;

public sealed class ShipmentLine
{
    public Guid ShipmentId { get; set; }
    public Guid OrderLineId { get; set; }
    public int Qty { get; set; }

    public Shipment? Shipment { get; set; }
    public OrderLine? OrderLine { get; set; }
}

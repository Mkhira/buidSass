namespace BackendApi.Modules.Cart.Entities;

public sealed class CartAbandonedEmission
{
    public Guid CartId { get; set; }
    public DateTimeOffset LastEmittedAt { get; set; }
}

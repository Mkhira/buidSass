namespace BackendApi.Modules.Pricing.Entities;

public sealed class AccountB2BTier
{
    public Guid AccountId { get; set; }
    public Guid TierId { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
    public Guid AssignedByAccountId { get; set; }
}

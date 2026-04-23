namespace BackendApi.Modules.Pricing.Entities;

public sealed class Promotion
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = "percent_off";
    public string Name { get; set; } = string.Empty;
    public string ConfigJson { get; set; } = "{}";
    public Guid[]? AppliesToProductIds { get; set; }
    public Guid[]? AppliesToCategoryIds { get; set; }
    public string[] MarketCodes { get; set; } = Array.Empty<string>();
    public int Priority { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public string? OwnerId { get; set; }
    public Guid? VendorId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

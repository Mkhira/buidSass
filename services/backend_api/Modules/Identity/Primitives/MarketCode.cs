namespace BackendApi.Modules.Identity.Primitives;

public sealed record MarketCode
{
    public static readonly MarketCode Ksa = new("ksa");
    public static readonly MarketCode Eg = new("eg");
    public static readonly MarketCode Platform = new("platform");

    public string Value { get; }

    public MarketCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Market code cannot be empty.", nameof(value));
        }

        Value = value.Trim().ToLowerInvariant();
    }

    public override string ToString() => Value;
}

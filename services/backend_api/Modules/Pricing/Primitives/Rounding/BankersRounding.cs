namespace BackendApi.Modules.Pricing.Primitives.Rounding;

public static class BankersRounding
{
    /// <summary>
    /// Applies banker's (half-even) rounding to a decimal minor-unit amount and returns a long.
    /// Used at the end of each pricing layer to collapse fractional intermediate state into
    /// integer minor units without the systematic upward bias of half-up.
    /// </summary>
    public static long RoundMinor(decimal minorAmount)
    {
        return checked((long)Math.Round(minorAmount, 0, MidpointRounding.ToEven));
    }
}

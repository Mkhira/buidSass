namespace BackendApi.Modules.Pricing.Primitives.Rounding;

public static class BankersRounding
{
    /// <summary>
    /// Rounds a decimal amount (in major units) to integer minor units using half-even (banker's) rounding.
    /// Multiply by 100 then round-to-even. Matches finance convention and eliminates systematic upward bias.
    /// </summary>
    public static long ToMinor(decimal amount)
    {
        var scaled = Math.Round(amount, 0, MidpointRounding.ToEven);
        return (long)scaled;
    }

    /// <summary>
    /// Applies banker's rounding to a long calculation that may carry fractional intermediate state.
    /// </summary>
    public static long RoundMinor(decimal minorAmount)
    {
        return (long)Math.Round(minorAmount, 0, MidpointRounding.ToEven);
    }
}

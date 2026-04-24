namespace BackendApi.Modules.Checkout.Primitives;

/// <summary>
/// Market-indexed catalogue of supported payment methods + COD policy (FR-010, FR-011, R9).
/// The map is configuration-driven — not a hardcoded switch — so adding a new market is a
/// config change, not a code change (Principle 5).
/// </summary>
public sealed class PaymentMethodCatalog
{
    public const string Card = "card";
    public const string Mada = "mada";
    public const string ApplePay = "apple_pay";
    public const string StcPay = "stc_pay";
    public const string BankTransfer = "bank_transfer";
    public const string Cod = "cod";
    public const string Bnpl = "bnpl";

    private readonly Dictionary<string, MarketPaymentConfig> _markets;

    public PaymentMethodCatalog()
    {
        // Defaults per spec 010 FR-010 + R9. Override via Checkout:PaymentMethods:* when we
        // wire IOptions; for v1 the defaults ARE the launch config.
        _markets = new Dictionary<string, MarketPaymentConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["ksa"] = new(
                AllowedMethods: new[] { Card, Mada, ApplePay, StcPay, BankTransfer, Cod },
                CodEnabled: true,
                CodCapMinor: 2_000_00,          // 2000 SAR in minor units
                CodExcludesRestricted: true),
            ["eg"] = new(
                AllowedMethods: new[] { Card, ApplePay, BankTransfer, Cod, Bnpl },
                CodEnabled: true,
                CodCapMinor: 5_000_00,          // 5000 EGP in minor units
                CodExcludesRestricted: true),
        };
    }

    public bool IsMethodAllowed(string marketCode, string method) =>
        _markets.TryGetValue(marketCode, out var cfg)
        && cfg.AllowedMethods.Any(m => string.Equals(m, method, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<string> AllowedMethods(string marketCode) =>
        _markets.TryGetValue(marketCode, out var cfg) ? cfg.AllowedMethods : Array.Empty<string>();

    public CodEligibility CheckCod(string marketCode, long totalMinor, bool cartHasRestricted)
    {
        if (!_markets.TryGetValue(marketCode, out var cfg) || !cfg.CodEnabled)
        {
            return new CodEligibility(false, "cart.payment.cod_not_available");
        }
        if (cfg.CodExcludesRestricted && cartHasRestricted)
        {
            return new CodEligibility(false, "checkout.cod_restricted_product");
        }
        if (totalMinor > cfg.CodCapMinor)
        {
            return new CodEligibility(false, "checkout.cod_cap_exceeded");
        }
        return new CodEligibility(true, null);
    }

    public sealed record MarketPaymentConfig(
        IReadOnlyList<string> AllowedMethods,
        bool CodEnabled,
        long CodCapMinor,
        bool CodExcludesRestricted);

    public sealed record CodEligibility(bool Allowed, string? ReasonCode);
}

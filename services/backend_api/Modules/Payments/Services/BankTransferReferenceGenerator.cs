using System.Security.Cryptography;
using System.Text;

namespace BackendApi.Modules.Payments.Services;

/// <summary>
/// Research §7 — bank-transfer reference format:
/// <c>&lt;MARKET&gt;-&lt;UUID4-FIRST-8&gt;-&lt;SHORT-HASH&gt;</c>
/// e.g. <c>SA-3a7f9c12-X8K2</c>. Total length 17 chars — fits in bank-
/// statement memo fields (typically 16–32 chars).
/// </summary>
public static class BankTransferReferenceGenerator
{
    private static readonly char[] HashAlphabet =
        "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray(); // omit ambiguous I/O/0/1

    public static string Generate(string marketCode, Guid paymentId)
    {
        var market = marketCode.ToUpperInvariant();
        var prefix = paymentId.ToString("N")[..8];
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(paymentId.ToString("N")));
        var sb = new StringBuilder(4);
        for (int i = 0; i < 4; i++)
        {
            sb.Append(HashAlphabet[hash[i] % HashAlphabet.Length]);
        }
        return $"{market}-{prefix}-{sb}";
    }
}

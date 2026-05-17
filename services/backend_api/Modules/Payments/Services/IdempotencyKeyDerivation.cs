using System.Security.Cryptography;
using System.Text;

namespace BackendApi.Modules.Payments.Services;

/// <summary>
/// BR-3 idempotency-key derivation (research §3). Format:
/// <c>sha256(order_id + ":" + method + ":" + attempt_id)</c> as lowercase hex.
/// </summary>
public static class IdempotencyKeyDerivation
{
    public static string Derive(Guid orderId, string method, Guid attemptId)
    {
        var input = $"{orderId:N}:{method}:{attemptId:N}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

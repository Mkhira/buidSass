using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace BackendApi.Modules.Identity.Primitives;

public static class IdentityOpaqueTokenCodec
{
    public static TokenComponents Create(int secretBytes = 32)
    {
        var tokenId = Guid.NewGuid();
        var secret = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(secretBytes));
        return new TokenComponents(tokenId, secret);
    }

    public static bool TryParse(string rawToken, out TokenComponents token)
    {
        token = default;
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return false;
        }

        var parts = rawToken.Trim().Split('.', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        if (!Guid.TryParseExact(parts[0], "N", out var tokenId))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(parts[1]))
        {
            return false;
        }

        token = new TokenComponents(tokenId, parts[1]);
        return true;
    }
}

public readonly record struct TokenComponents(Guid TokenId, string Secret)
{
    public override string ToString()
    {
        return $"{TokenId:N}.{Secret}";
    }
}

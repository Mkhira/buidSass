using Microsoft.AspNetCore.DataProtection;

namespace BackendApi.Modules.Identity.Admin.Common;

public static class TotpSecretCodec
{
    private static readonly byte[] MagicHeader = [0x54, 0x4F, 0x54, 0x50, 0x01]; // TOTP + v1

    public static byte[] Decode(IDataProtector protector, byte[] payload)
    {
        byte[] unprotected;
        try
        {
            unprotected = protector.Unprotect(payload);
        }
        catch (Exception ex)
        {
            throw new TotpSecretUnprotectFailed("Unable to decrypt stored TOTP secret payload.", ex);
        }

        if (unprotected.Length <= MagicHeader.Length)
        {
            throw new TotpSecretUnprotectFailed("Decoded TOTP secret payload is malformed.");
        }

        if (!unprotected.AsSpan(0, MagicHeader.Length).SequenceEqual(MagicHeader))
        {
            throw new TotpSecretUnprotectFailed("Decoded TOTP secret payload header is invalid.");
        }

        return unprotected.AsSpan(MagicHeader.Length).ToArray();
    }

    public static byte[] Encode(IDataProtector protector, byte[] secretBytes)
    {
        var payload = new byte[MagicHeader.Length + secretBytes.Length];
        MagicHeader.AsSpan().CopyTo(payload);
        secretBytes.AsSpan().CopyTo(payload.AsSpan(MagicHeader.Length));
        return protector.Protect(payload);
    }
}

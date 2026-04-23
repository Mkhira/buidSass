using BackendApi.Modules.Identity.Entities;

namespace BackendApi.Modules.Identity.Primitives;

public static class RefreshTokenHashResolver
{
    public static byte[] Resolve(RefreshToken refreshToken)
    {
        return refreshToken.TokenSecretHash
            ?? refreshToken.TokenHash
            ?? [];
    }
}

using BackendApi.Modules.Identity.Admin.Common;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;

namespace Identity.Tests.Unit.Admin;

public sealed class TotpSecretCodecTests
{
    [Fact]
    public void Decode_WhenPayloadIsCorrupted_ThrowsTotpSecretUnprotectFailed()
    {
        var dataProtectionProvider = DataProtectionProvider.Create("identity-tests");
        var protector = dataProtectionProvider.CreateProtector("identity.admin.totp.secret.v1");
        var encoded = TotpSecretCodec.Encode(protector, [1, 2, 3, 4, 5, 6]);
        encoded[0] ^= 0xFF;

        var act = () => TotpSecretCodec.Decode(protector, encoded);

        act.Should().Throw<TotpSecretUnprotectFailed>();
    }
}

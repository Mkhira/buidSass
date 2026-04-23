using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BackendApi.Modules.Identity.Primitives;

public sealed class IdentityJwtOptionsValidator(IHostEnvironment hostEnvironment) : IValidateOptions<IdentityJwtOptions>
{
    public ValidateOptionsResult Validate(string? name, IdentityJwtOptions options)
    {
        if (hostEnvironment.IsDevelopment() || hostEnvironment.IsEnvironment("Test"))
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        ValidateSurface("Customer", options.Customer, failures);
        ValidateSurface("Admin", options.Admin, failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateSurface(
        string surfaceName,
        IdentityJwtSurfaceOptions surface,
        List<string> failures)
    {
        var hasCurrentKey =
            !string.IsNullOrWhiteSpace(surface.PrivateKeyPem)
            || !string.IsNullOrWhiteSpace(surface.PrivateKeyPath);

        if (!hasCurrentKey)
        {
            failures.Add(
                $"Identity:Jwt:{surfaceName}:PrivateKeyPem or Identity:Jwt:{surfaceName}:PrivateKeyPath is required outside Development/Test.");
        }

        for (var i = 0; i < surface.RetiredValidationKeys.Count; i++)
        {
            var retired = surface.RetiredValidationKeys[i];
            if (string.IsNullOrWhiteSpace(retired.KeyId))
            {
                failures.Add(
                    $"Identity:Jwt:{surfaceName}:RetiredValidationKeys:{i}:KeyId is required.");
            }

            var hasPublicKey =
                !string.IsNullOrWhiteSpace(retired.PublicKeyPem)
                || !string.IsNullOrWhiteSpace(retired.PublicKeyPath);

            if (!hasPublicKey)
            {
                failures.Add(
                    $"Identity:Jwt:{surfaceName}:RetiredValidationKeys:{i}:PublicKeyPem or PublicKeyPath is required.");
            }
        }
    }
}

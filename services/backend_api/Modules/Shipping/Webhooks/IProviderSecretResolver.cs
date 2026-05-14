namespace BackendApi.Modules.Shipping.Webhooks;

/// <summary>
/// Resolves the provider webhook signing secret from configuration / KV.
/// Wired against <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>
/// at the composition root; the production binding pulls from
/// <c>shipping/<market>/<provider>/api-key</c> KV slots via the
/// layered-configuration pipeline (E1 contract).
/// </summary>
public interface IProviderSecretResolver
{
    /// <summary>Returns a dictionary of <c>logical-path → secret-value</c> entries for the given provider.</summary>
    IReadOnlyDictionary<string, string> ResolveSecrets(string providerId);
}

/// <summary>
/// Default implementation backed by IConfiguration keys
/// <c>Shipping:Providers:<providerId>:WebhookSigningKey</c>. Tests bind a
/// dictionary directly.
/// </summary>
public sealed class ConfigurationProviderSecretResolver(Microsoft.Extensions.Configuration.IConfiguration configuration)
    : IProviderSecretResolver
{
    public IReadOnlyDictionary<string, string> ResolveSecrets(string providerId)
    {
        var key = configuration[$"Shipping:Providers:{providerId}:WebhookSigningKey"];
        if (string.IsNullOrWhiteSpace(key))
        {
            return new Dictionary<string, string>();
        }
        // The provider's ValidateWebhookSignature looks up by logical path.
        var logicalPath = providerId switch
        {
            BackendApi.Modules.Shipping.Primitives.ShippingConstants.Providers.Smsa => "shipping/sa/smsa/api-key",
            BackendApi.Modules.Shipping.Primitives.ShippingConstants.Providers.AramexKsa => "shipping/sa/aramex-ksa/api-key",
            BackendApi.Modules.Shipping.Primitives.ShippingConstants.Providers.AramexEg => "shipping/eg/aramex-eg/api-key",
            BackendApi.Modules.Shipping.Primitives.ShippingConstants.Providers.Bosta => "shipping/eg/bosta/api-key",
            _ => $"shipping/unknown/{providerId}/api-key",
        };
        return new Dictionary<string, string>(StringComparer.Ordinal) { [logicalPath] = key };
    }
}

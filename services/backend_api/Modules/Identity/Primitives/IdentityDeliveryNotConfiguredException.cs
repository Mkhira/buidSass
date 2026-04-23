namespace BackendApi.Modules.Identity.Primitives;

public sealed class IdentityDeliveryNotConfiguredException(string channel)
    : InvalidOperationException($"Identity delivery channel '{channel}' is not configured.")
{
    public string Channel { get; } = channel;
}

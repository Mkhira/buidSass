namespace BackendApi.Modules.Notifications.Workers;

/// <summary>
/// Resolves (recipient_id, channel) → channel-specific delivery address at
/// dispatch time. Implemented by the Identity / Customer module so the
/// Notifications module never persists recipient addresses — recipient PII
/// stays inside its owning module per Principle 25 audit/data-residency.
/// The default <see cref="SandboxRecipientAddressResolver"/> emits synthetic
/// addresses suitable for sandbox provider impls.
/// </summary>
public interface IRecipientAddressResolver
{
    /// <summary>Returns null if no deliverable address is available for the channel.</summary>
    Task<string?> ResolveAsync(Guid recipientId, string channel, CancellationToken cancellationToken);
}

/// <summary>
/// Default sandbox impl. Real Identity wiring lands when spec 004/029 ships
/// an <c>IdentityRecipientAddressResolver</c>. The sandbox version returns
/// deterministic placeholder addresses so dispatch + provider sandbox impls
/// exercise end-to-end without depending on Identity test fixtures.
/// </summary>
public sealed class SandboxRecipientAddressResolver : IRecipientAddressResolver
{
    public Task<string?> ResolveAsync(Guid recipientId, string channel, CancellationToken cancellationToken)
    {
        var idShort = recipientId.ToString("N")[..8];
        var address = channel switch
        {
            "email" => $"sandbox+{idShort}@example.test",
            "sms" => $"+966500{idShort[..5]}",
            "push" => $"fcm-token-sandbox-{idShort}",
            _ => null,
        };
        return Task.FromResult<string?>(address);
    }
}

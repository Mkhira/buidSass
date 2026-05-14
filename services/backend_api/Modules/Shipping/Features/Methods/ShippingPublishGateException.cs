namespace BackendApi.Modules.Shipping.Features.Methods;

/// <summary>
/// Thrown when the V-1 publish gate (Principle 4 — AR + EN names + reviewer
/// ≠ author) fails. Endpoint handlers convert it to a 400
/// <c>publish_gate_failed</c> response with the message detail. Using a
/// dedicated exception type avoids parsing exception text to detect the
/// gate failure.
/// </summary>
public sealed class ShippingPublishGateException(string message) : InvalidOperationException(message);

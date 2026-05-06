namespace BackendApi.Modules.Shared;

/// <summary>
/// Cross-module read contract for orders / order_lines used by spec 023's
/// support-ticket flow. Spec 011 implements; 023 consumes via DI.
/// Loose-coupling pattern (see specs 020 / 021 / 022).
/// </summary>
public interface IOrderLinkedReadContract
{
    /// <summary>
    /// Read the order or order_line for ownership + market-code resolution.
    /// </summary>
    /// <param name="kind">Either <c>"order"</c> or <c>"order_line"</c>.</param>
    Task<LinkedEntityReadResult?> ReadAsync(
        string kind,
        Guid linkedEntityId,
        Guid actorCustomerId,
        CancellationToken ct);
}

/// <summary>
/// Generic linked-entity read result used by every <c>I*LinkedReadContract</c>.
/// <see cref="MarketCode"/> is the authoritative market for FR-006a resolution;
/// <see cref="OwnedByActor"/> false → <c>403 support.ticket.linked_entity_not_owned</c>.
/// <see cref="VendorId"/> is propagated to the ticket row for P6 multi-vendor-readiness.
/// </summary>
public sealed record LinkedEntityReadResult(
    Guid LinkedEntityId,
    string MarketCode,
    bool OwnedByActor,
    Guid? VendorId,
    string? DisplaySummary);

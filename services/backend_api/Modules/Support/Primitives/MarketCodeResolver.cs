using BackendApi.Modules.Shared;

namespace BackendApi.Modules.Support.Primitives;

/// <summary>
/// FR-006a — linked-entity-first market resolution. Standalone tickets fall
/// back to the customer's market-of-record. Linked-entity-unavailable raises
/// <see cref="MarketCodeUnresolvableException"/> rather than silently
/// misrouting (Clarification Q3).
/// </summary>
public sealed class MarketCodeResolver
{
    private readonly IOrderLinkedReadContract _orders;
    private readonly IReturnLinkedReadContract _returns;
    private readonly IQuoteLinkedReadContract _quotes;
    private readonly IReviewLinkedReadContract _reviews;
    private readonly IVerificationLinkedReadContract _verifications;

    public MarketCodeResolver(
        IOrderLinkedReadContract orders,
        IReturnLinkedReadContract returns,
        IQuoteLinkedReadContract quotes,
        IReviewLinkedReadContract reviews,
        IVerificationLinkedReadContract verifications)
    {
        _orders = orders;
        _returns = returns;
        _quotes = quotes;
        _reviews = reviews;
        _verifications = verifications;
    }

    public async Task<MarketResolution> ResolveAsync(
        string? linkedEntityKind,
        Guid? linkedEntityId,
        Guid actorCustomerId,
        string customerMarketOfRecord,
        CancellationToken ct)
    {
        if (linkedEntityKind is null || linkedEntityId is null)
        {
            // Standalone — fall back to customer's market-of-record.
            return new MarketResolution(customerMarketOfRecord, OwnedByActor: true, VendorId: null);
        }

        LinkedEntityReadResult? hit = linkedEntityKind switch
        {
            TicketLinkedEntityKindNames.Order
                => await _orders.ReadAsync(TicketLinkedEntityKindNames.Order, linkedEntityId.Value, actorCustomerId, ct),
            TicketLinkedEntityKindNames.OrderLine
                => await _orders.ReadAsync(TicketLinkedEntityKindNames.OrderLine, linkedEntityId.Value, actorCustomerId, ct),
            TicketLinkedEntityKindNames.ReturnRequest
                => await _returns.ReadAsync(linkedEntityId.Value, actorCustomerId, ct),
            TicketLinkedEntityKindNames.Quote
                => await _quotes.ReadAsync(linkedEntityId.Value, actorCustomerId, ct),
            TicketLinkedEntityKindNames.Review
                => await _reviews.ReadAsync(linkedEntityId.Value, actorCustomerId, ct),
            TicketLinkedEntityKindNames.Verification
                => await _verifications.ReadAsync(linkedEntityId.Value, actorCustomerId, ct),
            _ => throw new InvalidOperationException($"Unknown linked-entity kind '{linkedEntityKind}'."),
        };

        if (hit is null)
        {
            // Linked entity unavailable / not found → explicit failure (Clarification Q3).
            throw new MarketCodeUnresolvableException(linkedEntityKind, linkedEntityId.Value);
        }

        return new MarketResolution(hit.MarketCode, hit.OwnedByActor, hit.VendorId);
    }
}

public sealed record MarketResolution(string MarketCode, bool OwnedByActor, Guid? VendorId);

/// <summary>
/// Thrown by <see cref="MarketCodeResolver"/> when a linked-entity-bound ticket
/// cannot resolve its market because the linked entity is unavailable.
/// Surfaced as <c>400 support.ticket.market_code_unresolvable</c>.
/// </summary>
public sealed class MarketCodeUnresolvableException : Exception
{
    public string LinkedEntityKind { get; }
    public Guid LinkedEntityId { get; }

    public MarketCodeUnresolvableException(string linkedEntityKind, Guid linkedEntityId)
        : base($"Cannot resolve market for linked entity '{linkedEntityKind}' / {linkedEntityId}.")
    {
        LinkedEntityKind = linkedEntityKind;
        LinkedEntityId = linkedEntityId;
    }
}

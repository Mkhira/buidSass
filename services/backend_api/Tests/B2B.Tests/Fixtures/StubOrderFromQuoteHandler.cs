using BackendApi.Modules.Shared;

namespace B2B.Tests.Fixtures;

/// <summary>
/// Spec 021 T053 — test-only stub for <see cref="IOrderFromQuoteHandler"/>. Used by
/// integration tests of US1 / US3 / US5 / US6 acceptance flows so they don't depend on
/// spec 011 being at DoD on main.
///
/// Default behavior: returns a fresh, deterministic <see cref="Guid"/> as the order id
/// and reports <see cref="OrderConversionResult.WasIdempotentReplay"/>=false. Tests
/// asserting the idempotent-replay path can pre-seed <see cref="ReplayMap"/> to coerce
/// a replay response.
///
/// NEVER registered in production DI. Lives in <c>Tests/B2B.Tests/Fixtures</c>.
/// </summary>
public sealed class StubOrderFromQuoteHandler : IOrderFromQuoteHandler
{
    /// <summary>Pre-seeded mapping from idempotency key → existing order id, simulating spec 011's idempotent replay.</summary>
    public Dictionary<Guid, Guid> ReplayMap { get; } = new();

    /// <summary>Captured invocations — useful for asserting transaction enrolment + line totals.</summary>
    public List<QuoteConversionRequest> Invocations { get; } = new();

    /// <summary>If set, the next call throws this exception instead of returning a result. Used to test rollback.</summary>
    public Exception? FailureToThrow { get; set; }

    public ValueTask<OrderConversionResult> CreateAsync(
        QuoteConversionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Invocations.Add(request);

        if (FailureToThrow is not null)
        {
            var ex = FailureToThrow;
            FailureToThrow = null;
            throw ex;
        }

        if (ReplayMap.TryGetValue(request.IdempotencyKey, out var existingOrderId))
        {
            return ValueTask.FromResult(new OrderConversionResult(existingOrderId, WasIdempotentReplay: true));
        }

        var orderId = Guid.NewGuid();
        ReplayMap[request.IdempotencyKey] = orderId;
        return ValueTask.FromResult(new OrderConversionResult(orderId, WasIdempotentReplay: false));
    }
}

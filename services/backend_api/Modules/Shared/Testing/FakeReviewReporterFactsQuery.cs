namespace BackendApi.Modules.Shared.Testing;

/// <summary>
/// In-process fake of <see cref="IReviewReporterFactsQuery"/>. Returns the
/// supplied facts for every customer — sufficient for tests that exercise the
/// qualified-reporter threshold path under both qualified + unqualified
/// branches (just construct two separate fakes).
/// </summary>
public sealed class FakeReviewReporterFactsQuery : IReviewReporterFactsQuery
{
    private readonly ReviewReporterFacts _facts;

    public FakeReviewReporterFactsQuery(ReviewReporterFacts facts) => _facts = facts;

    /// <summary>Convenience: 30-day-old, has-delivered-order. Qualifies under default policy.</summary>
    public static FakeReviewReporterFactsQuery Qualified { get; } =
        new(new ReviewReporterFacts(AccountAgeDays: 30, HasDeliveredOrder: true));

    /// <summary>Brand-new account, no delivered order. Fails the qualifier under default policy.</summary>
    public static FakeReviewReporterFactsQuery Unqualified { get; } =
        new(new ReviewReporterFacts(AccountAgeDays: 0, HasDeliveredOrder: false));

    public Task<ReviewReporterFacts> GetAsync(Guid customerId, CancellationToken ct) =>
        Task.FromResult(_facts);
}

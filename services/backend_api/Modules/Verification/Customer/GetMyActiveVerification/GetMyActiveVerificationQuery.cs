namespace BackendApi.Modules.Verification.Customer.GetMyActiveVerification;

/// <summary>
/// Customer's "current" verification per spec 020 contracts §2.2 — the most-
/// recent non-terminal row OR the active <c>approved</c> row. Returns null if
/// the customer has never submitted (no row found).
/// </summary>
/// <param name="Id">Verification id.</param>
/// <param name="State">Wire-format state.</param>
/// <param name="MarketCode">Market the verification was issued for.</param>
/// <param name="Profession">Customer-stated profession.</param>
/// <param name="SubmittedAt">When the customer submitted.</param>
/// <param name="DecidedAt">When the reviewer decided (if any).</param>
/// <param name="ExpiresAt">Approval expiry (only set when state == approved).</param>
/// <param name="RenewalOpen">True if the customer is inside the earliest renewal window of their active approval.</param>
/// <param name="NextAction">UI hint: "wait_for_review" / "provide_info" / "renew" / "resubmit_after_cooldown" / "verified" / "none".</param>
public sealed record GetMyActiveVerificationResponse(
    Guid Id,
    string State,
    string MarketCode,
    string Profession,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? DecidedAt,
    DateTimeOffset? ExpiresAt,
    bool RenewalOpen,
    string NextAction);

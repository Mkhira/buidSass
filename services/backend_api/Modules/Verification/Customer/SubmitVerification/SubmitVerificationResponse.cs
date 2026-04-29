namespace BackendApi.Modules.Verification.Customer.SubmitVerification;

/// <summary>
/// Successful submission echo per spec 020 contracts §2.1.
/// Customers use <see cref="State"/> to render the next-action UI hint
/// (typically "Your verification is under review").
/// </summary>
public sealed record SubmitVerificationResponse(
    Guid Id,
    string MarketCode,
    int SchemaVersion,
    string State,
    DateTimeOffset SubmittedAt,
    Guid? SupersedesId);

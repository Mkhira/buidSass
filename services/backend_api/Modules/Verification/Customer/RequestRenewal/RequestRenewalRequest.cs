namespace BackendApi.Modules.Verification.Customer.RequestRenewal;

/// <summary>
/// Customer requests renewal of an existing approval per spec 020 contracts §2.7.
/// V1 carries the prior approval's profession + regulator_identifier forward
/// automatically; if either changed (e.g., profession upgrade, license re-issue
/// with new number), the customer overrides via this body. Both fields are
/// optional in the request — null values fall back to the prior approval.
/// </summary>
public sealed record RequestRenewalRequest(
    string? Profession,
    string? RegulatorIdentifier);

public sealed record RequestRenewalResponse(
    Guid Id,
    Guid SupersedesId,
    string State,
    DateTimeOffset SubmittedAt);

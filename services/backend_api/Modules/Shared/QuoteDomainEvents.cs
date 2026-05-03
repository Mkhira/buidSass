using MediatR;

namespace BackendApi.Modules.Shared;

/// <summary>
/// Spec 021 quote lifecycle domain events (data-model §6). All implement
/// <see cref="INotification"/> for the in-process MediatR bus. Consumed by spec 025
/// (notifications). State writes never block on subscriber delivery (FR-043).
/// </summary>
public sealed record QuoteRequested(
    Guid QuoteId,
    Guid CustomerId,
    Guid? CompanyId,
    string MarketCode,
    string LocaleHint) : INotification;

public sealed record QuotePublished(
    Guid QuoteId,
    Guid QuoteVersionId,
    int VersionNumber,
    Guid CustomerId,
    Guid? CompanyId,
    string MarketCode,
    string LocaleHint,
    IReadOnlyDictionary<string, string> PdfStorageKeysByLocale) : INotification;

public sealed record QuotePendingApprover(
    Guid QuoteId,
    Guid CompanyId,
    IReadOnlyCollection<Guid> ApproverUserIds,
    Guid BuyerUserId,
    string MarketCode) : INotification;

public sealed record QuoteAccepted(
    Guid QuoteId,
    Guid OrderId,
    Guid CustomerId,
    Guid? CompanyId,
    string MarketCode,
    string LocaleHint) : INotification;

public sealed record QuoteRejected(
    Guid QuoteId,
    Guid CustomerId,
    Guid? CompanyId,
    string MarketCode,
    string LocaleHint) : INotification;

public sealed record QuoteApproverRejected(
    Guid QuoteId,
    Guid BuyerUserId,
    Guid RejectingApproverUserId,
    string MarketCode) : INotification;

public sealed record QuoteExpired(
    Guid QuoteId,
    Guid CustomerId,
    Guid? CompanyId,
    string MarketCode,
    string LocaleHint) : INotification;

public sealed record QuoteWithdrawn(
    Guid QuoteId,
    Guid CustomerId,
    Guid? CompanyId,
    string Reason,
    string MarketCode) : INotification;

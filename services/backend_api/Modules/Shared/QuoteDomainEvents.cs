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

/// <summary>
/// Customer / buyer requested a revision on a published quote. Spec 021 contract §2.6;
/// state transition <c>revised → drafted</c> (operator-only-visible). Spec 025 fans out
/// to the operator queue ("X has requested changes on quote Y") so the admin can pick
/// it up for re-authoring; the caller's <see cref="CommentLocaleHint"/> tells the
/// notifier which locale to render the inline comment in.
///
/// <para>
/// <see cref="ActorUserId"/> distinguishes individual-customer requests (where it
/// equals <c>CustomerId</c>) from buyer-membership requests on a company quote (where
/// the buyer's user-id and the quote's logical customer-of-record may differ —
/// company quotes can outlive a single buyer's tenure).
/// </para>
/// </summary>
public sealed record QuoteRevisionRequested(
    Guid QuoteId,
    Guid CustomerId,
    Guid? CompanyId,
    Guid ActorUserId,
    string MarketCode,
    string CommentLocaleHint) : INotification;

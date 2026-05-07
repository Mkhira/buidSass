using BackendApi.Modules.Shared;
using BackendApi.Modules.Support.Persistence;
using BackendApi.Modules.Support.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Support.Agent.GetTicketAdminDetail;

/// <summary>
/// Spec 023 T071 + T099 (admin half) + T136c — admin-side full ticket detail
/// handler. Internal notes are gated by role per
/// <c>contracts/support-tickets-contract.md §2</c>: support_agent /
/// support_lead / super_admin see all messages; customer roles never reach
/// this handler. The endpoint enforces the role gate before invoking the
/// handler; the handler additionally honours an explicit "is lead or super"
/// signal carried on the query for any future shared use.
///
/// <para>Linked-entity preview is resolved through the appropriate per-kind
/// read contract:
/// <see cref="IOrderLinkedReadContract"/> /
/// <see cref="IReturnLinkedReadContract"/> /
/// <see cref="IQuoteLinkedReadContract"/> /
/// <see cref="IReviewLinkedReadContract"/> /
/// <see cref="IVerificationLinkedReadContract"/>. The preview is best-effort:
/// a transient null from the contract degrades to a missing
/// <c>LinkedEntityPreview</c> rather than failing the read.</para>
///
/// <para>T136c FR-016a: ticket-author display uses
/// <see cref="IReviewDisplayHandleQuery"/> (handle if non-empty, else
/// first_name + ' ' + last_initial + '.'). For B2B customers the company
/// name is surfaced as a secondary line via
/// <see cref="ICompanyAccountQuery"/> — the company-name read itself is
/// out-of-scope for this slice (no contract owns "company display name"
/// at V1); the company id is surfaced verbatim so admin UIs can display
/// it.</para>
/// </summary>
public sealed class GetTicketAdminDetailHandler
{
    private readonly SupportDbContext _db;
    private readonly IReviewDisplayHandleQuery _displayHandleQuery;
    private readonly ICompanyAccountQuery _companyQuery;
    private readonly IOrderLinkedReadContract _orderRead;
    private readonly IReturnLinkedReadContract _returnRead;
    private readonly IQuoteLinkedReadContract _quoteRead;
    private readonly IReviewLinkedReadContract _reviewRead;
    private readonly IVerificationLinkedReadContract _verificationRead;

    public GetTicketAdminDetailHandler(
        SupportDbContext db,
        IReviewDisplayHandleQuery displayHandleQuery,
        ICompanyAccountQuery companyQuery,
        IOrderLinkedReadContract orderRead,
        IReturnLinkedReadContract returnRead,
        IQuoteLinkedReadContract quoteRead,
        IReviewLinkedReadContract reviewRead,
        IVerificationLinkedReadContract verificationRead)
    {
        _db = db;
        _displayHandleQuery = displayHandleQuery;
        _companyQuery = companyQuery;
        _orderRead = orderRead;
        _returnRead = returnRead;
        _quoteRead = quoteRead;
        _reviewRead = reviewRead;
        _verificationRead = verificationRead;
    }

    public async Task<GetTicketAdminDetailResult> HandleAsync(
        GetTicketAdminDetailQuery q, CancellationToken ct)
    {
        // CodeRabbit Loop-1: scope the root ticket read by the caller's
        // market_code per ADR-010. Super-admins may bypass the partition
        // (cross-market investigation requires it); agents/leads must remain
        // confined to their market. A foreign-market read is reported as
        // `LinkedEntityNotFound` rather than 403 to avoid leaking the
        // existence of cross-market tickets.
        var ticket = q.IsSuperAdmin
            ? await _db.Tickets.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == q.TicketId, ct)
            : await _db.Tickets.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == q.TicketId
                    && t.MarketCode == q.MarketCode, ct);
        if (ticket is null)
        {
            return new GetTicketAdminDetailResult(
                Success: false,
                ReasonCode: TicketReasonCode.LinkedEntityNotFound,
                Detail: "Ticket not found.");
        }

        // Per contracts §2: admin-side actors (agent / lead / super_admin)
        // always see all message kinds including internal_note. The endpoint
        // enforces this role gate before invoking. Customer reads use a
        // separate handler (GetMyTicketHandler) and never include
        // internal_note rows.
        var messages = await _db.Messages.AsNoTracking()
            .Where(m => m.TicketId == ticket.Id)
            .OrderBy(m => m.CreatedAtUtc)
            .Select(m => new AdminTicketMessageDto(
                m.Id, m.Kind, m.ActorId, m.ActorRole, m.Body, m.BodyLocale,
                m.LeadIntervention, m.RedactedAtUtc, m.RedactedByRole, m.CreatedAtUtc))
            .ToListAsync(ct);

        var assignments = await _db.Assignments.AsNoTracking()
            .Where(a => a.TicketId == ticket.Id)
            .OrderBy(a => a.AssignedAtUtc)
            .Select(a => new AdminTicketAssignmentDto(
                a.Id, a.AgentId, a.AssignmentKind, a.AssignedByActorId,
                a.JustificationNote, a.AssignedAtUtc, a.SupersededAtUtc, a.SupersededReason))
            .ToListAsync(ct);

        var links = await _db.Links.AsNoTracking()
            .Where(l => l.TicketId == ticket.Id)
            .OrderBy(l => l.CreatedAtUtc)
            .Select(l => new AdminTicketLinkDto(
                l.Id, l.Kind, l.LinkedEntityId, l.CreatedVia, l.CreatedAtUtc))
            .ToListAsync(ct);

        // T136c: FR-016a customer-display rule
        var customerDisplay = await BuildCustomerDisplayAsync(ticket.CustomerId, ct);

        // Linked-entity preview via the per-kind read contract.
        // Best-effort: a null result degrades to a missing preview rather
        // than failing the entire detail read.
        AdminTicketLinkedEntityPreview? linkedPreview = null;
        if (ticket.LinkedEntityKind is not null && ticket.LinkedEntityId is not null)
        {
            var preview = await ResolveLinkedEntityPreviewAsync(
                ticket.LinkedEntityKind,
                ticket.LinkedEntityId.Value,
                ticket.CustomerId,
                ct);
            linkedPreview = preview;
        }

        return new GetTicketAdminDetailResult(
            Success: true,
            ReasonCode: null,
            Detail: null,
            TicketId: ticket.Id,
            State: ticket.State,
            Category: ticket.Category,
            Priority: ticket.Priority,
            Subject: ticket.Subject,
            Body: ticket.Body,
            MarketCode: ticket.MarketCode,
            Locale: ticket.Locale,
            AssignedAgentId: ticket.AssignedAgentId,
            CreatedAtUtc: ticket.CreatedAtUtc,
            UpdatedAtUtc: ticket.UpdatedAtUtc,
            ResolvedAtUtc: ticket.ResolvedAtUtc,
            ClosedAtUtc: ticket.ClosedAtUtc,
            ReopenCount: ticket.ReopenCount,
            FirstResponseTargetMinutesSnapshot: ticket.FirstResponseTargetMinutesSnapshot,
            ResolutionTargetMinutesSnapshot: ticket.ResolutionTargetMinutesSnapshot,
            FirstResponseDueUtc: ticket.FirstResponseDueUtc,
            ResolutionDueUtc: ticket.ResolutionDueUtc,
            BreachAcknowledgedAtFirstResponse: ticket.BreachAcknowledgedAtFirstResponse,
            BreachAcknowledgedAtResolution: ticket.BreachAcknowledgedAtResolution,
            Customer: customerDisplay,
            LinkedEntityPreview: linkedPreview,
            Messages: messages,
            Assignments: assignments,
            Links: links);
    }

    private async Task<AdminTicketCustomerDisplay> BuildCustomerDisplayAsync(
        Guid customerId, CancellationToken ct)
    {
        var info = await _displayHandleQuery.GetAsync(customerId, ct);

        // T136c FR-016a: prefer ReviewDisplayHandle; otherwise compose
        // first_name + ' ' + last_initial + '.'.
        var displayName = "(unknown customer)";
        if (info is not null)
        {
            if (!string.IsNullOrWhiteSpace(info.ReviewDisplayHandle))
            {
                displayName = info.ReviewDisplayHandle.Trim();
            }
            else
            {
                var firstName = info.FirstName?.Trim() ?? string.Empty;
                var lastName = info.LastName?.Trim() ?? string.Empty;
                var initial = lastName.Length > 0 ? lastName[..1] + "." : string.Empty;
                // CodeRabbit Loop-2: trim the composed name so an empty
                // first_name doesn't surface as " S." in admin reads.
                displayName = string.IsNullOrEmpty(initial)
                    ? firstName
                    : $"{firstName} {initial}".Trim();
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = "(unknown customer)";
                }
            }
        }

        // B2B secondary line — surface the company id verbatim. The admin UI
        // owns label formatting (e.g. "Company: <id>"); returning a raw
        // identifier keeps this DTO purely data-shaped per Principle 28
        // and lets the client localize the label. No V1 contract provides a
        // human-readable company-name lookup; that lands when 020 ships its
        // company-display read.
        var companyId = await _companyQuery.ResolveCompanyIdAsync(customerId, ct);

        return new AdminTicketCustomerDisplay(
            CustomerId: customerId,
            DisplayName: displayName,
            CompanyName: companyId?.ToString());
    }

    private async Task<AdminTicketLinkedEntityPreview?> ResolveLinkedEntityPreviewAsync(
        string kind, Guid linkedEntityId, Guid customerId, CancellationToken ct)
    {
        LinkedEntityReadResult? result = kind switch
        {
            TicketLinkedEntityKindNames.Order => await _orderRead.ReadAsync(
                TicketLinkedEntityKindNames.Order, linkedEntityId, customerId, ct),
            TicketLinkedEntityKindNames.OrderLine => await _orderRead.ReadAsync(
                TicketLinkedEntityKindNames.OrderLine, linkedEntityId, customerId, ct),
            TicketLinkedEntityKindNames.ReturnRequest => await _returnRead.ReadAsync(
                linkedEntityId, customerId, ct),
            TicketLinkedEntityKindNames.Quote => await _quoteRead.ReadAsync(
                linkedEntityId, customerId, ct),
            TicketLinkedEntityKindNames.Review => await _reviewRead.ReadAsync(
                linkedEntityId, customerId, ct),
            TicketLinkedEntityKindNames.Verification => await _verificationRead.ReadAsync(
                linkedEntityId, customerId, ct),
            _ => null,
        };

        if (result is null) return null;
        return new AdminTicketLinkedEntityPreview(
            Kind: kind,
            LinkedEntityId: result.LinkedEntityId,
            MarketCode: result.MarketCode,
            VendorId: result.VendorId,
            DisplaySummary: result.DisplaySummary);
    }
}

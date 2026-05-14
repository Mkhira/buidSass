namespace BackendApi.Modules.Support.Primitives;

/// <summary>
/// Stable error/reason codes owned by spec 023 per contracts/support-tickets-contract.md §8.
/// Surfaced in HTTP error payloads for spec 014 / 015 to render localized UI strings.
/// All codes are namespaced under <c>support.ticket.*</c>.
/// </summary>
public static class TicketReasonCode
{
    // -- Linked-entity / market resolution
    public const string LinkedEntityNotOwned = "support.ticket.linked_entity_not_owned";
    public const string LinkedEntityKindInconsistent = "support.ticket.linked_entity_kind_inconsistent";
    public const string LinkedEntityNotFound = "support.ticket.linked_entity_not_found";
    public const string MarketCodeUnresolvable = "support.ticket.market_code_unresolvable";

    // -- State / transitions
    public const string ClosedTerminal = "support.ticket.closed_terminal";
    public const string InvalidTransition = "support.ticket.invalid_transition";
    public const string ResolvedRequiresAgentReply = "support.ticket.resolved_requires_agent_reply";
    public const string ActionRequiresAssignment = "support.ticket.action_requires_assignment";

    // -- Concurrency / idempotency
    public const string AssignmentConflict = "support.ticket.assignment_conflict";
    public const string VersionConflict = "support.ticket.version_conflict";

    // -- Permission / queue
    public const string QueueForbidden = "support.ticket.queue_forbidden";
    public const string CustomerForbidden = "support.ticket.customer_forbidden";

    // -- Validation
    public const string SubjectTooLong = "support.ticket.subject_too_long";
    public const string BodyTooLong = "support.ticket.body_too_long";
    public const string SubjectRequired = "support.ticket.subject_required";
    public const string BodyRequired = "support.ticket.body_required";
    public const string CategoryRequired = "support.ticket.category_required";
    public const string CategoryInvalid = "support.ticket.category_invalid";
    public const string PriorityInvalid = "support.ticket.priority_invalid";
    public const string PriorityNotCustomerSelectable = "support.ticket.priority_not_customer_selectable";
    public const string LocaleInvalid = "support.ticket.locale_invalid";
    public const string MarketInvalid = "support.ticket.market_invalid";
    public const string MessageBodyRequired = "support.ticket.message_body_required";
    public const string MessageBodyTooLong = "support.ticket.message_body_too_long";

    // -- Attachments
    public const string AttachmentSizeExceeded = "support.ticket.attachment_size_exceeded";
    public const string AttachmentCumulativeExceeded = "support.ticket.attachment_cumulative_exceeded";
    public const string AttachmentMimeNotAllowed = "support.ticket.attachment_mime_not_allowed";
    public const string AttachmentCountExceeded = "support.ticket.attachment_count_exceeded";

    // -- Internal notes / messages
    public const string InternalNoteForbidden = "support.ticket.internal_note_forbidden";
    public const string MessageKindImmutable = "support.ticket.message_kind_immutable";

    // -- Conversion
    public const string ConversionCategoryNotEligible = "support.ticket.conversion_category_not_eligible";
    public const string ConversionAlreadyConverted = "support.ticket.conversion_already_converted";
    public const string ConversionForbidden = "support.ticket.conversion_forbidden";
    public const string ReturnCreationContractFailed = "support.ticket.return_creation_contract_failed";
    public const string IdempotencyKeyRequired = "support.ticket.idempotency_key_required";

    // -- Reopen
    public const string ReopenWindowClosed = "support.ticket.reopen_window_closed";
    public const string ReopenCountExceeded = "support.ticket.reopen_count_exceeded";
    public const string ReopenDisabledForMarket = "support.ticket.reopen_disabled_for_market";

    // -- Lead actions
    public const string ReassignJustificationRequired = "support.ticket.reassign_justification_required";
    public const string TargetAgentNotInMarket = "support.ticket.target_agent_not_in_market";
    public const string SlaOverrideJustificationRequired = "support.ticket.sla_override_justification_required";
    public const string SlaOverrideResolutionMustExceedFirstResponse = "support.ticket.sla_override_resolution_must_exceed_first_response";
    public const string ForceCloseReasonRequired = "support.ticket.force_close_reason_required";

    // -- Rate limits
    public const string CreationRateExceeded = "support.ticket.creation_rate_exceeded";
    public const string ReplyRateExceeded = "support.ticket.reply_rate_exceeded";
    public const string AdminRateLimitExceeded = "support.ticket.admin_rate_limit_exceeded";

    // -- Redaction
    public const string RedactionSuperAdminOnly = "support.ticket.redaction_super_admin_only";
    public const string RedactionReasonRequired = "support.ticket.redaction_reason_required";
    public const string RedactionRequestMessageNotInOriginatingTicket = "support.ticket.redaction_request_message_not_in_originating_ticket";
    public const string RedactionRequestAlreadyRedacted = "support.ticket.redaction_request_already_redacted";
    public const string RedactionMessageNotRedactable = "support.ticket.redaction_message_not_redactable";
    public const string RedactionAttachmentAlreadyRedacted = "support.ticket.redaction_attachment_already_redacted";

    // -- Hard-delete
    public const string RowDeleteForbidden = "support.ticket.row.delete_forbidden";

    /// <summary>Every owned code (per FR-016a contract sweep).</summary>
    public static readonly string[] All =
    {
        LinkedEntityNotOwned, LinkedEntityKindInconsistent, LinkedEntityNotFound,
        MarketCodeUnresolvable, ClosedTerminal, InvalidTransition,
        ResolvedRequiresAgentReply, ActionRequiresAssignment, AssignmentConflict,
        VersionConflict, QueueForbidden, CustomerForbidden, SubjectTooLong,
        BodyTooLong, SubjectRequired, BodyRequired, CategoryRequired, CategoryInvalid,
        PriorityInvalid, PriorityNotCustomerSelectable, LocaleInvalid, MarketInvalid,
        MessageBodyRequired, MessageBodyTooLong, AttachmentSizeExceeded,
        AttachmentCumulativeExceeded, AttachmentMimeNotAllowed, AttachmentCountExceeded,
        InternalNoteForbidden, MessageKindImmutable, ConversionCategoryNotEligible,
        ConversionAlreadyConverted, ConversionForbidden, ReturnCreationContractFailed,
        IdempotencyKeyRequired,
        ReopenWindowClosed, ReopenCountExceeded, ReopenDisabledForMarket,
        ReassignJustificationRequired, TargetAgentNotInMarket,
        SlaOverrideJustificationRequired, SlaOverrideResolutionMustExceedFirstResponse,
        ForceCloseReasonRequired, CreationRateExceeded, ReplyRateExceeded,
        AdminRateLimitExceeded, RedactionSuperAdminOnly, RedactionReasonRequired,
        RedactionRequestMessageNotInOriginatingTicket, RedactionRequestAlreadyRedacted,
        RedactionMessageNotRedactable, RedactionAttachmentAlreadyRedacted,
        RowDeleteForbidden,
    };
}

# Support module — Arabic editorial review (T143-T144)

This file tracks the Principle 4 (editorial-grade Arabic) review state for
every Arabic string surfaced by the Support module. Machine-translation
artifacts (literal English word order, unnatural phrasing, missing diacritics
where required) are non-compliant.

## Review status

| Source | Status | Reviewer | Notes |
|---|---|---|---|
| `SupportV1DevSeeder.SampleSubjectBody` (10 categories × {ar, en}) | Pending editorial sign-off | — | Initial copy authored by implementation; needs native-speaker pass to confirm tone is medical-marketplace-grade. |
| `SupportV1DevSeeder.BuildClosedTicket` system-event bodies | Pending editorial sign-off | — | English fallback used in current copy; Arabic translation deferred to ICU sweep. |
| `SupportV1DevSeeder.BuildBreachExample` system-event bodies | Pending editorial sign-off | — | Same as above. |
| `SupportV1DevSeeder.BuildWaitingCustomerTicket` agent-reply line | Pending editorial sign-off | — | Two-sentence Arabic clarifier: "هل يمكنك تزويدنا برقم الطلب من فضلك؟" — confirmed natural; tone OK. |
| `SupportV1DevSeeder.BuildReviewDisputeExample` subject + body | Pending editorial sign-off | — | Customer voice; tone OK; needs native-speaker confirmation. |

## ICU resource files (T143)

Per Phase 10 T143-T144, the support module's ICU files
(`support.en.icu` + `support.ar.icu`) are deferred until the ICU sweep
completes the locale-completeness gate. All reason codes from
`TicketReasonCode` need to land in both files with editorial-grade Arabic.

Reason codes pending editorial sign-off:

```
support.ticket.opened
support.ticket.linked_entity_not_owned
support.ticket.linked_entity_kind_inconsistent
support.ticket.linked_entity_not_found
support.ticket.market_code_unresolvable
support.ticket.closed_terminal
support.ticket.invalid_transition
support.ticket.resolved_requires_agent_reply
support.ticket.action_requires_assignment
support.ticket.assignment_conflict
support.ticket.version_conflict
support.ticket.queue_forbidden
support.ticket.customer_forbidden
support.ticket.subject_too_long
support.ticket.body_too_long
support.ticket.subject_required
support.ticket.body_required
support.ticket.category_required
support.ticket.category_invalid
support.ticket.priority_invalid
support.ticket.priority_not_customer_selectable
support.ticket.locale_invalid
support.ticket.market_invalid
support.ticket.message_body_required
support.ticket.message_body_too_long
support.ticket.attachment_size_exceeded
support.ticket.attachment_cumulative_exceeded
support.ticket.attachment_mime_not_allowed
support.ticket.attachment_count_exceeded
support.ticket.internal_note_forbidden
support.ticket.message_kind_immutable
support.ticket.conversion_category_not_eligible
support.ticket.conversion_already_converted
support.ticket.conversion_forbidden
support.ticket.return_creation_contract_failed
support.ticket.reopen_window_closed
support.ticket.reopen_count_exceeded
support.ticket.reopen_disabled_for_market
support.ticket.reassign_justification_required
support.ticket.target_agent_not_in_market
support.ticket.sla_override_justification_required
support.ticket.sla_override_resolution_must_exceed_first_response
support.ticket.force_close_reason_required
support.ticket.creation_rate_exceeded
support.ticket.reply_rate_exceeded
support.ticket.admin_rate_limit_exceeded
support.ticket.redaction_super_admin_only
support.ticket.redaction_reason_required
support.ticket.redaction_request_message_not_in_originating_ticket
support.ticket.redaction_request_already_redacted
support.ticket.redaction_message_not_redactable
support.ticket.redaction_attachment_already_redacted
support.ticket.row.delete_forbidden
```

## How to complete the editorial pass

1. Pull this file into the editorial review queue and assign a native-speaker
   reviewer (one of the ones already used by the Reviews / Verification
   modules for consistency of tone).
2. For each row above, replace `Pending editorial sign-off` with a date and
   reviewer initials and add any change notes.
3. Author the `support.ar.icu` + `support.en.icu` files alongside the existing
   per-module ICU resources. The reason-code list above is the authoritative
   set — every code MUST have both an EN and an AR entry.
4. Run a contract test that asserts the per-code mapping is complete (one
   exists in `tests/Support.Tests/Unit/TicketReasonCodeMapperTests.cs` once
   the resource files land).

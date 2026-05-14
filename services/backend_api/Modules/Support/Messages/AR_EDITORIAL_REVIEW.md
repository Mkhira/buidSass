# Support module — Arabic editorial review (T143-T144)

This file tracks the Principle 4 (editorial-grade Arabic) review state for
every Arabic string surfaced by the Support module. Machine-translation
artifacts (literal English word order, unnatural phrasing, missing diacritics
where required) are non-compliant.

The ICU resource files (`support.en.icu` + `support.ar.icu`) now ship with
the module. The Arabic file is marked `editorial_status: draft-pending-review`
in its `_meta` block. **Acceptance criterion (T143 / T144)**: every row below
MUST have reviewer initials + date BEFORE any spec 014 (storefront) or
spec 015 (admin shell) PR that surfaces these strings to a production-bound
user-facing surface may merge.

## Review status

### Reason-code strings (`support.ar.icu`)

| Key | Status | Reviewer | Notes |
|---|---|---|---|
| `support.ticket.opened` | Pending editorial sign-off | — | |
| `support.ticket.linked_entity_not_owned` | Pending editorial sign-off | — | |
| `support.ticket.linked_entity_kind_inconsistent` | Pending editorial sign-off | — | |
| `support.ticket.linked_entity_not_found` | Pending editorial sign-off | — | |
| `support.ticket.market_code_unresolvable` | Pending editorial sign-off | — | |
| `support.ticket.closed_terminal` | Pending editorial sign-off | — | |
| `support.ticket.invalid_transition` | Pending editorial sign-off | — | |
| `support.ticket.resolved_requires_agent_reply` | Pending editorial sign-off | — | |
| `support.ticket.action_requires_assignment` | Pending editorial sign-off | — | |
| `support.ticket.assignment_conflict` | Pending editorial sign-off | — | |
| `support.ticket.version_conflict` | Pending editorial sign-off | — | |
| `support.ticket.queue_forbidden` | Pending editorial sign-off | — | |
| `support.ticket.customer_forbidden` | Pending editorial sign-off | — | |
| `support.ticket.subject_too_long` | Pending editorial sign-off | — | |
| `support.ticket.body_too_long` | Pending editorial sign-off | — | |
| `support.ticket.subject_required` | Pending editorial sign-off | — | |
| `support.ticket.body_required` | Pending editorial sign-off | — | |
| `support.ticket.category_required` | Pending editorial sign-off | — | |
| `support.ticket.category_invalid` | Pending editorial sign-off | — | |
| `support.ticket.priority_invalid` | Pending editorial sign-off | — | |
| `support.ticket.priority_not_customer_selectable` | Pending editorial sign-off | — | |
| `support.ticket.locale_invalid` | Pending editorial sign-off | — | |
| `support.ticket.market_invalid` | Pending editorial sign-off | — | |
| `support.ticket.message_body_required` | Pending editorial sign-off | — | |
| `support.ticket.message_body_too_long` | Pending editorial sign-off | — | |
| `support.ticket.attachment_size_exceeded` | Pending editorial sign-off | — | |
| `support.ticket.attachment_cumulative_exceeded` | Pending editorial sign-off | — | |
| `support.ticket.attachment_mime_not_allowed` | Pending editorial sign-off | — | |
| `support.ticket.attachment_count_exceeded` | Pending editorial sign-off | — | |
| `support.ticket.internal_note_forbidden` | Pending editorial sign-off | — | |
| `support.ticket.message_kind_immutable` | Pending editorial sign-off | — | |
| `support.ticket.conversion_category_not_eligible` | Pending editorial sign-off | — | |
| `support.ticket.conversion_already_converted` | Pending editorial sign-off | — | |
| `support.ticket.conversion_forbidden` | Pending editorial sign-off | — | |
| `support.ticket.return_creation_contract_failed` | Pending editorial sign-off | — | |
| `support.ticket.idempotency_key_required` | Pending editorial sign-off | — | |
| `support.ticket.reopen_window_closed` | Pending editorial sign-off | — | |
| `support.ticket.reopen_count_exceeded` | Pending editorial sign-off | — | |
| `support.ticket.reopen_disabled_for_market` | Pending editorial sign-off | — | |
| `support.ticket.reassign_justification_required` | Pending editorial sign-off | — | |
| `support.ticket.target_agent_not_in_market` | Pending editorial sign-off | — | |
| `support.ticket.sla_override_justification_required` | Pending editorial sign-off | — | |
| `support.ticket.sla_override_resolution_must_exceed_first_response` | Pending editorial sign-off | — | |
| `support.ticket.force_close_reason_required` | Pending editorial sign-off | — | |
| `support.ticket.creation_rate_exceeded` | Pending editorial sign-off | — | |
| `support.ticket.reply_rate_exceeded` | Pending editorial sign-off | — | |
| `support.ticket.admin_rate_limit_exceeded` | Pending editorial sign-off | — | |
| `support.ticket.redaction_super_admin_only` | Pending editorial sign-off | — | |
| `support.ticket.redaction_reason_required` | Pending editorial sign-off | — | |
| `support.ticket.redaction_request_message_not_in_originating_ticket` | Pending editorial sign-off | — | |
| `support.ticket.redaction_request_already_redacted` | Pending editorial sign-off | — | |
| `support.ticket.redaction_message_not_redactable` | Pending editorial sign-off | — | |
| `support.ticket.redaction_attachment_already_redacted` | Pending editorial sign-off | — | |
| `support.ticket.row.delete_forbidden` | Pending editorial sign-off | — | |

### Dev seeder strings (`SupportV1DevSeeder`)

These never reach a production-bound surface (the seeder is Dev/Staging-only
via `SeedGuard`). They are scheduled to migrate into the ICU files as part of
the same editorial pass.

| Source | Status | Reviewer | Notes |
|---|---|---|---|
| `SupportV1DevSeeder.SampleSubjectBody` (10 categories × {ar, en}) | Pending editorial sign-off | — | Initial copy authored by implementation; needs native-speaker pass to confirm tone is medical-marketplace-grade. |
| `SupportV1DevSeeder.BuildClosedTicket` system-event bodies | Pending editorial sign-off | — | English fallback used in current copy; Arabic translation deferred to ICU sweep. |
| `SupportV1DevSeeder.BuildBreachExample` system-event bodies | Pending editorial sign-off | — | Same as above. |
| `SupportV1DevSeeder.BuildWaitingCustomerTicket` agent-reply line | Pending editorial sign-off | — | Two-sentence Arabic clarifier: "هل يمكنك تزويدنا برقم الطلب من فضلك؟" — tone OK; needs native-speaker confirmation. |
| `SupportV1DevSeeder.BuildReviewDisputeExample` subject + body | Pending editorial sign-off | — | Customer voice; tone OK; needs native-speaker confirmation. |

## How to complete the editorial pass

1. Pull this file into the editorial review queue and assign a native-speaker
   reviewer (one of the ones already used by the Reviews / Verification
   modules for consistency of tone).
2. For each row above, replace `Pending editorial sign-off` with a date and
   reviewer initials and add any change notes.
3. Migrate the dev-seeder strings into ICU keys (or document the policy that
   they stay inline because the seeder is non-prod).
4. Flip the `editorial_status` field in `support.ar.icu` `_meta` block from
   `draft-pending-review` to `reviewed`.
5. The `TicketReasonCodeIcuKeyCompletenessTests` unit test (Phase 10 T143
   coverage) already asserts that every `TicketReasonCode` constant has an
   entry in BOTH bundles — keep it passing.

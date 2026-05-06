# Spec 021 — Arabic editorial review queue

Per Constitution Principle 4 ("Arabic quality MUST be editorial-grade, not
machine-translated"), every Arabic string shipped under `b2b.ar.icu` MUST pass
editorial review before the spec ships externally.

This file tracks the review status of each ICU key. The Phase-1F editorial pass
flips these to `reviewed` once a native Arabic-speaking reviewer has signed off.

> **Phase 10 / T147 status (2026-05-05):** the queue below is intentionally
> left in `draft` status. T147 is a human-in-the-loop linguistic gate — it
> requires a native Arabic-speaking reviewer to verify register, terminology
> ("PO" vs "أمر شراء", "approver" vs "مُعتمِد"), and RTL number / date
> presentation against the in-market admin tone. Marking entries `reviewed`
> without that human pass would violate Principle 4. The review remains a
> Phase-1F gate as documented in the spec; this PR ships every other Phase 10
> task and leaves the editorial sweep as the only intentional follow-up.

The **Used by** column flags which slice / handler currently emits the key
on a customer-facing path; reviewer can prioritize by traffic / blast radius.
A blank Used-by means the key is reserved by data-model §3 (state machine /
contract surface) but not yet emitted by a live handler.

| ICU key | Status | Used by | Reviewer | Date |
|---|---|---|---|---|
| b2b.reason.quote.required_field_missing | draft | US1/Cycle-B RequestQuoteFromCart (validation, idempotency-key gate) | — | — |
| b2b.reason.quote.cart_empty | draft | US1/Cycle-B RequestQuoteFromCart (empty snapshot) | — | — |
| b2b.reason.quote.product_not_quotable | draft | reserved (US2 RequestQuoteFromProduct) | — | — |
| b2b.reason.quote.no_active_company_membership | draft | US1/Cycle-B RequestQuoteFromCart (membership / branch checks) | — | — |
| b2b.reason.quote.po_required | draft | US1/Cycle-B RequestQuoteFromCart (company.po_required=true) | — | — |
| b2b.reason.quote.po_already_used | draft | US1/Cycle-B RequestQuoteFromCart (unique_po_required collision; UX_quotes_company_po race) | — | — |
| b2b.reason.quote.po_warning_acknowledged | draft | reserved (US1 SubmitAcceptance soft-warning audit metadata) | — | — |
| b2b.reason.quote.rate_limit_exceeded | draft | US1/Cycle-B RequestQuoteFromCart (per-customer + per-company FR-045 buckets) | — | — |
| b2b.reason.quote.market_mismatch | draft | US1/Cycle-B RequestQuoteFromCart (FR-011 cross-market / missing schema fallback) | — | — |
| b2b.reason.quote.eligibility_required | draft | reserved (US1 SubmitAcceptance / US6 Conversion FR-036) | — | — |
| b2b.reason.quote.invalid_state_for_action | draft | US1/Cycle-C2 WithdrawQuote + RequestRevision (terminal-state attempts; xmin race; non-`revised` source for revision); reserved SubmitAcceptance | — | — |
| b2b.reason.quote.no_changes_provided | draft | US1/Cycle-C2 RequestRevision (whole-comment-missing — buyer pinged endpoint without saying what to change) | — | — |
| b2b.reason.quote.no_approver_available | draft | reserved (US1 SubmitAcceptance Q1) | — | — |
| b2b.reason.quote.cooldown_active | draft | reserved (research §R10 — V1 unused, kept for forward compat) | — | — |
| b2b.reason.quote.already_decided | draft | reserved (approver finalize race) | — | — |
| b2b.reason.quote.reason_required | draft | reserved (RequestRevision / approver reject / admin draft) | — | — |
| b2b.reason.quote.below_baseline_reason_required | draft | reserved (US3 AuthorQuoteDraft FR-040) | — | — |
| b2b.reason.quote.expired | draft | reserved (US1 SubmitAcceptance race vs expiry worker) | — | — |
| b2b.reason.quote.tax_preview_drift_threshold_exceeded | draft | reserved (US6 Conversion R11) | — | — |
| b2b.reason.quote.idempotency_replay | draft | reserved (idempotency-key replay marker) | — | — |
| b2b.reason.quote.account_inactive | draft | reserved (FR-038 layered in Cycle C once Modules/Shared probe lands) | — | — |
| b2b.reason.quote.company_suspended | draft | US1/Cycle-B RequestQuoteFromCart (FR-026) | — | — |
| b2b.reason.quote.product_archived | draft | reserved (US3 admin authoring edge case) | — | — |
| b2b.reason.quote.not_found | draft | US1/Cycle-C1 GetMyQuote + US1/Cycle-C2 WithdrawQuote / RequestRevision (visibility-leak: 404 fires for unknown-id, not-authorized read, OR read-but-not-write authority); reserved Document | — | — |
| b2b.reason.quote.document_not_found | draft | reserved (US1 DownloadQuoteVersionDocument) | — | — |
| b2b.reason.company.tax_id_invalid | draft | reserved (US4 RegisterCompany) | — | — |
| b2b.reason.company.duplicate_tax_id | draft | reserved (US4 RegisterCompany) | — | — |
| b2b.reason.company.last_admin_cannot_be_removed | draft | reserved (US4 membership invariants FR-024) | — | — |
| b2b.reason.company.last_approver_cannot_be_removed_with_required | draft | reserved (US4 membership invariants FR-025) | — | — |
| b2b.reason.company.member_already_exists | draft | reserved (US4 invitations) | — | — |
| b2b.reason.company.invitation_email_invalid | draft | reserved (US4 invitations) | — | — |
| b2b.reason.company.invitation_already_pending | draft | reserved (US4 invitations) | — | — |
| b2b.reason.company.invitation_expired | draft | reserved (US4 invitations) | — | — |
| b2b.reason.template.name_already_exists | draft | reserved (US7 SaveAsTemplate) | — | — |

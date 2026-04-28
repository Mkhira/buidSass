# Feature Specification: Reviews & Moderation

**Feature Branch**: `phase_1D_creating_specs` (working) · target merge branch: `022-reviews-moderation`
**Created**: 2026-04-28
**Status**: Draft
**Constitution**: v1.0.0
**Phase**: 1D (Business Modules · Milestone 7)
**Depends on**: 011 `orders` at DoD (delivered + not-refunded eligibility query); 015 `admin-foundation` contract merged to `main` (admin shell + RBAC + audit panel)
**Soft-couples to**: 005 `catalog` (product page rating aggregate consumer); 013 `returns-and-refunds` (refund-state observer for retroactive review hiding); 019 `admin-customers` (customer-segment lookups); 023 `support-tickets` (escalation path); 025 `notifications` (admin moderation digest)
**Consumed by**: 005 / customer storefront (product rating + review feed); 022's product-rating aggregate is read by 005's product-detail screen and 006's search-result decoration
**Input**: User description: "Phase 1D, spec 022 — reviews moderation. Review entity linked to delivered order line; customer submission only if the order is in delivered state and not refunded; admin moderation queue with flag reasons, hide / delete, and reinstate; profanity / abuse filter hook; aggregated rating on product detail; admin notes on review (audited); report-review flow for other customers; reviews-v1 seeder spanning verified-buyer reviews in moderated + flagged states. Bilingual AR + EN end-to-end; multi-vendor-ready."

## Clarifications

### Session 2026-04-28

- Q: Eligibility window — for how long after a delivered, non-refunded order line MAY a verified buyer submit a review? → A: **Per-market configurable, default 180 days from `delivered_at`** (KSA + EG both default to 180 days at V1 launch; `super_admin` may tune the window per market via spec 015 settings; range 30–730 days). After the window closes, the eligibility query returns `review.eligibility.window_closed` and the customer cannot submit. The window is computed from the order line's `delivered_at` timestamp owned by spec 011, NOT from the order's payment-completion time.
- Q: One-review-per-buyer-per-product or one-review-per-buyer-per-order-line? → A: **One review per `(customer_id, product_id)` globally**, regardless of how many times the customer has bought that product. The first delivered, non-refunded line establishes eligibility; subsequent purchases of the same product cannot create a second review by the same customer. The customer MAY edit the existing review until 30 days after `created_at` (per-market configurable; range 7–90 days); after the edit window closes the review is read-only. Editing resets moderation state to `pending_moderation` if the profanity filter trips on the edit.
- Q: Profanity / abuse filter — block at submission time, hold for moderation, or post-publish flag? → A: **Hold for moderation when the filter trips**, never silently block. A submission that trips the filter persists to the DB in state `pending_moderation` (NOT visible to other customers; visible to its author + the admin queue). The author sees a "your review is being reviewed by our team" confirmation, NOT an editorial rejection. Reviews that don't trip the filter publish immediately to state `visible`. The filter wordlist is per-market (different language profiles) and seeded by `reviews-v1`; `super_admin` updates the lists via spec 015 settings.
- Q: Customer report-review flow — what reasons may a reporter pick, and what visibility threshold escalates the review for admin attention? → A: **Five fixed reasons** (per-market ICU-keyed): `inappropriate_language`, `spam_or_irrelevant`, `personal_attack`, `false_or_misleading`, `other_with_required_note`. A reporter must be a signed-in customer (anonymous reports are rejected). A review that accumulates **3 distinct customer reports within 30 days** automatically transitions to `flagged` and surfaces in the admin moderation queue with a "community-reported" badge; the threshold is per-market configurable (range 1–10) by `super_admin`.
- Q: Refund-then-already-published-review semantics — when an order line is refunded after the review was published, what happens? → A: **The review is auto-transitioned to `hidden` with reason `auto_hidden:order_refunded` and an audit row.** The customer is notified and may dispute the auto-hide via spec 023 support tickets. The review is NOT deleted (FR-005a-style preservation). Storefront product-detail and search no longer surface the review; the rating aggregate excludes it. If the refund is later reversed (rare, manual), the review remains `hidden` until an admin manually reinstates it — admins are notified via the moderation queue.
- Q: Bilingual review body — required at submission, or one-locale-only? → A: **One locale required at submission; the other locale stays `null`.** The submitted locale is the customer's authoring `locale` field (`ar` or `en`). The unauthored locale is NEVER auto-machine-translated (Principle 4 — no machine-translated AR). The storefront renders the available locale and prepends an annotation `Original written in {locale}` when the viewer's locale differs from the authored locale. Admin / moderator UI shows the locale of authorship verbatim. A future Phase 1.5 spec MAY introduce a `reviews.translator` role + admin-translation queue for editorial second-locale; that is out of scope for V1.
- Q: Image / media moderation at V1 — auto-publish, hold for moderation, gate on prior trust, or drop media support? → A: **All image attachments are held for moderation regardless of text-filter result.** Any review with `media_urls.length > 0` MUST persist in state `pending_moderation` on submission, even when the text passes the profanity filter and would otherwise have published immediately. Text-only reviews continue to publish to `visible` whenever the text filter passes. The held review's audit row carries `triggered_by=customer_submission` and a sub-flag `media_attachment_review_required=true` so the moderator queue can filter on this distinct hold reason. Image-content moderation is a human task at V1; no automated image classifier ships in this spec. Future Phase 1.5+ MAY add an image-classifier hook to short-circuit obvious cases (e.g., NSFW detection); that is out of scope for V1.
- Q: Customer edits during `pending_moderation` — block, allow with re-stamp, allow without re-stamp, or rate-limited? → A: **Edits ALLOWED while in `pending_moderation`; each edit re-stamps `pending_moderation_started_at` and invalidates any in-progress moderator decision via optimistic concurrency.** A moderator who decides against an older `row_version` MUST receive `409 reviews.moderation.version_conflict` with the latest content embedded for re-decision. The audit log preserves every edit version (full `before / after` snapshot per edit). The queue MUST surface an `edited-since-last-surface` indicator on items that have been edited since the moderator's most recent open. Customer-side edit rate limit (FR-040 envelope: 5 edits / hour / customer) still applies as the abuse safeguard.
- Q: Multi-account / sock-puppet abuse on community reports — should reports be weighted by reporter quality? → A: **Reports MUST be weighted by reporter-account-age + verified-buyer status; only "qualified" reports count toward the auto-flag threshold.** A report is `qualified` and counts toward the FR-023 threshold (default 3) when ALL of: (a) reporter's `customer` account is ≥ **14 days old** at report time; AND (b) reporter has at least **one delivered, non-refunded order line** on the platform (any product, not just the reviewed one). Reports from un-qualified accounts persist (moderator can see them in the queue under a separate "low-confidence reports" list) but do NOT increment the threshold counter. Both the account-age threshold (range 0-90 days) and the verified-buyer requirement (boolean per market) MUST be tunable per market by `super_admin` via spec 015 settings. The audit row on each `ReviewFlag` MUST capture the reporter's `is_qualified` evaluation at report time so the threshold counter is reproducible.
- Q: Customer display name shown on a published review — full name, first+initial, custom handle, or pseudonymous? → A: **First-name + last-name-initial as the default (e.g., "Mohamed K."), with an optional per-customer `review_display_handle` (1-40 chars, validator-checked for profanity via the same wordlist) that overrides the default when set.** This single canonical rule applies to BOTH the storefront review render AND the moderator-queue render, so there is no display-inconsistency between the two surfaces. The field is editable from spec 019 admin-customers via a customer-facing "review display name" setting; changes apply to all of the customer's existing and future reviews from `now()` onward (no re-render of historical data). Profanity-filter trips on a `review_display_handle` follow the same `pending_moderation` flow as a body trip — but applied to the **customer profile**, not the individual reviews. (Implementation note: this requires a small new field on the customer-profile entity owned by spec 019; documented as a soft-coupling assumption.)

---

## Primary outcomes

1. Every verified buyer of a delivered, non-refunded order line can submit one bilingual review per product, see its publication state, edit it within the configured window, and trust that their voice reaches the storefront — or that the filter / queue holds it for editorial review with a transparent reason.
2. The product-detail and search surfaces (specs 005 / 006) consume one canonical rating aggregate per product per market, refreshed within seconds of any review-state change, so customers see consistent star ratings without independent re-aggregation paths.
3. Admin moderators can work a single, filterable, market-aware queue surfacing every review in `pending_moderation` (filter-tripped) or `flagged` (community-reported) state; open a review; hide / delete / reinstate with required reasoning + admin notes; and trust that every decision is captured in the audit log with the actor, timestamp, prior state, and reason.
4. A community-driven reporting flow lets any signed-in customer flag a review with one of five fixed reasons; the system aggregates flags and escalates to the queue at a per-market threshold (default 3 distinct reporters within 30 days), keeping the editorial bar high without weaponising the report button.
5. Refund flow integrity: when an order line is refunded after its review was published, the review is auto-hidden with audit; the rating aggregate is recomputed; the customer can dispute via spec 023.
6. The data model, market configuration, vendor scoping, and admin role boundaries are designed so that future multi-vendor expansion (Phase 2) can layer vendor-scoped review moderation on top without rewriting the review schema or its state machine.

---

## Roles and actors

| Role | Permission | What they can do in 022 |
|---|---|---|
| `customer` (signed-in) | spec 004 | Submit one review per `(customer_id, product_id)` after a delivered + non-refunded order line; edit within the edit window; report another customer's review with one of five fixed reasons. |
| `customer` (unsigned / browsing) | none | Read visible reviews + the product rating aggregate; cannot submit, edit, or report. |
| `reviews.moderator` | new in this spec | Open the moderation queue; transition a review to `hidden` / `deleted` / `visible (reinstate)`; attach admin notes; resolve flag escalations. |
| `reviews.policy_admin` | new in this spec | All `reviews.moderator` powers plus: edit per-market eligibility window, edit-window length, profanity wordlists, and community-report threshold. |
| `super_admin` | spec 015 | Implicit superset of all of the above. |
| `support` (spec 023) | existing | Read-only on a review's state + last decision reason; can open a ticket against a review for further investigation. Cannot decide moderation. |
| `viewer.finance` | spec 015 | Read-only on every review entity for reporting + dispute investigations. |

The customer-facing surface is owned by Phase 1C spec 014 (mobile + web storefront); 022 ships only the backend contracts. The admin moderation queue UI is owned by spec 015 + this spec's contract; 015 builds the screens against 022's endpoints.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — A verified buyer submits a review for a delivered order line, no profanity (Priority: P1)

A customer in KSA-AR who received their order line 14 days ago opens the product detail screen, taps **Write a review**, rates 4 of 5 stars, types a bilingual headline + body in Arabic, and submits. The eligibility query returns `eligible`; the profanity filter does not trip; the review publishes immediately to state `visible`; the product rating aggregate refreshes within seconds; the customer sees their review on the product page.

**Why this priority**: Verified-buyer review submission is the constitutional core of Principle 15 and the most-frequent customer flow in this spec.

**Independent Test**: Sign in as a customer with a delivered, non-refunded order line for product P; submit a review with body that doesn't trip the filter; verify the review row exists in `visible`, the rating aggregate row for P is updated, and the storefront API returns the review on next read.

**Acceptance Scenarios**:

1. *Given* a customer with a delivered, non-refunded order line for P, *when* they submit a review with a 4-star rating + bilingual body, *then* the review row persists in `visible`, the rating aggregate for `(P, market_code)` is recomputed, and the response payload echoes the review id + state.
2. *Given* a customer who has not bought P, *when* they attempt to submit a review for P, *then* the request rejects with `review.eligibility.no_delivered_purchase`.
3. *Given* a customer whose order line for P was refunded, *when* they attempt to submit a review for P, *then* the request rejects with `review.eligibility.refunded`.
4. *Given* a customer whose order line for P was delivered 200 days ago (window default 180 days), *then* the request rejects with `review.eligibility.window_closed`.
5. *Given* a customer who already has a review for P, *when* they attempt to submit a second review, *then* the request rejects with `review.eligibility.already_reviewed` and includes the existing review id.

---

### User Story 2 — A customer's review trips the profanity filter and lands in the moderation queue (Priority: P1)

A different customer submits a review whose body contains a wordlist match. The submission persists in state `pending_moderation`, NOT visible to other customers; the author receives a "your review is being reviewed by our team" confirmation. The review surfaces in the admin moderation queue with a `filter-tripped` badge, the matched terms highlighted. A `reviews.moderator` opens the review, decides on `visible` or `hidden`, attaches an admin note, and clicks **Decide**. The state transitions; the audit log captures the decision; the rating aggregate refreshes accordingly.

**Why this priority**: Filter + queue is the second-most-critical authoring path. Without it, the storefront is exposed to abuse on day 1.

**Independent Test**: Submit a review whose body contains a seeded profanity term; verify state is `pending_moderation`; verify the queue endpoint returns the review with the `filter-tripped` badge; sign in as `reviews.moderator` and approve / hide; verify the audit row.

**Acceptance Scenarios**:

1. *Given* a customer submits a review with body containing a seeded profanity term, *when* the request is processed, *then* the review row is persisted in `pending_moderation` with `filter_trip_terms[]` recorded, the rating aggregate is NOT updated, and the response carries `pending_review` indicator.
2. *Given* a `reviews.moderator` opens the queue, *when* they filter by `state=pending_moderation`, *then* the review appears with the `filter-tripped` badge and the matched terms are surfaced for the moderator's reference.
3. *Given* a `reviews.moderator` decides `visible` with admin note ≥ 10 chars, *then* the state flips, the rating aggregate refreshes, and an audit row captures `actor_id`, `timestamp`, `from_state=pending_moderation`, `to_state=visible`, `admin_note`.
4. *Given* a `reviews.moderator` decides `hidden` with reason ≥ 10 chars, *then* the state flips, the rating aggregate stays unchanged (review was never `visible`), and the customer receives a "your review was not approved" notification with the reason category.
5. *Given* a moderator without `reviews.moderator` permission attempts to decide, *then* the request returns `403 reviews.moderation.forbidden`.

---

### User Story 3 — A customer reports an inappropriate review and it escalates to the queue (Priority: P1)

A customer browsing reviews sees one they consider abusive. They tap **Report**, pick `personal_attack`, optionally add a note, and submit. The review accumulates one `ReviewFlag` row. After two more distinct customers report it within 30 days, the system auto-transitions the review to `flagged` and surfaces it in the admin queue with a `community-reported` badge. A `reviews.moderator` decides `hidden` (community report substantiated) or `visible` (reinstate, false-positive); audit captures both the report timeline and the decision.

**Why this priority**: Reports are the customer-driven safety net. Without it the queue depends entirely on the filter (which only catches lexical matches, not contextual abuse).

**Independent Test**: Sign in as 3 distinct customers; report a `visible` review; verify it auto-transitions to `flagged` after the third report; sign in as `reviews.moderator`; resolve.

**Acceptance Scenarios**:

1. *Given* an unsigned visitor attempts to report, *then* the request returns `401 review.report.unauthenticated`.
2. *Given* a signed-in customer reports a review with reason `other_with_required_note`, *when* the note is < 10 chars, *then* the request rejects with `review.report.note_required`.
3. *Given* a signed-in customer reports the same review twice, *when* they submit the second report, *then* the request returns `409 review.report.already_reported_by_actor` (idempotent — does not double-count).
4. *Given* the review accumulates 3 distinct-customer reports within 30 days (default threshold), *then* the review state auto-transitions to `flagged`, an audit row captures `to_state=flagged` with `triggered_by=community_report_threshold`, and the moderation queue surfaces the review.
5. *Given* a `reviews.moderator` decides `visible` (false-positive), *then* the state flips back to `visible`, the review's `flag_history` retains the prior reports for context, and the rating aggregate refreshes.

---

### User Story 4 — A `reviews.moderator` hides, reinstates, and finally deletes a review (Priority: P2)

A moderator opens a flagged review, decides `hidden` with reason "personal attack on competitor". A week later the affected customer disputes via spec 023. The moderator opens the ticket, re-reads the review, decides `visible` (reinstate). Two weeks later a second escalation arrives via legal: the review constitutes defamation. A `super_admin` decides `deleted`; the review row is preserved (FR-005a-style soft-delete) but is no longer surfaced anywhere. Every transition is audited.

**Why this priority**: The full hide → reinstate → delete lifecycle is a P2 because most reviews never reach `deleted`, but the operational primitives must exist at launch.

**Independent Test**: Walk a single review through `visible → hidden → visible → deleted` via four moderator actions; verify each transition has an audit row with the correct from/to states; verify the rating aggregate updates correctly at each transition.

**Acceptance Scenarios**:

1. *Given* a `reviews.moderator` decides `hidden` on a `visible` review without a reason note, *then* the request rejects with `reviews.moderation.reason_required`.
2. *Given* a hidden review, *when* a `reviews.moderator` decides `visible` (reinstate), *then* the rating aggregate refreshes to include the review's rating.
3. *Given* a hidden review, *when* a `reviews.moderator` (not `super_admin`) decides `deleted`, *then* the request rejects with `reviews.moderation.delete_requires_super_admin`.
4. *Given* a `super_admin` decides `deleted`, *then* the review row remains in the DB but is excluded from every storefront / search / aggregate read; the row is read-only thereafter.
5. *Given* an audit log read for the review, *then* every transition appears with actor + timestamp + from_state + to_state + reason / admin note.

---

### User Story 5 — A refund causes a previously-published review to auto-hide (Priority: P2)

A customer submits a review for product P; the review publishes to `visible`. Two weeks later, the customer initiates a refund for the original order line, which spec 013 approves. The 022 module observes the refund event, auto-transitions the review to `hidden` with reason `auto_hidden:order_refunded`, recomputes the rating aggregate, and notifies the customer. The customer disputes via spec 023; a `reviews.moderator` reads the dispute, decides whether to reinstate; the audit log preserves the auto-hide event and the human decision.

**Why this priority**: Refund integrity is constitutional (Principle 15 — review eligibility tied to completed-purchase). P2 because refunds-after-review are uncommon in absolute volume but the spec MUST handle them correctly when they happen.

**Independent Test**: Publish a review for a delivered order line; trigger a refund event for the same line; verify the review auto-transitions to `hidden`, the rating aggregate excludes it, and an audit row with `actor_id='system'` and `triggered_by=refund_event` is present.

**Acceptance Scenarios**:

1. *Given* a `visible` review for a customer's order line, *when* spec 013 emits a `refund.completed` event for that line, *then* 022 auto-transitions the review to `hidden` within 60 s, writes an audit row with `actor_id='system'`, and refreshes the rating aggregate.
2. *Given* the auto-hidden review, *when* the customer dispute is upheld and a `reviews.moderator` decides `visible` (reinstate), *then* the audit history retains both the auto-hide and the manual reinstate; both events are visible in the admin review-history panel.
3. *Given* the auto-hidden review, *when* the refund is later reversed by spec 013 (rare; manual), *then* the review remains `hidden` until a moderator reinstates manually — the system does NOT auto-reinstate on refund reversal.

---

### User Story 6 — Storefront consumes the rating aggregate (Priority: P1)

The product-detail screen (spec 005 customer storefront, owned by spec 014) calls a 022 read-only endpoint with `(product_id, market_code)` and receives `{ avg_rating, review_count, distribution: {1:..,2:..,3:..,4:..,5:..}, last_updated_utc }`. The aggregate excludes `pending_moderation`, `hidden`, and `deleted` reviews. Search results (spec 006) decorate hits with the same aggregate.

**Why this priority**: Without the aggregate, the storefront cannot display ratings — a customer-trust-critical feature. P1.

**Independent Test**: Seed 50 reviews across all 5 states for product P; call the aggregate endpoint; verify only `visible` and `flagged` reviews count; verify avg_rating + distribution math.

**Acceptance Scenarios**:

1. *Given* a product P with 10 visible 5-star reviews, 5 visible 1-star reviews, 3 pending_moderation, 2 hidden, *when* the aggregate is read, *then* the response shows `review_count=15`, `avg_rating=3.67`, distribution counts only the visible 15.
2. *Given* a `flagged` review, *when* the aggregate is read, *then* the review's rating IS included (flagged means "community reported, awaiting moderator" — still customer-visible by default until decided otherwise).
3. *Given* a moderator transitions a review from `visible` to `hidden`, *when* the next aggregate read happens within 60 s, *then* the aggregate reflects the new state.
4. *Given* a customer reads the aggregate for `(P, EG)` but only `(P, SA)` reviews exist, *then* the response shows `review_count=0` with `avg_rating=null` (NOT zero).

---

### User Story 7 — `reviews-v1` seeder for staging and local development (Priority: P3)

A developer or QA engineer runs the seeder. It creates: 30 visible reviews across 6 products spanning all 5 ratings; 5 pending_moderation reviews (filter-tripped); 4 flagged reviews (community-reported); 3 hidden reviews; 2 deleted reviews. Each review is tied to a synthetic customer + delivered, non-refunded order line. Bilingual editorial-grade AR/EN labels (Principle 4). Profanity wordlists for KSA + EG seeded.

**Why this priority**: Without realistic seed data the moderation surface and aggregate cannot be exercised end-to-end in staging or local. P3 because manual review submission via the customer test client is also possible (just less efficient).

**Independent Test**: `seed --dataset=reviews-v1 --mode=apply` against a fresh staging DB; verify per-state distribution; verify the wordlist rows; verify aggregate consistency.

**Acceptance Scenarios**:

1. *Given* a fresh staging DB, *when* the seeder runs, *then* it produces ≥ 1 row in each of `visible`, `pending_moderation`, `flagged`, `hidden`, `deleted` states.
2. *Given* the seeder runs twice on the same DB, *then* it is idempotent (no duplicate reviews).
3. *Given* the seeder runs with `--mode=dry-run`, *then* it exits 0 with a planned-changes report and writes nothing.
4. *Given* the seeder fails partway, *then* the partial transaction is rolled back.
5. *Given* an admin opens any seeded review, *then* AR + EN bodies render correctly with no machine-translated artifacts.

---

### Edge Cases

- A customer submits a review while their order line is in `delivered` state, then the line is refunded **before the rating aggregate next refreshes**: 022's refund-event subscriber transitions the review to `hidden`; the aggregate recomputes; net effect is the review never appears in the aggregate (correct outcome).
- A customer edits their `visible` review and the edit trips the profanity filter: the review transitions to `pending_moderation`; the aggregate excludes it until a moderator decides; the audit row captures the edit-induced state change distinct from a fresh-submission state change.
- Two `reviews.moderator`s decide the same review concurrently: optimistic-concurrency (xmin row_version) — the loser sees `409 reviews.moderation.version_conflict`. Decision idempotency: the second moderator may retry against the new state.
- A customer reports their own review: `400 review.report.cannot_report_own_review`.
- A profanity-wordlist entry contains an unintended substring (false positive): `super_admin` removes the term via the policy admin endpoint; existing `pending_moderation` reviews caused by that term remain in the queue (manual review still required) — the wordlist edit does NOT auto-resolve historical trips.
- A customer's account is locked / deleted in spec 004: every review they authored is auto-transitioned to `hidden` with reason `auto_hidden:author_account_locked` (similar to refund-auto-hide) and the rating aggregate refreshes.
- A product is archived in spec 005: existing reviews remain in the operational store (FR-005a-style preservation); the rating aggregate keeps computing for historical / support traceability; the storefront's product-detail surface is owned by spec 005 and decides whether to render the archived product at all.
- A customer's review is in `pending_moderation` for > 7 days (SLA breach): the queue surfaces a "stale" badge; no auto-decide; ops escalation is operator-driven.
- A multi-market customer (rare; account migrates from EG to SA): existing reviews remain attached to the original market; new submissions are scoped to the new market.
- The community-report threshold is lowered from 3 to 1 mid-day: existing visible reviews with 1 or 2 prior reports do NOT retroactively transition to `flagged`; only new reports applied after the threshold change count toward escalation.
- A `super_admin` issues a hard-delete via the API: rejected with `405 review.row.delete_forbidden` (FR-005a). Soft-delete (state `deleted`) is the only deletion path.

---

## Requirements *(mandatory)*

### Functional Requirements

#### Lifecycle and state model (Principle 24)

- **FR-001**: Reviews MUST share a five-state lifecycle: `pending_moderation` (initial when filter trips) | `visible` (initial when filter clean, also reinstate target) → `flagged` (community-reported escalation; still customer-visible) → `hidden` (admin or system action; not customer-visible; reversible) → `deleted` (terminal; preserved row, fully suppressed).
- **FR-002**: Every state transition MUST write an audit row with `actor_id` (or `'system'`), `actor_role`, `timestamp_utc`, `from_state`, `to_state`, `reason_note?`, `admin_note?`, and `triggered_by` (one of `customer_submission`, `customer_edit`, `community_report_threshold`, `refund_event`, `account_locked`, `moderator_action`, `manual_super_admin`) (Principle 25).
- **FR-003**: Decisions to `hidden` or `deleted` MUST require a reason note ≥ 10 characters; decisions to `visible` (reinstate) MUST require an admin note ≥ 10 characters; system-triggered transitions MUST carry the structured `triggered_by` value and MAY have a synthetic note like `auto_hidden:order_refunded`.
- **FR-004**: Only `super_admin` MAY decide `deleted`; `reviews.moderator` MAY decide `hidden` or `visible`. The API MUST enforce this at the handler layer (`403 reviews.moderation.delete_requires_super_admin` for moderator-attempted deletes).
- **FR-005**: A `deleted` review MUST be terminal and read-only; reinstate from `deleted` is forbidden via the API (`400 reviews.moderation.delete_terminal`); historical operations (audit reads, support-investigation reads) remain possible.
- **FR-005a**: Reviews MUST NEVER be hard-deleted from the operational store (FR-005a-style preservation). `deleted` is a soft state; the row is retained indefinitely so audit, dispute, and refund-trace queries remain resolvable. Any `DELETE` API call against a review MUST return `405 review.row.delete_forbidden`.

#### Customer review submission

- **FR-006**: Review submission MUST capture: `product_id`, `order_line_id` (proof-of-purchase), `rating` (integer 1-5), `headline` (max 100 chars; required; stored under the customer's authoring `locale`), `body` (max 4000 chars; required; stored under the same locale), `media_urls?` (max 4 images, signed-URL inputs from spec 015 storage), `market_code` (auto-assigned from customer's market-of-record), `locale` (`ar` or `en` — the single locale the customer authored in). The unauthored locale stays `null` at the data layer; the system MUST NOT auto-machine-translate to fill the other locale (Principle 4). The storefront renders the available locale and prepends an annotation `"Original written in {locale}"` when the viewer's locale differs from `locale`.
- **FR-007**: Eligibility MUST require: a `delivered` order line owned by the submitting customer, the line NOT in any refund state per spec 013, and `(now − delivered_at) ≤ window_days` where `window_days` is read from the per-market `reviews_market_schemas` row (default 180; range 30-730).
- **FR-008**: Uniqueness MUST be enforced as **one non-deleted review per `(customer_id, product_id)`** via a unique partial index `(customer_id, product_id) WHERE state != 'deleted'`. Subsequent purchases of the same product cannot create a second concurrent review by the same customer. A customer whose prior review was soft-deleted by `super_admin` (state `deleted`) MAY submit a fresh review, subject to all other eligibility rules — the deleted row remains intact for audit traceability and is excluded from the uniqueness check.
- **FR-009**: Customers MUST be able to edit their own review for `edit_window_days` from `created_at` (default 30; range 7-90, per-market). Edits MUST be allowed in any non-`deleted` state, including `pending_moderation`. An edit that trips the profanity filter (or that adds/removes/replaces media per FR-014a) MUST transition the review to `pending_moderation` regardless of prior state and emit an audit row with `triggered_by=customer_edit`. When the review is already in `pending_moderation`, an edit MUST re-stamp `pending_moderation_started_at` to `now()` so the SLA window restarts; the moderator-side `row_version` (xmin) advances so any in-progress moderator decision against the prior version returns `409 reviews.moderation.version_conflict` per FR-019. The audit log MUST preserve every edit version with full `before / after` snapshots. The queue MUST surface an `edited-since-last-surface` indicator on items that have been edited since the requesting moderator's most recent open. Customer-side edit volume is bounded by FR-040 (5 edits / hour / customer).
- **FR-010**: After the edit window closes, customer-side mutation endpoints MUST return `400 review.edit.window_closed`. Admin actions remain available.

#### Profanity / abuse filter

- **FR-011**: The profanity / abuse filter MUST be applied at submission AND at every customer edit. The filter MUST source its wordlist from a per-market `reviews_filter_wordlists` table (one row per `(market_code, term)`); `super_admin` may add / remove terms via the policy-admin API; changes are audited.
- **FR-012**: Filter trips MUST persist the matched terms on the review row in a `filter_trip_terms[]` field for moderator visibility; the field is admin-only and MUST NOT leak to the customer-facing API.
- **FR-013**: The filter MUST support both AR and EN wordlists; matches are case-insensitive and Arabic-normalized (consistent with spec 006 search Arabic-normalization rules).
- **FR-014**: A filter trip MUST NOT silently reject the submission; the row persists in `pending_moderation` and the customer receives a `pending_review` confirmation indicator. The customer's review draft is preserved.
- **FR-014a**: A submission with `media_urls.length > 0` MUST persist in `pending_moderation` regardless of the text-filter result, with `media_attachment_review_required=true` recorded on the review row. The customer receives the same `pending_review` confirmation indicator as a filter-trip submission. The moderation queue MUST surface this hold reason as a distinct badge (`media-pending`) so moderators can filter the queue on `state=pending_moderation AND media_attachment_review_required=true`. An edit that adds, removes, or replaces media MUST re-trigger this hold (transition to `pending_moderation` regardless of prior state) with `triggered_by=customer_edit` and `media_attachment_review_required=true`.

#### Admin moderation queue

- **FR-015**: The admin moderation queue MUST surface every review in `pending_moderation` or `flagged` state, with filters by `market_code`, `state`, `triggered_by`, `community_report_count`, `created_at_range`. Default page size 50, max 200.
- **FR-016**: A queue item MUST display: review headline + body (truncated), authoring `locale`, the canonical reviewer-display rule from FR-016a (first-name + last-initial OR `review_display_handle`), product name, order line `delivered_at`, all flag reports (reasons + reporter display rendered with the same FR-016a rule + `is_qualified` flag), `filter_trip_terms[]` (if any), `media_urls[]` rendered inline (if any) with the `media-pending` indicator (FR-014a), the full audit history, and any prior admin notes.
- **FR-016a**: The canonical reviewer-display rule applied to BOTH storefront and admin-queue surfaces is: (a) if the customer profile carries a non-empty `review_display_handle` (owned by spec 019 admin-customers; 1-40 chars; profanity-filtered via the same wordlist as FR-011), render the handle; (b) otherwise render the customer's `first_name` + the first character of `last_name` + `.` (example: `"Mohamed K."`). The rule is computed at read time (NOT denormalized onto the review row) so a customer's profile name change applies to all their reviews from `now()` onward. A profanity trip on `review_display_handle` MUST flag the customer profile (not the individual reviews) for moderation; until resolved, the storefront falls back to the default first-name + last-initial render.
- **FR-017**: A `reviews.moderator` MUST be able to decide `visible` / `hidden`; a `super_admin` MUST be able to decide `deleted`. Each decision MUST capture a reason note (`hidden` / `deleted`) or admin note (`visible`) ≥ 10 chars.
- **FR-018**: An admin note MUST be append-only; an admin may add multiple notes over a review's lifetime; each note carries the actor + timestamp; previous notes remain visible (no edits, no deletions) for accountability.
- **FR-019**: Decisions MUST be optimistic-concurrency-guarded via row_version (xmin). Concurrent decisions return `409 reviews.moderation.version_conflict` with the current row body for merge.

#### Community report flow

- **FR-020**: A signed-in customer MUST be able to report a review with one of five fixed reasons: `inappropriate_language`, `spam_or_irrelevant`, `personal_attack`, `false_or_misleading`, `other_with_required_note` (the last requires a `note ≥ 10 chars`).
- **FR-021**: A customer MUST NOT be able to report their own review (`400 review.report.cannot_report_own_review`).
- **FR-022**: A customer MUST NOT be able to report the same review twice; a duplicate report returns `409 review.report.already_reported_by_actor` and does not double-count.
- **FR-023**: When a review accumulates **`community_report_threshold`** distinct **qualified** customer reports within a rolling **30-day** window, the system MUST auto-transition the review from `visible` to `flagged` and surface it in the queue with a `community-reported` badge. The threshold defaults to **3** per market and is tunable per market by `super_admin` (range 1-10). A report is `qualified` if and only if the reporter satisfies both `report_qualifying_account_age_days` (default 14; range 0-90, per-market) and `report_qualifying_requires_verified_buyer` (default `true`, per-market boolean). Un-qualified reports are still persisted as `ReviewFlag` rows for moderator visibility (under a "low-confidence" filter on the queue) but do NOT increment the threshold counter. The reporter's `is_qualified` evaluation at report time MUST be persisted on the `ReviewFlag` row so the threshold count is reproducible during audit.
- **FR-024**: A `flagged` review MUST remain customer-visible until a moderator decides otherwise. (`flagged` is "needs review" — not "hidden by community".)

#### Rating aggregate

- **FR-025**: A `product_rating_aggregates` table keyed `(product_id, market_code)` MUST hold `avg_rating`, `review_count`, `distribution` (5-bucket counts), and `last_updated_utc`. The aggregate is the single canonical read source for storefront + search.
- **FR-026**: The aggregate MUST be recomputed within **60 seconds** of any review state transition that affects countability (`*` → `visible`, `visible` → `*`, edits to `rating` of a visible review). A `RatingAggregateRefresher` worker handles batched refreshes; immediate refresh on transition is also allowed for urgency.
- **FR-027**: The aggregate MUST count only `visible` and `flagged` reviews. `pending_moderation`, `hidden`, and `deleted` MUST be excluded.
- **FR-028**: A read for `(product_id, market_code)` with no eligible reviews MUST return `review_count=0` and `avg_rating=null` (NOT zero — distinguishes "no reviews yet" from "average is zero").
- **FR-029**: The aggregate MUST be readable WITHOUT authentication (consistent with Principle 3 — browse remains unauthenticated). Response is cacheable for 60 s by an upstream HTTP cache.

#### Refund + account-lifecycle integrations

- **FR-030**: 022 MUST subscribe to spec 013's `refund.completed` event; on receipt, every `visible` or `flagged` review whose underlying order line was refunded MUST auto-transition to `hidden` with `triggered_by=refund_event` and `reason_note='auto_hidden:order_refunded'` within 60 s. The aggregate refreshes accordingly.
- **FR-031**: 022 MUST subscribe to spec 004's `customer.account_locked` and `customer.account_deleted` events; on receipt, every review authored by that customer MUST auto-transition to `hidden` with `triggered_by=account_locked` and `reason_note='auto_hidden:author_account_locked'`.
- **FR-032**: A reversed refund (rare) MUST NOT auto-reinstate the review; admins are notified via the moderation queue's "needs review" filter and may manually reinstate.

#### Audit (Principle 25)

- **FR-033**: Every state transition (customer-submission, customer-edit, moderator-decision, system-auto-transition), every report submission, every wordlist edit, every threshold edit, every admin-note addition MUST emit an audit row to the shared audit log via the existing `IAuditEventPublisher`.
- **FR-034**: Audit rows MUST be immutable and MUST NOT be deletable from any UI; the underlying audit-log table is owned by spec 003 and append-only.

#### Bilingual + RTL (Principle 4)

- **FR-035**: Customer-supplied review headline + body are stored in a single `locale` per FR-006 (no bilingual authoring requirement at submission). Customer-facing **system-generated** strings (reason codes, queue messages, notification copy, "Original written in {locale}" annotation) MUST resolve to ICU keys in both `en` and `ar` and be rendered in the viewer's locale.
- **FR-036**: The admin moderation queue MUST switch to RTL when the operator's locale is `ar`.

#### Notifications integration (Principle 19)

- **FR-037**: 022 MUST emit domain events (`review.submitted`, `review.published`, `review.held_for_moderation`, `review.flagged`, `review.hidden`, `review.deleted`, `review.reinstated`, `review.auto_hidden`) consumed by spec 025 for: customer notification on hold/hide/reinstate, admin moderation digest emails, weekly community-reports digest.
- **FR-038**: This spec MUST NOT directly send notifications; it only emits events.

#### Multi-vendor readiness (Principle 6)

- **FR-039**: Every review row and rating-aggregate row MUST carry a `vendor_id` column (nullable in V1; populated by single-vendor seed). Indexed for future-vendor-scoped reads. The admin UI MUST NOT expose vendor scoping in V1. Profanity wordlists are platform-level (per-market only) and do NOT carry `vendor_id`; if Phase 2 multi-vendor introduces vendor-scoped wordlists, that is a forward-compatible additive change.

#### Operational safeguards

- **FR-040**: Customer review submission MUST be rate-limited per `customer_id`: 5 review submissions / hour, 20 / day (overridable per environment). Over-limit returns `429 review.rate_limit.submission_exceeded`. Edits and reports follow the same envelope (5 / hour / actor).
- **FR-041**: Admin moderation actions MUST be rate-limited per `actor_id`: 60 decisions / hour to defeat scripted abuse; over-limit returns `429 reviews.moderation.rate_limit_exceeded`.
- **FR-042**: All admin endpoints MUST require authentication and the corresponding RBAC permission; lookups MUST cap result-set size at 200 with paging.

### Key Entities

- **Review** — verified-buyer authored content tied to one `OrderLine`. Lifecycle, rating, bilingual headline + body, optional media, market scope. Persisted in a new `reviews` schema.
- **ReviewModerationDecision** — append-only record per state-transition decision: actor, timestamp, from/to, reason / admin note, triggered_by.
- **ReviewAdminNote** — append-only operator-side notes attached to a review (separate from moderation decisions; an admin may add notes without making a decision).
- **ReviewFlag** — customer-submitted report of a review: reporter, reason, optional note, created_at.
- **ProductRatingAggregate** — denormalized read-side per `(product_id, market_code)`.
- **ReviewsFilterWordlist** — per-market `(market_code, term)` rows for the profanity / abuse filter.
- **ReviewsMarketSchema** — per-market policy: `eligibility_window_days`, `edit_window_days`, `community_report_threshold`, `community_report_window_days`.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A verified buyer with a delivered, non-refunded order line MUST be able to author and submit a review (rating + bilingual headline + body) in under 2 minutes from product-detail tap to confirmation.
- **SC-002**: A submitted review that does NOT trip the profanity filter MUST become customer-visible (state `visible` AND counted in the rating aggregate) within 60 seconds of submission, p95.
- **SC-003**: 100 % of state transitions, report submissions, wordlist edits, threshold edits, and admin-note additions MUST produce a matching audit row, verified by the audit-coverage script in spec 015.
- **SC-004**: 0 % of `visible` or `flagged` reviews MAY exist for an order line currently in any refund state, verified by an integrity-scan job that runs daily.
- **SC-005**: The product rating aggregate MUST refresh within 60 s of any countable review state transition, measured over a 7-day soak.
- **SC-006**: The admin moderation queue MUST surface a `pending_moderation` review within 60 s of submission, p95.
- **SC-007**: AR-locale screen-render correctness (RTL, label completeness, formatting) MUST score 100 % against a representative 30-screen editorial-review checklist (Principle 4).
- **SC-008**: The `reviews-v1` seeder MUST populate ≥ 1 row in each of `visible`, `pending_moderation`, `flagged`, `hidden`, `deleted` states, plus seeded wordlists for both KSA + EG, in under 10 s on a fresh staging DB.
- **SC-009**: A duplicate-report attempt by the same customer against the same review MUST be rejected with 100 % accuracy under a 100-concurrent-attempt stress test (FR-022).
- **SC-010**: A profanity-tripped submission MUST persist with `state=pending_moderation` (NOT visible) in 100 % of test cases, verified by a wordlist-coverage matrix test suite.

---

## Assumptions

- The 011 `orders` module exposes a deterministic eligibility query `IOrderLineDeliveryEligibilityQuery.IsEligibleForReview(customer_id, product_id) → {eligible, reason_code, delivered_at?}` that 022 consumes; if the query is not yet on `main`, 022 ships against a documented contract stub and integration tests assert against a fake. (Cross-module hook declared in `Modules/Shared/`.)
- The 013 `returns-and-refunds` module emits `refund.completed` and `refund.reversed` events on its in-process bus; 022 subscribes via the same MediatR notification channel used by specs 020 / 021 / 007-b.
- The 004 `identity-and-access` module emits `customer.account_locked` and `customer.account_deleted` events on its lifecycle channel; 022 subscribes via the existing `ICustomerAccountLifecycleSubscriber` interface from spec 020.
- The 015 `admin-foundation` shell (RBAC, audit panel, idempotency middleware, rate-limit middleware) is at DoD on `main` before 022 implementation begins.
- The 005 `catalog` module exposes a product-existence check + name lookup endpoint; 022's queue consumer renders the product name from this read-only endpoint (no FK at DB level — same multi-module pattern as specs 020 / 021).
- Storage for review media uses the existing spec 015 storage abstraction (signed-URL upload + retrieval); 022 stores only the `media_urls[]` array on the review row.
- The customer-profile schema (owned by spec 019 admin-customers) MUST carry an optional `review_display_handle` field (1-40 chars, profanity-filtered) per FR-016a. 022 reads the customer's `first_name`, `last_name`, and `review_display_handle?` at storefront / queue render time and applies the FR-016a canonical rule. If spec 019 has not yet shipped the field, 022 ships against a contract stub and falls back to first-name + last-initial only until 019 lands.
- AR Arabic-normalization for the profanity filter reuses spec 006 search's normalization rules; 022 does not reimplement.
- Single-vendor at V1 (Principle 6); `vendor_id` columns are present and indexed but not exposed in admin UI.
- Operators sign in through the spec 015 admin shell; this spec does not introduce a new auth path.
- Currency / market resolution per market is fixed (EG → EGP, KSA → SAR); reviews carry `market_code` only.
- The eligibility window default of 180 days, edit window default of 30 days, and community-report threshold default of 3 / 30 days are conservative values picked for V1 launch; all are tunable per market by `super_admin` post-launch.

---

## Out of Scope

- **Customer Q&A / questions on product detail** — separate concept from reviews; deferred to Phase 1.5.
- **Helpful / unhelpful voting on reviews** — deferred to Phase 1.5.
- **Verified-buyer badge on reviews** — implicit (only verified buyers can submit per FR-007); explicit visual badge is a spec 014 storefront concern.
- **Review-response by sellers / vendors** — Phase 2 multi-vendor concern.
- **AI-driven review summarization** — Phase 2.
- **Aggregate sentiment analysis** — Phase 2.
- **Review-import from migration sources** — out of scope; V1 launches with no historical reviews.
- **Cross-product review portability** (e.g., "this customer's other reviews") — Phase 1.5.
- **Photo-only review submissions** (no text body) — V1 requires a text body in both locales (FR-006); photo-only reviews are deferred.
- **Threshold for auto-hide based on community reports alone** (i.e., no moderator review) — explicitly rejected; community reports always escalate to a queue, never auto-hide.
- **Reviewer reputation / trust score** — Phase 2.
- **External-platform review syndication** (Trustpilot, Google) — out of scope.

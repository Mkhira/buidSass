# Feature Specification: 025 — Notifications

**Feature Branch**: `phase-1E`
**Spec ID**: 025
**Created**: 2026-05-10
**Status**: Draft
**Phase**: 1E — Integrations · Milestone 8
**Input**: Implementation-plan §Phase 1E spec 025 (lines 586–599) — "template mgmt; event-triggered SMS + email + push (no WhatsApp); campaign basics; preference mgmt. ADR-009 Accepted."

---

## Clarifications

### Session 2026-05-10

Five priority questions resolved. Sources: `default` = orchestrator-applied recommended default per the agreed workflow; `deferred-default` = within the 5-question cap (none here — all five questions were answered).

- Q: EG SMS provider at v1 → A: **Vodafone Egypt SMS API** (national-tier deliverability, regulator-compliant headers). Source: `default`. Rationale: Vodafone Egypt commands the highest market share + best regulator alignment for Arabic SMS; the alternative (Infobip) remains the documented backup. ADR-009 v1 stack is therefore: SES (email) + Unifonic (KSA SMS) + Vodafone Egypt (EG SMS) + FCM (push).
- Q: Per-market backup providers at v1 → A: **Email backup = SendGrid (multi-market). SMS backup KSA = Infobip. SMS backup EG = Infobip. Push backup = none at v1** (FCM is the de-facto Android+iOS standard; backup deferred to 1.5). Source: `default`. Rationale: SendGrid has KSA-friendly ToS; Infobip is multi-market with KSA + EG presence and is a common Unifonic alternative. Configurable via `provider_routing` table; not auto-engaged at launch.
- Q: Quiet-hours policy at v1 → A: **22:00–08:00 local time (KSA local for `sa`, EG local for `eg`) for marketing only; transactional bypasses.** Source: `default`. Rationale: matches the spec's existing assumption and Principle 19 send-window-compliance requirement; documented per market in `notifications.market_schemas`.
- Q: Auto-failover default state at v1 → A: **`auto_failover_enabled=false` (manual failover only)** at v1, per market × channel. Source: `default`. Rationale: provider behavior is not yet observed in production; auto-failover risks oscillation and double-spend. Operator can flip to `true` once burn-in data exists. Audit event `provider.failover` fires on either path.
- Q: Dead-letter retention at v1 → A: **30 days** retention for unresolved dead-letter rows; auto-archive after 30 days (state retained for query, but row is moved to a cold table); audit-log retains the `notification.dead_letter` event for 365+ days. Source: `default`. Rationale: 30 days is sufficient for operator review; longer retention bloats the operator review queue.

ADR-009 transition recorded here: `Proposed (narrowed)` → **`Accepted`**. The fingerprint script will pick up the change; no separate ADR amendment PR is required because the implementation plan and Stage-7 process pre-authorized this acceptance under Milestone 8.

---

## ADR & Constitution Traceability

| Source | Title | How 025 satisfies it |
|---|---|---|
| Principle 4 | Bilingual + RTL editorial | All templates have AR + EN variants; AR is editorial-grade, never machine-translated; PDFs and notifications respect RTL. |
| Principle 5 | Market config (EG + KSA) | Per-market provider selection, per-market send-window compliance, per-market unsubscribe language. |
| Principle 19 | Notifications | Push, email, SMS supported. WhatsApp explicitly deferred to Phase 1.5-f. Coverage: OTP, order updates, offers, abandoned cart, restock, price drop, verification, refunds, shipping. Templates, localization, event-triggered sends, channel preferences, delivery logging — all required. Admin-managed campaigns required. |
| Principle 24 | State machines | Three explicit machines: Notification (`pending → queued → sending → delivered ∪ failed → retrying → dead_letter`), Template lifecycle (`draft → in_review → published ↔ archived`), Campaign (`draft → scheduled → sending → completed ∪ paused → cancelled`). |
| Principle 25 | Audit | Template publish, campaign send, preference change, opt-out, dead-letter, provider failover all audit-logged. |
| Principle 28 | AI-build standard | Implementation-ready: provider matrix, template schema, event-subscriber list, audit events all enumerated. |
| Principle 29 | Required spec output | All twelve sections present below. |
| ADR-009 | Notification & OTP providers | **Flipped from Proposed to Accepted in this spec**: SES (email), Unifonic (SMS, KSA-resident infra), FCM (push). Stack confirmed via clarify. |
| ADR-010 | Cloud + residency | All notification metadata (templates, delivery logs, preferences) persists in KSA-Central Postgres. Provider egress is explicitly tolerated (carriers and email gateways operate globally) but PII payloads are minimized at egress (see FR-029). |
| Spec 003 | shared-foundations | `audit_log_entries`, `AddLayeredConfiguration()` for KV-sourced provider credentials. |
| Spec 004 | identity-and-access | OTP delivery channel; user identity for preference scoping. Hard dependency. |
| Spec 011 | order | Order-update event subscribers consume order state-machine transitions. Hard dependency. |
| Spec E1 | infrastructure-integration | Provides `notifications-{email,sms,push}/<market>/<provider>/<key>` Key Vault slots. **Hard prerequisite — E1 at exit.** |

025 does **not** modify the constitution or ADR table. It records ADR-009's flip from `Proposed (narrowed)` → `Accepted` with the chosen provider stack.

---

## Goal

Deliver a centralized, multi-channel, multi-market, multilingual notification platform that:

1. Sends transactional events (OTP, order, verification, refunds, shipping, restock, price-drop, abandoned-cart) over SMS, email, and push reliably with retries and dead-letter.
2. Lets admins author and publish AR + EN templates and run targeted campaigns.
3. Lets customers manage channel preferences (basic opt-out at v1; full preference center deferred to 1.5-e).
4. Enforces per-market send-window compliance, unsubscribe language, and rate limits.
5. Provides full delivery-event audit and provider-failover behavior.

025 is **backend-heavy with admin-UI surfaces** for template authoring, campaign management, and delivery-log viewing. Customer-facing UI scope is limited to the basic opt-out screens (full UI is 1.5-e).

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Customer receives an order-confirmation notification across all preferred channels (Priority: P1)

A customer places an order. The order-placed event triggers the notification module. Templates are resolved per the customer's market (`sa` or `eg`), language preference (`ar` or `en`), and channel preferences. The customer receives the order-confirmation via email and push (default-on at signup). Each delivery is logged with provider message-id, send timestamp, and final status.

**Why this priority**: Order confirmation is the highest-volume transactional notification and the most user-facing trust signal at launch. Without it, customers cannot confirm purchases and support volume spikes.

**Independent Test**: Place an order on Staging with a customer whose preferences are email + push enabled, AR locale, KSA market. Confirm email lands within 60s with editorial AR copy + RTL layout, push lands within 30s, both delivery rows appear in `notifications.deliveries` with provider-id and `delivered` status.

**Acceptance Scenarios**:

1. **Given** an `order.placed` event is published with `customer_id` + `order_id` + `market_code=sa`, **When** the notification module subscribes and resolves the recipient, **Then** the system creates one `Notification` per (channel × recipient) for each of the customer's enabled channels (email + push by default), in `pending` state.
2. **Given** a `Notification` is in `pending`, **When** the worker picks it up, **Then** the state transitions to `queued → sending → delivered` (each transition emits a domain event); the AR template is rendered with editorial-grade copy (no machine translation); RTL is preserved in HTML email.
3. **Given** a customer's channel preferences specify `sms=disabled`, **When** an order-update event fires, **Then** no SMS is created; the audit log records `notification.skipped` with reason `channel_disabled_by_customer`.
4. **Given** a delivery succeeds, **When** the provider webhook returns `delivered`, **Then** the `Notification` row's `delivered_at` timestamp is set and the `notification.delivered` audit event fires.

---

### User Story 2 — Customer receives a one-time password (OTP) within strict latency budget (Priority: P1)

A customer attempts to log in or verify an action requiring OTP (per spec 004). The notification module sends the OTP code over SMS (KSA market: Unifonic) or email (fallback) within the latency budget required by spec 004's auth flow (default: 30 seconds end-to-end from event publish to user device).

**Why this priority**: OTP is the gating dependency for spec 004 identity flows. Failure or latency on OTP cascades to login failure across the entire platform.

**Independent Test**: Trigger an OTP-required login from a Staging account. Confirm SMS arrives within 30 s of the auth-request timestamp; email fallback path also arrives within 30 s when SMS is suppressed.

**Acceptance Scenarios**:

1. **Given** an `auth.otp_requested` event with `phone_e164` and `market_code=sa`, **When** the OTP-priority worker handles it, **Then** the SMS provider for the market (Unifonic for `sa`) is invoked and the message-id is recorded; the worker uses a dedicated high-priority queue (separate from broadcast/campaign queues) so non-OTP volume never delays OTP.
2. **Given** the SMS provider returns a transient error, **When** the retry logic engages (max 3 attempts, exponential backoff: 1s/3s/9s capped at OTP TTL), **Then** the OTP either succeeds within TTL or transitions to `failed` and triggers the email fallback path.
3. **Given** the OTP TTL has elapsed (default 5 min per spec 004), **When** the customer attempts to use the code, **Then** spec 004 rejects it; the notification is not retried beyond TTL.
4. **Given** OTP delivery exceeds 30s p95 budget over a 5-minute window, **When** the SLO monitor evaluates, **Then** an alert fires with the route partition (provider id, market) for diagnosis.

---

### User Story 3 — Admin authors and publishes a bilingual notification template (Priority: P1)

An admin opens the template editor in the admin web app, drafts a new "shipping-update" template with AR + EN variants and three placeholders (`{order_number}`, `{tracking_url}`, `{estimated_delivery}`), submits for review, and publishes after a peer review. The template is now active for the corresponding event subscriber.

**Why this priority**: Without templates, the runtime has nothing to render. Template management is foundational for all transactional and campaign flows.

**Independent Test**: As an admin user with `template-author` role, draft a template; as a second admin with `template-reviewer` role, approve and publish; trigger the event; confirm rendered output uses the new template content.

**Acceptance Scenarios**:

1. **Given** an admin opens the template editor, **When** they create a new `shipping_update` template with AR + EN bodies and a list of placeholders, **Then** the template is saved in `draft` state with both locales required.
2. **Given** a `draft` template, **When** the author submits for review, **Then** the state transitions to `in_review` and a `template.submitted_for_review` audit event fires.
3. **Given** an `in_review` template, **When** a reviewer with `template-reviewer` role approves, **Then** the state transitions to `published`; previous published version is automatically archived; subsequent renders use the new version.
4. **Given** a `published` template misses one of the locales (AR or EN), **When** the publish gate runs, **Then** the publish is rejected with a clear error per Principle 4.
5. **Given** a template is `archived`, **When** an old `Notification` references the archived version, **Then** rendering uses the version that was published at the notification's creation time (snapshot reference, not current).

---

### User Story 4 — Admin runs a targeted campaign broadcast (Priority: P2)

An admin authors a "Ramadan launch" campaign targeting customers in the KSA market with last-purchase ≤ 30 days, schedules it to send at 17:00 KSA on the next day, and monitors delivery progress. The campaign sends through the email channel only, respects per-customer rate limits, and pauses if the per-hour cap is reached.

**Why this priority**: Campaign basics are required by Principle 19 for v1; the full campaign system (segments + advanced targeting) is in scope post-launch but the MVP slice belongs here.

**Independent Test**: Create a campaign targeting a 100-customer test segment; schedule to send in 5 minutes; confirm 100 deliveries are queued and sent within the campaign window; confirm rate-limited customers are deferred and re-tried in the next window.

**Acceptance Scenarios**:

1. **Given** an admin authors a campaign with a target segment (criteria: market, last-purchase, locale, opt-in status), a template, a channel, and a `send_at` timestamp, **When** they save, **Then** the campaign is in `draft` state.
2. **Given** a `draft` campaign, **When** the admin schedules it, **Then** the campaign transitions to `scheduled` with `send_at`.
3. **Given** `send_at` arrives, **When** the scheduler runs, **Then** the campaign transitions to `sending`, the targeted recipient list is materialized at send-time (snapshot), and one `Notification` per recipient is enqueued.
4. **Given** an admin pauses a `sending` campaign, **When** the pause command runs, **Then** new `Notification`s stop being enqueued, in-flight ones complete naturally, and the campaign transitions to `paused` (resumable).
5. **Given** a recipient is opted-out for the channel, **When** the campaign processor evaluates, **Then** that recipient is skipped with `notification.skipped` reason `channel_disabled_by_customer`; no provider call is made.
6. **Given** the campaign completes, **When** the admin opens the campaign report, **Then** counts are visible: total targeted, sent, delivered, bounced, opt-out-skipped, rate-limited.

---

### User Story 5 — Customer opts out of marketing email (Priority: P2)

A customer clicks the unsubscribe link in a marketing email; the link is signed and routes to a confirmation page; the system records the opt-out for the email channel; subsequent campaigns skip this customer.

**Why this priority**: Opt-out is required by per-market compliance (KSA SAMA + Egypt's anti-spam guidelines). Without it, marketing is non-compliant from day one.

**Independent Test**: Send a campaign email to a test customer; click the unsubscribe link; verify a second campaign send to the same customer skips them with the documented audit reason; verify transactional notifications (OTP, order updates) still send.

**Acceptance Scenarios**:

1. **Given** a marketing email sent to a customer, **When** the customer clicks the signed unsubscribe link, **Then** they land on a localized confirmation page; on confirm, the email-marketing channel is opted out for that customer; an audit event `preference.opt_out` fires.
2. **Given** a customer is opted out of email-marketing, **When** a campaign attempts to enqueue an email-marketing notification, **Then** the notification is skipped with the reason recorded.
3. **Given** a customer is opted out of email-marketing, **When** a transactional event fires (order-placed, OTP, shipping-update, refund), **Then** the email IS sent — opt-out applies to marketing only, not transactional.
4. **Given** an unsubscribe link is tampered with (signature invalid), **When** the customer clicks it, **Then** they see an error page and the opt-out is NOT recorded; an audit event `preference.opt_out_signature_invalid` fires.

---

### User Story 6 — Operator handles a provider outage with failover (Priority: P2)

The primary SMS provider (Unifonic for KSA) returns 5xx errors for an extended window. The system retries with exponential backoff, then escalates to the dead-letter queue after the retry budget is exhausted. An operator reviews the dead-letter queue and decides to either retry against a backup provider (failover) or accept the loss.

**Why this priority**: Provider outages are inevitable. Without an explicit failover path and dead-letter visibility, the platform silently drops notifications during incidents.

**Independent Test**: Inject a synthetic 5xx response from the SMS provider stub for 5 minutes; confirm three retry attempts per notification; confirm dead-letter queue accumulates the failures; trigger the operator's "retry-from-dead-letter" action; confirm the notification re-enters the active queue.

**Acceptance Scenarios**:

1. **Given** the SMS provider returns 5xx on a `Notification` send attempt, **When** the worker handles the failure, **Then** the state transitions to `retrying` with `retry_count=1` and a backoff delay (1s/3s/9s/27s capped at 5 min).
2. **Given** a `Notification` reaches `retry_count=3` without success, **When** the worker handles the next failure, **Then** the state transitions to `dead_letter` with the last provider error captured; an audit event `notification.dead_letter` fires; an alert fires if the dead-letter rate exceeds 1% over 10 minutes.
3. **Given** an operator inspects the dead-letter queue in the admin web, **When** they choose "retry now", **Then** the `Notification` re-enters `pending` with `retry_count` reset to 0.
4. **Given** an admin enables a per-market backup SMS provider via the admin UI, **When** the primary provider fails for > 50% of attempts in a 5-minute window, **Then** the worker auto-routes to the backup; an audit event `provider.failover` fires; new `Notification`s use the backup until manual revert.

---

### User Story 7 — Auditor reviews 90-day delivery history with provider attribution (Priority: P3)

An auditor (compliance team) requests the last 90 days of notification delivery history filtered by market, channel, and event type, with provider message-ids for traceability against carrier records.

**Why this priority**: Required for SAMA / market-regulator compliance reviews. Lower priority than P1/P2 because it is a reporting concern.

**Independent Test**: Run the audit-report query for a 90-day window. Confirm every `Notification` row appears with channel, event type, market, provider, provider message-id, send timestamp, delivered timestamp (if any), and final status.

**Acceptance Scenarios**:

1. **Given** 90 days of delivery history, **When** an auditor queries with `(market_code, channel, event_type)` filter, **Then** the result includes provider attribution fields per `notifications.deliveries`.
2. **Given** retention policy ≥ 90 days for delivery details and ≥ 365 days for `audit_log_entries`, **When** the auditor queries 180-day-old delivery, **Then** only the audit-log row is available (delivery details GC'd per retention).
3. **Given** an opt-out occurred 60 days ago, **When** the auditor queries opt-out events, **Then** the `preference.opt_out` audit row is returned with channel, market, and timestamp.

---

### Edge Cases

- **Locale missing in published template**: render gate rejects publish (US3 ac 4); runtime never sees a half-localized template.
- **Customer locale unset**: default to market's primary locale (`ar` for `sa`, `ar` for `eg` per Principle 4 — Arabic-first market posture); fall back to `en` only if the chosen locale's body is missing (which is impossible at runtime per Principle 4 enforcement, but defensive).
- **Push token invalidated by app uninstall**: provider returns `unregistered`; `Notification` transitions to `failed` with reason `push_token_invalid`; the token is marked invalid on the user-device record (spec 004); future push attempts to that token are skipped.
- **Email bounce (hard)**: provider webhook returns `bounce`; `Notification` transitions to `failed` with reason `email_hard_bounce`; the email-channel preference is auto-disabled with audit; customer must reset email in profile to re-enable.
- **Email bounce (soft)**: provider webhook returns `soft-bounce`; the system retries on next event; no auto-disable.
- **Customer in a quiet-hours window**: per-market send-window policy (e.g., KSA: marketing-only no-send 22:00–08:00 KSA local) defers marketing notifications until window opens; transactional bypass quiet hours; per-market schema documented in `notifications.market_schemas`.
- **Cross-market customer**: a customer with EG account but currently in KSA — market is set at account-creation and immutable at v1; market migration deferred. Send-window policy follows the account's market, not the device IP.
- **Template-render failure (placeholder missing)**: `Notification` transitions to `failed` with reason `template_render_error`; an alert fires on the rate; root cause likely upstream (event payload missing fields).
- **Campaign targets > 100K recipients**: enqueue throttled to documented per-second cap to avoid provider-side rate-limit (Unifonic-published cap, SES per-second cap, FCM per-app cap). Cap configurable per provider.
- **Provider webhook delivered out of order or duplicated**: idempotency on provider message-id; duplicate webhook is ignored; out-of-order is reconciled by `event_kind` precedence (`delivered` > `sent` > `queued`).
- **Unsubscribe link signature expired**: signed token has 30-day TTL; customer sees a "link expired" page with a CTA to log in and manage preferences directly.
- **Customer logs in to manage preferences but token is anonymous-issued**: re-authenticate per spec 004 before applying preference change; audit captures both the anonymous-token use AND the authenticated re-confirm.
- **WhatsApp request received**: route returns 501 Not Implemented with a clear "deferred to Phase 1.5-f" message. No silent acceptance.

---

## User Roles

| Role | Responsibilities | Permissions |
|---|---|---|
| **Customer** | Receive notifications; opt out of marketing channels; (basic) view recent notifications. | Read own delivery history (90 days); update own preferences. |
| **Template Author** (admin) | Create/edit templates in `draft`; submit for review. | Write to templates in `draft` state only. |
| **Template Reviewer** (admin) | Approve / reject `in_review` templates; trigger publish. | Approve/reject; cannot edit body during review. |
| **Campaign Manager** (admin) | Author and schedule campaigns; pause/cancel; view reports. | CRUD on campaigns; cannot edit templates. |
| **Notifications Operator** (admin) | View dead-letter queue; trigger retries; toggle backup providers; rotate provider credentials. | Read all delivery rows; act on dead-letter; configure provider routing. |
| **Auditor** | Read-only delivery + audit-log access for compliance reviews. | Read deliveries + audit-log; no writes. |
| **System (event subscriber)** | Consume domain events from spec 004/011/etc. and enqueue notifications. | Internal — no human authentication. |

Roles map onto spec 004's RBAC system. New role definitions added under `notifications.*` permission scopes.

---

## Business Rules

1. **BR-1 — Per-market provider routing.** Every channel × market pair has a primary provider and an optional backup. Routing is configured per-market, never hardcoded.
2. **BR-2 — Locale completeness on publish.** Templates MUST publish with both AR and EN bodies non-empty; AR MUST be editorial-grade per Principle 4. Publish is rejected otherwise.
3. **BR-3 — Marketing vs transactional opt-out scope.** Opt-out applies to **marketing channels only**. Transactional events (OTP, order-status, refund, verification, shipping) bypass opt-out per market regulation (transactional notifications are not marketing).
4. **BR-4 — Retry budget per channel.** SMS: 3 attempts, OTP-priority queue capped at TTL. Email: 5 attempts over 24 hours. Push: 2 attempts over 1 hour (push tokens are ephemeral).
5. **BR-5 — Dead-letter on budget exhaustion.** A `Notification` that exhausts retries transitions to `dead_letter` and is held for operator review; auto-retry from `dead_letter` is forbidden (BR-13).
6. **BR-6 — Send-window compliance per market.** Per-market schemas define quiet hours for marketing. Marketing notifications outside the window are deferred; transactional notifications bypass.
7. **BR-7 — Rate limits per recipient.** Maximum 5 transactional + 1 marketing notifications per channel per recipient per 24 hours, configurable per market.
8. **BR-8 — Templates are versioned & snapshot-referenced.** A `Notification` carries a snapshot of the template version at creation; later template edits do not retroactively change rendered content.
9. **BR-9 — Idempotency on provider message-id.** Webhook handlers MUST be idempotent: duplicate webhook delivery (provider re-delivery, etc.) does not produce duplicate state changes or duplicate audit rows.
10. **BR-10 — Hard-delete forbidden.** Templates, campaigns, notifications, and preferences are soft-deleted. Hard-delete is forbidden (matches FR-005a from prior phases).
11. **BR-11 — All provider credentials sourced from Key Vault.** No provider API key, account SID, or service-account JSON appears in `appsettings*.json`. Inherits from spec 003 + E1.
12. **BR-12 — Audit every state-changing action.** Template publish/archive, campaign send/pause/cancel, preference change, opt-out, dead-letter transition, provider failover — all audit-logged with actor identity.
13. **BR-13 — No auto-retry from `dead_letter`.** An operator must explicitly trigger retry; this prevents infinite loops on systemic provider failures.
14. **BR-14 — Per-market unsubscribe language.** Unsubscribe footer copy MUST match per-market regulatory requirements (KSA + EG specific copy in `notifications.market_schemas`).
15. **BR-15 — OTP isolation.** OTP-channel notifications use a dedicated worker pool and dedicated provider queue; no campaign or non-OTP transactional volume can starve OTP.
16. **BR-16 — Deferral note on WhatsApp.** WhatsApp is explicitly out of scope for v1 (spec 1.5-f). Any code path that would route WhatsApp MUST return 501 with a clear message.

---

## User Flow

### Flow 1 — Transactional event → notification (e.g., order-placed)

```
Order module publishes order.placed event (spec 011)
  → notification module's OrderPlacedSubscriber consumes
  → Recipient resolved (customer_id → contact methods + preferences from spec 004 + spec 011)
  → For each (enabled-channel × marketing/transactional eligibility):
      → resolve provider for (channel, market)
      → resolve template version for event_kind
      → render with event payload + customer locale
      → enqueue Notification in `pending` (idempotency key = correlation_id + channel)
  → Worker pulls from queue → state: queued → sending
  → Provider invoked, message-id captured
  → state: delivered (on synchronous success) OR queued for webhook (async)
  → Provider webhook arrives → state finalization (delivered | failed | bounced)
  → Audit event emitted at every transition
```

### Flow 2 — OTP request → SMS

```
Auth module publishes auth.otp_requested (spec 004)
  → OtpSubscriber consumes (HIGH priority, dedicated queue)
  → Recipient = phone_e164 from event
  → Provider = SMS for market (Unifonic for sa, TBD for eg per clarify)
  → Render OTP template (single locale per customer)
  → Send synchronously with 3 retries inside TTL
  → state: delivered OR failed (within TTL)
  → audit event with masked phone (last 4 digits)
```

### Flow 3 — Template lifecycle

```
Author creates draft → writes AR + EN bodies + placeholder list
  → submits for review → state: in_review → audit
Reviewer approves → state: published → previous published auto-archives
  → audit event template.published
  → cache invalidates → next render uses new version
```

### Flow 4 — Campaign

```
Manager authors campaign → target segment + template + channel + send_at
  → state: draft
Manager schedules → state: scheduled
At send_at, scheduler picks up:
  → state: sending
  → snapshot recipient list at this moment
  → for each recipient: enqueue Notification (respect per-channel preference + opt-out + send-window + rate limit)
  → state: completed when queue drained
Manager may: pause (state: paused, resumable) | cancel (state: cancelled, terminal)
```

### Flow 5 — Customer opt-out (marketing)

```
Customer receives marketing email
  → clicks signed unsubscribe link (HMAC-SHA256, 30-day TTL)
  → server validates signature → loads preference page (no auth required for this single action)
  → customer clicks "confirm" → preference.email_marketing = false
  → audit event preference.opt_out
  → next campaign send: skip
```

### Flow 6 — Provider failover

```
Worker detects primary provider failure rate > 50% in 5 min
  → audit event provider.degraded
  → operator-configured backup provider engages (manual or auto per BR-1 setting)
  → audit event provider.failover
  → new Notifications use backup until operator manually reverts
```

### Flow 7 — Dead-letter handling

```
Notification exhausts retry budget
  → state: dead_letter
  → audit event notification.dead_letter
  → alert fires if dead-letter rate > 1% / 10 min
Operator opens dead-letter view in admin
  → reviews root cause
  → optionally: "retry now" → re-enqueue with retry_count=0
  → optionally: "discard" → state remains dead_letter, archived after 30 days
```

---

## UI States

This is a backend-heavy spec, but two admin UI surfaces exist:

### Admin: Template Editor
- Loading
- Empty (no templates yet)
- Draft (editing — auto-save)
- In Review (reviewer view, read-only body, comments allowed)
- Published (read-only, archive button)
- Archived (read-only)
- Locale-incomplete error (cannot publish)
- Render-preview error (placeholder mismatch)

### Admin: Campaign Manager
- Loading
- Empty (no campaigns)
- Draft (editor, target preview shows count)
- Scheduled (countdown, edit allowed until send_at - 5 min)
- Sending (live progress: enqueued / sent / delivered / bounced / skipped)
- Paused (resume CTA)
- Completed (report)
- Cancelled (read-only)

### Customer: Basic Opt-out (single page)
- Loading
- Confirm Unsubscribe (CTA: "Confirm" / "Keep me subscribed")
- Confirmed (success message in customer's locale)
- Link Expired (CTA: "Sign in to manage preferences")
- Link Invalid (signature mismatch error)

Per Principle 27, all states have loading / empty / error / success / restricted variants. Per Principle 4, every UI string has AR + EN editorial copy.

---

## Data Model

### New tables under `notifications` schema

| # | Table | Purpose | Key columns |
|---|---|---|---|
| 1 | `templates` | Template definitions | `id`, `event_kind`, `current_version_id`, `state`, `created_at` |
| 2 | `template_versions` | Versioned content per template | `id`, `template_id`, `version_no`, `state`, `body_ar`, `body_en`, `subject_ar`, `subject_en` (email/push only), `placeholders[]`, `published_at`, `archived_at` |
| 3 | `notifications` | Per-recipient notification record | `id`, `correlation_id`, `recipient_id`, `recipient_kind` (`customer`, `admin`, `anonymous`), `channel` (`sms`, `email`, `push`), `event_kind`, `template_version_id`, `market_code`, `locale`, `state`, `provider_id`, `provider_message_id`, `attempts`, `created_at`, `delivered_at`, `failed_at`, `payload_redacted_jsonb` |
| 4 | `deliveries` | Delivery attempts (one per attempt) | `id`, `notification_id`, `attempt_no`, `provider_id`, `provider_message_id`, `status`, `error_code`, `error_message_redacted`, `requested_at`, `responded_at` |
| 5 | `webhooks_received` | Idempotency table for provider webhooks | `provider_id`, `provider_message_id`, `event_kind`, `received_at` (composite PK) |
| 6 | `campaigns` | Campaign definitions | `id`, `name`, `state`, `template_id`, `channel`, `target_criteria_jsonb`, `send_at`, `created_by`, `recipient_count_snapshot`, `started_at`, `completed_at` |
| 7 | `campaign_recipients` | Materialized recipient list per campaign | `campaign_id`, `recipient_id`, `notification_id` (nullable), `skipped_reason` (nullable) |
| 8 | `preferences` | Per-customer channel preferences | `customer_id`, `channel`, `category` (`transactional`, `marketing`), `enabled`, `updated_at` |
| 9 | `unsubscribe_tokens` | Signed opt-out tokens | `token_hash`, `customer_id`, `channel`, `category`, `expires_at`, `used_at` |
| 10 | `provider_routing` | Provider-per-(market, channel) configuration | `market_code`, `channel`, `primary_provider_id`, `backup_provider_id`, `auto_failover_enabled`, `failover_threshold_pct`, `updated_at` |
| 11 | `dead_letter_queue` | Operator review queue for exhausted retries | `notification_id`, `last_error_message_redacted`, `entered_at`, `resolved_at`, `resolution` (`retry`, `discard`, null) |
| 12 | `market_schemas` | Per-market send-window + unsubscribe-language config | `market_code`, `quiet_hours_marketing`, `unsubscribe_footer_ar`, `unsubscribe_footer_en`, `rate_limit_marketing_per_24h`, `rate_limit_transactional_per_24h` |

### State machines

**Notification.state** (`notifications.state`):
```
pending → queued → sending → delivered
                   ↓
                 failed → retrying → (loop up to budget) → dead_letter
                   ↓
                 skipped (terminal — opt-out, send-window, rate-limit)
```

**TemplateVersion.state** (`template_versions.state`):
```
draft → in_review → published ↔ archived
```

**Campaign.state** (`campaigns.state`):
```
draft → scheduled → sending → completed
            ↓         ↓
       cancelled    paused → sending (resumable)
                       ↓
                   cancelled
```

### Key invariants

- `templates.current_version_id` always points to the latest `published` version (or `NULL` if none yet).
- A `Notification` row's `template_version_id` is set at creation time and is immutable thereafter (BR-8 snapshot).
- `webhooks_received` enforces idempotency via composite PK; duplicate webhook delivery is ignored.
- `unsubscribe_tokens.used_at` is set on first use; subsequent uses of the same token are no-ops (idempotent).

### Cross-references

- `notifications.recipient_id` references `auth.users.id` (spec 004) when `recipient_kind=customer`.
- `templates.event_kind` is a controlled enum mapping to event names from specs 004 (auth.otp_requested), 011 (order.placed, order.shipped, order.refund_initiated, etc.), 020 (verification.approved/rejected), pricing.* (price-drop), inventory.* (restock), and a small marketing-only set.
- `provider_routing.primary_provider_id` and `backup_provider_id` reference a closed enum of provider slugs (`ses`, `unifonic`, `fcm`, plus per-market backups locked in clarify).

---

## Validation Rules

### V-1 — Template publish gate
- Both `body_ar` and `body_en` non-empty (BR-2).
- All placeholders declared in `placeholders[]` are referenced in BOTH bodies; rejection on mismatch.
- AR body passes the editorial-quality marker (manual reviewer sign-off; reviewer marks `ar_editorial_reviewed=true` before publish).
- Reviewer ≠ author (separation of duties; ≥ 2 distinct admin actors).

### V-2 — Notification create
- `recipient_kind` and `recipient_id` consistent (`customer` requires existing user).
- `market_code` ∈ {`sa`, `eg`}.
- `locale` ∈ {`ar`, `en`}.
- `channel` ∈ {`sms`, `email`, `push`} (WhatsApp returns 501).
- `event_kind` matches a registered event-subscriber.

### V-3 — Campaign send
- `send_at` is in the future at scheduling time; minimum lead = 5 minutes.
- Target criteria yields a non-empty recipient list.
- Channel + template are compatible (e.g., push template can render a 200-char body; campaign would fail otherwise).
- Channel marketing-eligible (campaigns NEVER use OTP channel).

### V-4 — Preference change
- Customer authenticated via spec 004 OR via a valid signed unsubscribe token (single-use, 30-day TTL).
- Cannot opt out of `transactional` category — UI hides the toggle; API rejects.

### V-5 — Provider routing
- Every (market, channel) pair MUST have a primary provider configured before any notification of that combination is sent.
- Primary ≠ backup (no self-failover).
- `failover_threshold_pct` ∈ [10, 90].

### V-6 — Idempotency
- `webhooks_received` PK enforces idempotency at the database level.
- The `correlation_id + channel` index on `notifications` deduplicates retried event subscriptions.

### V-7 — Rate limits
- Per-recipient send count over the last 24h MUST NOT exceed BR-7 caps. Excess goes to `skipped` with reason `rate_limited`.

---

## API / Service Requirements

### S-1 — Public customer endpoints

| Endpoint | Method | Auth | Purpose |
|---|---|---|---|
| `/notifications/me` | GET | customer JWT | List recent (90 days) notifications for the calling customer |
| `/notifications/me/preferences` | GET | customer JWT | Read preferences |
| `/notifications/me/preferences` | PATCH | customer JWT | Update preferences (transactional category is read-only) |
| `/notifications/unsubscribe?token=<signed>` | GET | none (token-validated) | Render confirmation page |
| `/notifications/unsubscribe` | POST | none (token-validated) | Confirm opt-out |

### S-2 — Provider webhook endpoints

| Endpoint | Method | Auth | Purpose |
|---|---|---|---|
| `/notifications/webhooks/ses` | POST | SNS signature | Receive SES delivery/bounce events |
| `/notifications/webhooks/unifonic` | POST | HMAC-SHA256 signature (vault-key) | Receive Unifonic delivery events |
| `/notifications/webhooks/fcm` | POST | OIDC token (FCM-issued) | Receive FCM delivery / unregistered events |

All webhook endpoints: idempotent (V-6); signature-validate fail-closed; reject unrecognized provider message-ids with 200 (per provider best practice — never reject signed but unknown ids, just log).

### S-3 — Admin endpoints (under `/admin/notifications/...`)

| Endpoint | Method | Permission |
|---|---|---|
| `/admin/notifications/templates` | GET, POST | `template-author` (POST), `template-reader` (GET) |
| `/admin/notifications/templates/{id}` | GET, PATCH (draft only) | `template-author` |
| `/admin/notifications/templates/{id}:submit` | POST | `template-author` |
| `/admin/notifications/templates/{id}:approve` | POST | `template-reviewer` (and reviewer ≠ author per V-1) |
| `/admin/notifications/templates/{id}:reject` | POST | `template-reviewer` |
| `/admin/notifications/campaigns` | GET, POST | `campaign-manager` |
| `/admin/notifications/campaigns/{id}:schedule` | POST | `campaign-manager` |
| `/admin/notifications/campaigns/{id}:pause` | POST | `campaign-manager` |
| `/admin/notifications/campaigns/{id}:cancel` | POST | `campaign-manager` |
| `/admin/notifications/dead-letter` | GET | `notifications-operator` |
| `/admin/notifications/dead-letter/{id}:retry` | POST | `notifications-operator` |
| `/admin/notifications/provider-routing` | GET, PUT | `notifications-operator` |
| `/admin/notifications/provider-routing/{market}/{channel}:failover` | POST | `notifications-operator` |
| `/admin/notifications/deliveries` | GET (filterable) | `auditor`, `notifications-operator` |

### S-4 — Internal event subscribers (MediatR INotificationHandler<T>)

| Event consumed | Handler |
|---|---|
| `auth.otp_requested` | `OtpRequestedSubscriber` |
| `order.placed`, `order.confirmed`, `order.shipped`, `order.delivered`, `order.cancelled` | `OrderEventSubscriber` |
| `order.refund_initiated`, `order.refund_completed` | `RefundEventSubscriber` |
| `verification.approved`, `verification.rejected` | `VerificationResultSubscriber` |
| `pricing.price_dropped` | `PriceDropSubscriber` |
| `inventory.restocked` | `RestockSubscriber` |
| `cart.abandoned_24h` | `AbandonedCartSubscriber` |
| `shipping.status_changed` (from spec 026 when ready) | `ShippingStatusSubscriber` |

### S-5 — Provider abstraction interface

`INotificationProvider` (one impl per provider per channel):
- `Task<SendResult> SendAsync(NotificationDispatch d, CancellationToken ct)` — synchronous send returning provider message-id.
- `bool SupportsChannel(Channel c)` — capability check.
- `bool ValidateWebhookSignature(HttpRequest req, string vaultSecretName)` — signature verification.

Provider impls live under `Modules/Notifications/Providers/{Ses,Unifonic,Fcm}/`. New providers added without touching subscribers (BR-1 + Principle 19).

---

## Edge Cases

(See "User Scenarios → Edge Cases" above for the primary list. The following supplements with infrastructure-adjacent cases.)

- **Key Vault secret rotation mid-flight**: a `Notification` is in `sending` when the provider credential rotates; the worker uses the credential resolved at request-build time; subsequent attempts use the rotated credential (no in-flight retry to the old one).
- **Event subscriber consumes a stale event from before its registration**: a replay protection mechanism — the subscriber checks `event_timestamp` against subscriber-registration timestamp; events older than registration are skipped with audit `notification.skipped_event_pre_registration` to avoid backfill notifications.
- **Recipient deactivated/deleted (spec 004 soft-delete)**: notifications already enqueued with that recipient transition to `skipped` with reason `recipient_deactivated`.
- **Locale unset at runtime for new account**: defaults to market's primary locale (per Principle 4 — Arabic-first markets).
- **Two campaigns target the same customer same day**: BR-7 rate limit applies; the second campaign's enqueue marks the recipient `skipped` with reason `rate_limited`; the campaign report shows the skip.
- **Dead-letter accumulation > 24 h**: alert fires; runbook prescribes operator review.
- **Provider returns success synchronously but webhook never arrives**: a reconciliation job runs every 30 min to age out `Notification` rows in `sending` for > 1 hour, transitioning to `failed` with reason `webhook_timeout` and emitting an alert if rate > 0.1%.

---

## Acceptance Criteria

### AC — Foundations

- **AC-1**: Database migrations for the 12 new tables in the `notifications` schema apply cleanly on a fresh Postgres 16 instance and on a Staging environment with prior phase data.
- **AC-2**: All 12 tables carry the four mandatory columns from spec 003 (`created_at`, `updated_at`, `deleted_at`, `market_code` where applicable).
- **AC-3**: The 8 ADR-009 secret slots provisioned by E1 (`notifications-email/multi/<provider>`, `notifications-sms/sa|eg/<provider>`, `notifications-push/multi/<provider>`) are populated by 025 with real provider credentials; the placeholder values are replaced; one `secret.placeholder_replaced` audit event fires per replacement.

### AC — Templates

- **AC-4**: An admin with `template-author` role can create a template with both AR and EN bodies; can submit for review; the state machine moves through `draft → in_review`.
- **AC-5**: A reviewer (≠ author) approves; the state moves to `published`; the previous published version of the same template auto-archives.
- **AC-6**: Publish is rejected when one of the locales is empty (V-1).
- **AC-7**: A `Notification` rendered against an archived template uses the snapshot version, not the latest (BR-8).

### AC — Transactional sends

- **AC-8**: A test `order.placed` event in Staging with a customer enrolled in email + push results in two `Notification` rows in `delivered` state within 60s; provider message-ids are captured.
- **AC-9**: An OTP request results in an SMS in `delivered` state within 30s p95 over a 100-request sample.
- **AC-10**: When SMS provider returns 5xx, the retry sequence (1s/3s/9s) is observed in `deliveries` rows; on exhaustion, the notification transitions to `dead_letter`.
- **AC-11**: When a customer's email is opted out of marketing, a campaign send skips them (`skipped_reason=channel_disabled_by_customer`); a transactional event still sends.

### AC — Campaigns

- **AC-12**: A campaign authored, scheduled for `send_at = now+5min`, and matching a 100-customer test segment, materializes 100 `campaign_recipients` rows at `send_at`, enqueues 100 `Notification`s (modulo opt-outs / rate limits), and transitions to `completed` when the queue drains.
- **AC-13**: A `sending` campaign can be paused; new enqueues stop within 5s; in-flight notifications complete; the campaign moves to `paused` and is resumable.
- **AC-14**: Campaign report shows accurate counts: targeted, sent, delivered, bounced, skipped (with reason breakdown).

### AC — Preferences & opt-out

- **AC-15**: A signed unsubscribe link → confirm flow opt-outs the customer for the email-marketing channel; an attempted second click is idempotent.
- **AC-16**: An expired unsubscribe link (> 30 days) shows the "expired" page and does NOT mutate preferences; an audit event fires.
- **AC-17**: A customer cannot opt out of the transactional category (UI hides the toggle; API returns 422).

### AC — Operator surfaces

- **AC-18**: The dead-letter queue lists all `dead_letter` notifications with last error and entered_at; the operator can trigger retry; the row re-enters `pending`.
- **AC-19**: The operator can configure a backup provider for (market, channel); when the primary's failure rate exceeds the configured threshold, the system auto-routes new notifications to the backup; an audit event `provider.failover` fires.
- **AC-20**: The provider-routing config refuses primary == backup (V-5).

### AC — Compliance & audit

- **AC-21**: The unsubscribe footer in marketing emails uses the per-market language from `notifications.market_schemas`; the AR copy is editorial-grade.
- **AC-22**: Marketing notifications enqueued during a market's quiet-hours window are deferred; transactional notifications are sent regardless.
- **AC-23**: Every state-changing action (template publish, campaign send, preference change, opt-out, dead-letter, provider failover) writes an audit-log row with actor identity.
- **AC-24**: An auditor's 90-day query returns delivery rows with provider attribution.

### AC — Security & residency

- **AC-25**: Provider credentials are exclusively read from Key Vault via `AddLayeredConfiguration()`; a CI guard rejects PRs that introduce a provider key in `appsettings*.json` (extends spec 003's guard).
- **AC-26**: Webhook endpoints reject invalid signatures with 401; valid signatures are accepted; idempotency table prevents duplicate processing on provider re-delivery.
- **AC-27**: PII redaction: notification payloads stored in `payload_redacted_jsonb` MUST NOT contain PAN, CVV, full national-ID, or full phone numbers. Phones masked to last-4. Verified by a CI guard scanning the payload-builder code paths.

### AC — Capacity & SLOs

- **AC-28**: A k6 load test in Staging at 5× expected launch RPS for 30 minutes drives the `OrderEventSubscriber` without backpressure-induced state-machine errors; queue depth stays under documented bounds; OTP p95 < 30s holds.
- **AC-29**: Dead-letter alerting fires within 10 minutes when dead-letter rate > 1%.

### AC — Cross-spec contracts

- **AC-30**: ADR-009 is flipped from `Proposed (narrowed)` to `Accepted` in the ADR table with the chosen stack (SES + Unifonic + FCM at v1; per-market SMS backup TBD in clarify).

---

## Success Criteria

- **SC-1**: 99% of order-placed notifications reach `delivered` within 5 minutes of the source event.
- **SC-2**: 99.5% of OTP SMS messages reach `delivered` within 30 seconds end-to-end (auth-event to provider-confirmed) in steady state.
- **SC-3**: Dead-letter rate is below 0.5% of total notifications over a 7-day rolling window in steady state.
- **SC-4**: Marketing campaign opt-out is respected on the very next campaign (zero false-positive sends to opted-out customers).
- **SC-5**: 100% of marketing notifications carry the per-market unsubscribe footer in the recipient's locale (zero violations in audit sweep).
- **SC-6**: An admin can author and publish a new bilingual template (with peer review) in under 15 minutes of admin time.
- **SC-7**: An operator can identify, diagnose, and retry a dead-letter notification in under 5 minutes.
- **SC-8**: Provider-failover from primary to backup completes within 5 minutes of the primary crossing the configured threshold (no operator action required if `auto_failover_enabled=true`).
- **SC-9**: 100% of provider credentials are sourced from Key Vault (zero matches in `appsettings*.json` CI sweep).
- **SC-10**: Auditor query for 90-day delivery + opt-out history returns within 30 seconds for a single-market filter.

---

## Phase Assignment

**Phase 1E — Integrations · Milestone 8 (spec 025)**.
Hard prerequisites: 004 (identity-and-access), 011 (order), and **E1 at exit** (Key Vault slots provisioned).

---

## Dependencies

### Hard
- **Spec 003** — `audit_log_entries` + `AddLayeredConfiguration()` + soft-delete columns.
- **Spec 004** — user identity, channel-contact methods (phone_e164, email), preference scope; OTP event publisher.
- **Spec 011** — order state machine and event publishers (`order.placed`, etc.).
- **Spec E1** — Key Vault slots `notifications-{email,sms,push}/<market>/<provider>/<key>`; ACA runtime hosting.

### Soft
- **Spec 020** — verification result events (consumed by `VerificationResultSubscriber`).
- **Spec 022** — review-related triggers (deferred — review notifications are out of scope at v1).
- **Spec 026** — shipping status events (`shipping.status_changed`); subscriber stub registers but the event source goes live when 026 ships. Race tolerated.
- **Spec 7-a/b** — pricing-related triggers (price-drop).

### Downstream consumers
- **Spec 027** (payments) — emits `payment.failed`, `payment.captured` events that 025 will surface in subsequent milestone iteration.
- **Spec 029** (qa-and-hardening) — load-tests the notifications path at 5× RPS.

---

## Assumptions

- **Email provider**: AWS SES at v1 (KSA + EG markets, multi-market). Default-on for transactional; campaign-eligible after warm-up.
- **SMS provider (KSA)**: Unifonic.
- **SMS provider (EG)**: Single-provider clarify question — defaulted in clarify session.
- **Push provider**: Firebase Cloud Messaging (FCM).
- **Backup providers**: TBD per clarify — per-market backup is configurable but a recommended-default pair gets locked in clarify.
- **Per-market quiet hours**: KSA marketing 22:00–08:00 KSA local; EG marketing 22:00–08:00 EG local. Confirm in clarify.
- **OTP TTL**: 5 minutes (matches spec 004 default).
- **Retention**: delivery details 90 days; audit-log 365+ days.
- **Languages**: AR + EN at v1 only.
- **Templates per launch**: ≤ 30 distinct event templates.

---

## Open Items

All five clarify items are resolved (see Clarifications → Session 2026-05-10). No open items remain blocking `/speckit-plan`.

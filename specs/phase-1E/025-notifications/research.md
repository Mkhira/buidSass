# Research: 025 — Notifications

**Phase**: 0 (Outline & Research)
**Date**: 2026-05-10

Eight research areas resolved.

## §1 — Provider HTTP-client choice (Refit vs bespoke `HttpClient`)

**Decision**: **Refit** for non-AWS-SDK / non-Firebase-SDK providers (Unifonic, Vodafone Egypt, Infobip, SendGrid). AWS SDK for SES; FirebaseAdmin SDK for FCM.

**Rationale**: Refit's interface-first declarations (e.g., `[Post("/messages")]`) match the contract-test approach and produce auditable provider clients in <100 LOC each. Polly attaches naturally. Bespoke `HttpClient` would duplicate boilerplate across five providers.

**Alternatives**: RestSharp (heavier dependency); raw `HttpClient` (more code, more to test).

## §2 — Hangfire vs custom worker

**Decision**: **Hangfire**. Postgres backend matches our DB choice; built-in retry, scheduling, dashboard; multiple queues map cleanly to OTP isolation (BR-15) via `[Queue("otp-priority")]`.

**Rationale**: Custom workers would re-implement retry, scheduling, deduplication, and an admin observability surface — all of which Hangfire already provides. Hangfire's Postgres storage uses the same connection so no extra infrastructure.

**Alternatives**: MassTransit (overkill — no broker), bespoke Postgres-LISTEN-based worker (re-invents Hangfire), Azure Service Bus (out-of-region operational risk for OTP latency).

## §3 — RTL preservation in HTML email templates

**Decision**: Templates carry an explicit `<html lang="ar" dir="rtl">` (or `en`/`ltr`) wrapper applied by the renderer. Inline styles use `text-align: start/end` rather than `left/right` to avoid LTR-leak in mail clients that strip dir attributes.

**Rationale**: Major mail clients (Gmail, Outlook, iOS Mail) honor `dir="rtl"` on the root; Outlook desktop strips wrapper attributes inconsistently — using logical CSS properties (`start`/`end`) hedges against that. Editorial reviewer sign-off catches client-specific regressions.

**Alternatives**: Per-client variants (too many); MJML (heavyweight; deferred to 1.5).

## §4 — AR editorial-marker enforcement

**Decision**: A boolean `ar_editorial_reviewed` column on `template_versions`, default `false`, settable only by users with `template-reviewer` role and only after viewing a render preview. Publish gate (V-1) blocks publish unless `ar_editorial_reviewed=true`.

**Rationale**: Hard-coded enforcement at the publish gate is the only reliable brake on machine-translation drift (Principle 4). Reviewer sign-off is captured as an audit event.

**Alternatives**: Automated quality scoring (unreliable for editorial standards); pure-process reliance (drift inevitable).

## §5 — FCM service-account JSON in containers

**Decision**: Service-account JSON stored as a single Key Vault secret (`notifications-push/multi/fcm/service-account-json`). Loaded once at container start by `AddLayeredConfiguration()`; FirebaseAdmin SDK initialized from a `MemoryStream` of the JSON; the JSON never touches disk.

**Rationale**: Disk-write of the JSON would create a forensic surface and a rotation-restart coupling. In-memory load matches the secret-rotation contract from E1 (no restart on rotation; SDK re-initializes lazily on next call after refresh window).

**Alternatives**: Workload Identity Federation between Azure AD and GCP (eliminates the JSON entirely) — deferred to 1.5 because it requires GCP-side cross-cloud setup work that's out of scope at v1.

## §6 — Idempotency-key derivation

**Decision**: For event-triggered notifications, the idempotency key is `sha256(correlation_id + channel + recipient_id)`. For campaigns, the key is `sha256(campaign_id + recipient_id + channel)`. Both forms are stored in the `notifications.idempotency_key` column with a unique index per (idempotency_key, NULL deleted_at).

**Rationale**: Correlation-id-derived keys mean a re-published upstream event (e.g., spec 011 republishing `order.placed` after a transient failure) does NOT enqueue a duplicate notification. Campaign keys prevent re-enqueue if the campaign worker crashes and restarts mid-materialization.

**Alternatives**: Pure DB unique constraint on `(correlation_id, channel, recipient_id)` (simpler but verbose); Redis-backed dedup (extra infra; not justified at v1).

## §7 — OTP-priority queue isolation

**Decision**: Hangfire queues named `otp-priority` and `default`. Two server instances configured per ACA replica:
- `OtpServer`: workers consume only `otp-priority`, count = 4 per replica.
- `DefaultServer`: workers consume `default` queue, count = 2 per replica.
OTP subscribers explicitly enqueue with `BackgroundJob.Enqueue<OtpDispatchWorker>(... [Queue("otp-priority")])`.

**Rationale**: Hangfire's per-server queue subscription is the cleanest Bus-level isolation. Doubling worker count on OTP gives latency headroom even during campaign bursts on `default`.

**Alternatives**: Single worker pool with priority levels (Hangfire doesn't support priorities natively); separate Hangfire schemas (operationally complex).

## §8 — Dead-letter archival

**Decision**: Dead-letter rows live in `notifications.dead_letter_queue` while unresolved. `DeadLetterArchiver` runs nightly and moves rows older than 30 days (per clarify-locked retention) to a `notifications.dead_letter_queue_archive` table — same shape, different table — preserving query-ability without bloating the operator-facing review queue.

**Rationale**: Operators query the live table for the working set; auditors query the archive table for compliance. Splitting the two prevents the operator UI from degrading under accumulation.

**Alternatives**: Soft-delete in place + view filter (reasonable; the archive-table approach is preferred for audit-evidence visibility).

---

All eight resolutions decided; no NEEDS CLARIFICATION remaining.

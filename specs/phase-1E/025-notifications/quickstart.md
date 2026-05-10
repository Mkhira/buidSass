# Quickstart: 025 — Notifications

**Phase**: 1 (Design)
**Date**: 2026-05-10
**Audience**: a backend engineer wiring the notifications module on Staging for the first time, after E1 has provisioned the runtime.

## Prerequisites

| Item | Verify |
|---|---|
| E1 at exit | `gh workflow run deploy-staging.yml` succeeded; `kv-dental-stg` has the 12 placeholder slots present. |
| Spec 003 + 004 + 011 at DoD | EF migrations from those modules applied to Staging Postgres. |
| Provider accounts ready | SES sandbox-out, Unifonic test-account, Vodafone Egypt sandbox, FCM service account JSON in hand. |

## Step 1 — Apply EF migrations

```bash
dotnet ef migrations script --idempotent \
  --project services/backend_api \
  --context NotificationsDbContext > /tmp/notifications.sql
psql "$STAGING_DB_URL" -f /tmp/notifications.sql
```

12 tables under `notifications` schema appear; verify with `\dn+` then `\dt notifications.*`.

## Step 2 — Populate provider credentials in Key Vault

```bash
# Replace E1 placeholder slugs with real provider slugs
az keyvault secret set --vault-name kv-dental-stg --name notifications-email--multi--ses--api-key --value "$SES_KEY"
az keyvault secret set --vault-name kv-dental-stg --name notifications-sms--sa--unifonic--api-key --value "$UNIFONIC_KEY"
az keyvault secret set --vault-name kv-dental-stg --name notifications-sms--eg--vodafone-egypt--api-key --value "$VFE_KEY"
az keyvault secret set --vault-name kv-dental-stg --name notifications-push--multi--fcm--service-account-json --file fcm.json
# Backups
az keyvault secret set --vault-name kv-dental-stg --name notifications-email--multi--sendgrid--api-key --value "$SG_KEY"
az keyvault secret set --vault-name kv-dental-stg --name notifications-sms--sa--infobip--api-key --value "$INFOBIP_SA"
az keyvault secret set --vault-name kv-dental-stg --name notifications-sms--eg--infobip--api-key --value "$INFOBIP_EG"
```

Each populated slot emits `secret.placeholder_replaced` audit event.

## Step 3 — Seed `provider_routing` and `market_schemas`

```bash
dotnet run --project services/backend_api -- seed --module notifications --mode apply
```

This executes `NotificationsV1Seeder` which inserts:
- `provider_routing` rows for `(sa,sms)`, `(eg,sms)`, `(*,email)`, `(*,push)` with primary + backup.
- `market_schemas` for `sa` and `eg` with quiet hours + unsubscribe footers.
- 30 sample template drafts (AR + EN) covering all event_kinds.

## Step 4 — Publish initial templates

In the admin web (`https://admin-stg.<env>/notifications/templates`):
1. Filter by `state=draft`.
2. For each: review AR + EN copy with the editorial reviewer, mark `ar_editorial_reviewed=true`, submit for review.
3. Reviewer (different user) approves → state moves to `published`.

Or via API for automation in CI:
```bash
curl -X POST "$BASE/admin/notifications/templates/$ID:submit" -H "Authorization: Bearer $AUTHOR_TOKEN"
curl -X POST "$BASE/admin/notifications/templates/$ID:approve" -H "Authorization: Bearer $REVIEWER_TOKEN"
```

## Step 5 — End-to-end transactional test

Place a test order (use the Phase 1D order seeder):
```bash
dotnet run --project services/backend_api -- test-publish-event \
  --kind order.placed --customer-id <id> --market sa
```

Within 60s, observe:
- Two `notifications.notifications` rows (email + push) for the customer.
- Both transition `pending → queued → sending → delivered`.
- Provider message ids captured.
- Audit-log rows for `notification.created` + `notification.delivered`.

## Step 6 — End-to-end OTP test

```bash
curl -X POST "$BASE/auth/otp/request" -d '{"phone":"+966500000000"}'
```

Within 30s:
- One `notifications.notifications` row, channel=sms, event_kind=otp.
- State delivered.
- p95 verified by re-running 100 times in a tight loop and aggregating timestamps.

## Step 7 — Campaign test

In admin web → Campaigns → Create:
- Target: 100 test customers (filter by `is_test=true`).
- Channel: email, marketing.
- Template: select a marketing template.
- `send_at`: now+5min.

Observe at `send_at`:
- 100 `campaign_recipients` rows materialized.
- 100 notifications enqueued (modulo opt-outs).
- Campaign state: `sending → completed`.

## Step 8 — Opt-out flow

Take any marketing email's unsubscribe link → confirm → verify:
- `preferences` row for `(customer_id, email, marketing)` is `enabled=false`.
- Audit row `preference.opt_out`.
- Re-run the campaign → that customer is in `campaign_recipients` with `skipped_reason=channel_disabled_by_customer`.

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Templates publish gate rejects | `ar_editorial_reviewed=false` or reviewer == author | Mark AR-reviewed and have a different admin approve. |
| OTP latency > 30s | OTP-priority queue not registered | Check Hangfire dashboard → confirm `OtpServer` is running with `otp-priority` queue. |
| Webhooks not received | Signature mismatch | Re-validate the vault-stored webhook signing key matches what the provider portal shows. |
| Dead-letter accumulating fast | Provider outage | Check provider status page; consider manual failover via `/admin/notifications/provider-routing/{market}/{channel}:failover`. |
| Email lands but RTL is broken | mail client stripping wrapper | Confirm template body uses `text-align: start/end` not `left/right`; re-test in target client. |

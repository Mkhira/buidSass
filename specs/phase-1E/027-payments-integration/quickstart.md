# Quickstart: 027 — Payments Integration

**Phase**: 1
**Date**: 2026-05-10
**Audience**: backend engineer wiring payments on Staging after E1 + 010 + 012 are at DoD.

## Prerequisites
- E1 at exit; Staging KV has the placeholder slots (E1 reserved 12; 027 populates 21).
- Specs 010 + 012 at DoD (cart + tax/invoice modules running).
- Provider sandbox accounts: HyperPay, Tap, Paymob, Kashier, Tabby, Tamara, Valu.
- PCI scope sign-off captured by `@security-team` (one-time review).

## Step 1 — Apply EF migrations
```bash
dotnet ef migrations script --idempotent \
  --project services/backend_api --context PaymentsDbContext > /tmp/payments.sql
psql "$STAGING_DB_URL" -f /tmp/payments.sql
```
Verify: `\dt payments.*` shows 12 tables.

## Step 2 — Run PCI scope sweep on the fresh schema
```bash
psql "$STAGING_DB_URL" -c "
SELECT table_name, column_name FROM information_schema.columns
WHERE table_schema = 'payments'
AND column_name ~* '(pan|primary_account_number|card_number|cvv|cvc|track1|track2|magstripe|card_pin|card_expiry)';
"
```
Expected: zero rows. If non-empty, **STOP** and remediate before populating credentials.

## Step 3 — Populate provider credentials
```bash
# KSA card primary
az keyvault secret set --vault-name kv-dental-stg --name payments--sa--hyperpay--api-key --value "$HP_KEY"
az keyvault secret set --vault-name kv-dental-stg --name payments--sa--hyperpay--api-secret --value "$HP_SECRET"
az keyvault secret set --vault-name kv-dental-stg --name payments--sa--hyperpay--webhook-signing-key --value "$HP_WHK"
# Repeat for the remaining 6 providers (Tap, Paymob, Kashier, Tabby, Tamara, Valu)
```
Each emits `secret.placeholder_replaced`.

## Step 4 — Seed `provider_routing` and `payment_methods_market_config`
```bash
dotnet run --project services/backend_api -- seed --module payments --mode apply
```
Inserts: `provider_routing` rows for each (market, method) pair; `payment_methods_market_config` rows enabling Mada, Apple Pay, STC Pay, Tabby, Tamara, COD, bank transfer for KSA, and Visa/MC, Apple Pay, Meeza, Valu, COD, bank transfer for EG.

## Step 5 — End-to-end card payment test
Place a paid order with a HyperPay sandbox Mada test card. Within 10s observe:
- `Payment` row state: `pending_authorization → captured`.
- `payment.captured` event published.
- Order's `payment_status` = `captured` (per spec 011 subscription).

## Step 6 — End-to-end BNPL test
Trigger Tabby flow with the sandbox-approved customer profile. Verify:
- Redirect URL returned.
- Webhook arrives → `Payment` transitions to `captured`.
- `payment.captured` event published with `method=bnpl_tabby`.

## Step 7 — End-to-end COD test
Place a COD order in EG. Verify `Payment.state=pending_collection_on_delivery`. In admin UI, mark "cash received" → state advances to `captured`.

## Step 8 — End-to-end bank transfer test
Place a bank-transfer order. Verify a unique reference is shown. In admin UI, mark "matched bank statement" → state advances to `captured`.

## Step 9 — Reconciliation dry-run
Run the reconciliation job manually for yesterday:
```bash
gh workflow run reconciliation-dry-run.yml -f date=$(date -u -v-1d +%F)
```
Verify a `reconciliation_runs` row exists with matched/exception counts. Inject one orphan provider row (via test fixture) and re-run to verify exception generation.

## Step 10 — Webhook replay test
Stop the webhook handler for 5 minutes during synthetic webhook traffic. Restart. Trigger replay via admin UI for the lost window. Verify all events processed; idempotency holds.

## Troubleshooting

| Symptom | Fix |
|---|---|
| Hosted-fields don't render | Provider domain not allow-listed in CSP; check `apps/customer_flutter/web/csp` |
| Webhook signature 401 | Re-verify HMAC secret in KV matches provider portal |
| Reconciliation finds many orphans | Provider may have settled async; check provider settlement-day cadence |
| Refund > captured rejected | Expected per V-5; check existing partial refunds sum |
| BNPL "rejected" with no detail | Expected — credit-decision detail withheld for privacy (BR-policy) |

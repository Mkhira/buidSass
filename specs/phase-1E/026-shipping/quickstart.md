# Quickstart: 026 — Shipping

**Phase**: 1
**Date**: 2026-05-10
**Audience**: backend engineer wiring shipping on Staging after E1 + 010 + 011 are at DoD.

## Prerequisites
- E1 at exit; Staging KV has the 4 shipping placeholder slots.
- Specs 010 and 011 at DoD (cart + order modules running).
- Provider sandbox accounts: SMSA, Aramex KSA, Aramex EG, Bosta.

## Step 1 — Apply EF migrations
```bash
dotnet ef migrations script --idempotent \
  --project services/backend_api --context ShippingDbContext > /tmp/shipping.sql
psql "$STAGING_DB_URL" -f /tmp/shipping.sql
```
Verify: `\dt shipping.*` shows 11 tables.

## Step 2 — Populate provider credentials
```bash
az keyvault secret set --vault-name kv-dental-stg --name shipping--sa--smsa--api-key --value "$SMSA"
az keyvault secret set --vault-name kv-dental-stg --name shipping--sa--aramex--api-key --value "$ARAMEX_KSA"
az keyvault secret set --vault-name kv-dental-stg --name shipping--eg--bosta--api-key --value "$BOSTA"
az keyvault secret set --vault-name kv-dental-stg --name shipping--eg--aramex--api-key --value "$ARAMEX_EG"
```
Each emits `secret.placeholder_replaced`.

## Step 3 — Seed shipping methods, zones, fee tables
```bash
dotnet run --project services/backend_api -- seed --module shipping --mode apply
```
Inserts: 12 zones (KSA-Riyadh, KSA-Jeddah, ..., EG-Cairo, EG-Alexandria, ...), 4 methods (Standard + Express per market), fee tables, and `provider_routing` rows.

## Step 4 — Publish methods
Via admin web: review AR + EN names → submit → reviewer approve → published.

## Step 5 — End-to-end fee quote
```bash
curl -X POST "$BASE/shipping/quote" -d '{
  "market_code":"sa","ship_to":{"city":"Riyadh","postal_code":"11564","country":"SA"},
  "weight_kg":3.5,"cart_total_sar":250
}' -H "Content-Type: application/json"
```
Response: list of eligible methods with fees.

## Step 6 — End-to-end shipment creation
Place a paid test order via existing 011 flow. Within 30s observe:
- `shipping.shipments` row with state=`label_purchased`.
- Label PDF blob URL is populated.
- Tracking number captured.
- `shipping.label_purchased` audit event.

## Step 7 — Webhook test
Trigger SMSA stub to send a sequence of webhooks (`handed → in_transit → out_for_delivery → delivered`); verify each transitions the shipment state, publishes `shipping.status_changed`, and is recorded in `shipment_events`. Send a duplicate webhook → verify idempotency.

## Step 8 — Failover test
Toggle SMSA stub to 5xx for 5 min; verify shipments queue then transition to `pending_label_provider_failure`. Manually swap routing via `/admin/shipping/provider-routing/sa/standard:failover`. Run the re-attempt-pending worker → confirm shipments succeed against Aramex.

## Troubleshooting
| Symptom | Fix |
|---|---|
| Quote returns "no methods" | Verify zone covers the city or postal-code prefix. |
| Label creation 401 | KV credential mismatch with provider portal — re-verify. |
| Webhook signature 401 | Re-verify HMAC secret in KV matches provider portal. |
| Status not advancing despite webhooks | Check precedence rules; an out-of-order earlier-state webhook does NOT regress. |

# Quickstart: Pricing & Tax Engine (007-a)

**Date**: 2026-04-20 | **Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)

---

## 1. Bring-Up

```bash
# from repo root, Postgres already running from spec 003
dotnet ef database update --project services/backend_api --context PricingDbContext
export PRICING__TokenSigningKey=$(openssl rand -hex 32)
dotnet run --project services/backend_api
```

Boot sequence:
1. `PricingDbContext.Database.Migrate()` applies `V007_*` migrations.
2. Default tax rules seeded (KSA 15%, EG 14%, zero-rated 0%).
3. Promo + tax rule caches primed (`MemoryCache`).
4. HMAC signing key loaded from config / Key Vault.

---

## 2. Smoke Walks

### 2.1 Resolve basket (retail, KSA)
```bash
curl -X POST http://localhost:5080/pricing/resolve-basket \
  -H "Content-Type: application/json" \
  -d '{"basketId":"01HW...","marketCode":"ksa","lines":[{"variantId":"01HV...","quantity":2}]}'
# expect 200 with basket breakdown, total = 2 * base * 1.15
```

### 2.2 Apply coupon
```bash
curl -X POST http://localhost:5080/pricing/validate-coupon \
  -H "Content-Type: application/json" \
  -d '{"basketId":"...","marketCode":"ksa","lines":[...],"couponCode":"SUMMER10"}'
# expect valid=true, discountMinor>0
```

### 2.3 Resolve token (from search)
```bash
curl -X POST http://localhost:5080/pricing/resolve-token \
  -d '{"priceToken":"<token-from-search-hit>","marketCode":"ksa"}'
# expect LineBreakdown within 100 ms
```

### 2.4 B2B basket
Send `companyId` in the request → verify `trace[].stage=business` entry present and `tier` entry carries `skipped=business_pricing_applied`.

### 2.5 Admin authoring + audit trail
Create a promotion, check `audit_events` table for `pricing.promotion.created` row with before/after (before=null for create).

### 2.6 Historical replay
```bash
curl -X POST http://localhost:5080/admin/pricing/resolve-debug \
  -H "Authorization: Bearer $ADMIN_JWT" \
  -d '{"basketId":"...","marketCode":"ksa","lines":[...],"at":"2026-03-01T00:00:00Z"}'
# expect breakdown computed against authored-data state as of 2026-03-01
```

---

## 3. Definition of Done

- [ ] All 8 user stories covered by ≥ 1 integration test
- [ ] SC-001 breakdown completeness — schema test on every resolution
- [ ] SC-002 `resolve-basket` p95 ≤ 250 ms @ 50 lines — k6 script
- [ ] SC-003 `resolve-token` p95 ≤ 100 ms — k6 script
- [ ] SC-004 0 rounding drift — FsCheck 10 000 baskets, zero failures
- [ ] SC-005 VAT golden fixtures pass byte-for-byte (KSA + EG)
- [ ] SC-006 every promo/coupon CRUD writes `audit_events`
- [ ] SC-007 tax rule change applies within 1 s of write
- [ ] SC-008 30-day audit export reconciles to finance reports, ±0 minor units
- [ ] OpenAPI published to `packages/shared_contracts/pricing/pricing.openapi.yaml`
- [ ] Observability spans emit R9 field set; no PII / coupon codes leaked
- [ ] RBAC policies `pricing.read`, `pricing.write.promo`, `pricing.write.coupon`, `pricing.write.business`, `pricing.write.tax`, `pricing.audit` wired through spec 004
- [ ] Operator runbook `docs/runbooks/pricing.md` covering tax rule changes, promo pause/expire, coupon usage-cap inspection, token-key rotation
- [ ] HMAC signing key rotation drill executed end-to-end in staging

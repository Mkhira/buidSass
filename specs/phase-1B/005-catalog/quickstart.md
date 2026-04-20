# Quickstart — Catalog (005)

**Feature**: `specs/phase-1B/005-catalog/spec.md`
**Plan**: `./plan.md`

## 1. Prerequisites

- .NET 9 SDK
- Docker (for Testcontainers Postgres)
- Local object-storage fake (provided by spec 003 `InMemoryObjectStorage` + `AlwaysCleanVirusScanner` for Development)
- Phase 1A specs 001–003 at DoD on `main`
- Spec 004 (identity-and-access) at DoD on `main` — required for admin auth + `customer.verified-professional` policy

## 2. Bring the module up locally

```bash
# From repo root
cd services/backend_api

# 1. Apply catalog migrations (includes taxonomy-key seed + restriction-reason-code seed)
dotnet ef database update --project Features/Catalog --context CatalogDbContext

# 2. Seed sample catalog (one brand, one category tree, one restricted + one unrestricted product, each with 2 variants)
dotnet run --project Tools/SeedSample -- --catalog=demo

# 3. Start the API with Development object-storage fake
Catalog__ObjectStorage=in-memory dotnet run --project Api
```

## 3. Walk the P1 acceptance scenarios

### 3.1 Customer browses a category (Story 1, AS-1..AS-5)

```bash
# Category listing
curl "http://localhost:5000/categories?parentId=<root-cat-id>" -H 'Accept-Language: en'

# Product listing under a category
curl "http://localhost:5000/products?categoryId=<cat-id>&page=1&pageSize=24" -H 'Accept-Language: ar'

# Detail for a restricted product — price visible, restriction flag + rationale present
curl "http://localhost:5000/products/<restricted-product-id>" -H 'Accept-Language: en'
```

### 3.2 Admin creates a product (Story 2)

```bash
# Login as catalog-editor admin (seeded by spec 004 tools)
ADMIN_AT=$(curl -s -X POST http://localhost:5000/admins/login \
  -H 'Content-Type: application/json' \
  -d '{"identifier":"catalog-editor@example.com","password":"<seeded>"}' | jq -r .accessToken)

# Create draft product
curl -X POST http://localhost:5000/admin/catalog/products \
  -H "Authorization: Bearer $ADMIN_AT" \
  -H 'Content-Type: application/json' \
  -d '{
    "brandId":"<brand-id>",
    "nameAr":"قفازات لاتكس","nameEn":"Latex Gloves",
    "marketingDescriptionAr":"...","marketingDescriptionEn":"...",
    "shortDescriptionAr":"...","shortDescriptionEn":"...",
    "categoryIds":["<cat-id>"],
    "restrictedForPurchase":false
  }'

# Add a variant
curl -X POST http://localhost:5000/admin/catalog/products/<pid>/variants \
  -H "Authorization: Bearer $ADMIN_AT" \
  -H 'Content-Type: application/json' \
  -d '{"sku":"GLOVE-L-100","axes":[{"key":"pack_size","valueType":"number","valueNum":100}]}'

# Upload a primary image (≤ 8 MB, JPEG/PNG/WebP/AVIF)
curl -X POST http://localhost:5000/admin/catalog/products/<pid>/media \
  -H "Authorization: Bearer $ADMIN_AT" \
  -F "file=@./sample.webp" \
  -F "altTextAr=قفاز" -F "altTextEn=Glove" -F "isPrimary=true"

# Publish (FR-015 parity gate enforced)
curl -X POST http://localhost:5000/admin/catalog/products/<pid>/publish \
  -H "Authorization: Bearer $ADMIN_AT"
```

### 3.3 Admin manages the category tree (Story 3)

```bash
# Create a nested category
curl -X POST http://localhost:5000/admin/catalog/categories \
  -H "Authorization: Bearer $ADMIN_AT" \
  -H 'Content-Type: application/json' \
  -d '{"parentId":"<parent-cat-id>","nameAr":"مشرط","nameEn":"Scalpels","slugAr":"mashrat","slugEn":"scalpels"}'

# Move under a different parent (rejected if depth > 6 or if cycle)
curl -X POST http://localhost:5000/admin/catalog/categories/<cat-id>/move \
  -H "Authorization: Bearer $ADMIN_AT" \
  -H 'Content-Type: application/json' \
  -d '{"newParentId":"<new-parent-id>","position":0}'
```

### 3.4 Restriction eligibility (Story 6)

```bash
# Unauthenticated visitor
curl "http://localhost:5000/products/<restricted-pid>/eligibility" -H 'Accept-Language: en'
# → { allowed:false, reasonCode:"requires-auth", reasonCopy:"..." }

# Unverified customer
curl "http://localhost:5000/products/<restricted-pid>/eligibility?customerId=<unverified>" -H 'Accept-Language: en'
# → { allowed:false, reasonCode:"requires-verification", ... }

# Verified-professional customer
curl "http://localhost:5000/products/<restricted-pid>/eligibility?customerId=<verified>" -H 'Accept-Language: en'
# → { allowed:true }
```

### 3.5 SKU reuse after archive (Clarification Q3)

```bash
# Archive variant
curl -X PUT http://localhost:5000/admin/catalog/products/<pid>/variants/<vid> \
  -H "Authorization: Bearer $ADMIN_AT" -H 'Content-Type: application/json' \
  -d '{"sku":"GLOVE-L-100","status":"archived","rowVersion":"<rv>","axes":[...]}'

# Create a new variant with the same SKU — now allowed; emits catalog.variant.sku.reused
curl -X POST http://localhost:5000/admin/catalog/products/<pid>/variants \
  -H "Authorization: Bearer $ADMIN_AT" -H 'Content-Type: application/json' \
  -d '{"sku":"GLOVE-L-100","axes":[{"key":"pack_size","valueType":"number","valueNum":50}]}'
```

### 3.6 Taxonomy read-only view (Clarification Q5)

```bash
curl http://localhost:5000/admin/catalog/taxonomy -H "Authorization: Bearer $ADMIN_AT"
# → JSON array of migration-seeded keys. No POST/PUT/DELETE routes exist in Phase 1B.
```

## 4. Run the tests

```bash
# Unit
dotnet test services/backend_api/Tests/Catalog.Unit

# Integration (Testcontainers Postgres + in-memory object storage)
dotnet test services/backend_api/Tests/Catalog.Integration

# Contract diff against prior published OpenAPI
pnpm --filter @buidsass/contracts contract-diff catalog
```

## 5. Regenerate shared contracts

```bash
scripts/shared-contracts/generate.sh catalog
# Writes packages/shared_contracts/catalog/{dart,ts}/
```

## 6. Verify DoD

- [ ] All FR-00X acceptance scenarios run green (3.1–3.6 above + negative twins).
- [ ] AR + EN editorial sign-off recorded in `specs/phase-1B/005-catalog/checklists/editorial-signoff.md`.
- [ ] k6 script `tests/perf/catalog/listing.k6.js` reports p95 ≤ 1.5 s at baseline load (SC-001).
- [ ] Eligibility truth-table test (12 cases + non-restricted short-circuit) is green (SC-006).
- [ ] Reindex-event latency test reports ≤ 2 s (SC-008).
- [ ] Contract fingerprint appended to PR body via `scripts/compute-fingerprint.sh`.
- [ ] No new secrets committed (gitleaks CI green).
- [ ] Audit-log sink receives an event for every action in `contracts/events.md` — verified by integration test `CatalogAuditCoverageTests.AllEventsPublished`.
- [ ] Migration-time assertion confirms `vendor_id IS NULL` on every catalog-owned row (SC-007).

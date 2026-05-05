# B2B seed surface

Spec 021 ships two seeders against `B2BDbContext`.

## `b2b.reference-data` (B2BReferenceDataSeeder)

Idempotent reference dataset; runs in every environment under
`seed --tag=b2b-reference`.

* Inserts the active `QuoteMarketSchema` v1 row for **ksa** and **eg** with the
  defaults from `quickstart.md §2`:
  * `validity_days` = 14
  * `rate_limit_per_customer_per_hour` = 10
  * `rate_limit_per_company_per_hour` = 50
  * `company_verification_required` = false
  * `tax_preview_drift_threshold_pct` = 5.00
  * `sla_decision_business_days` = 2
  * `sla_warning_business_days` = 1
  * `invitation_ttl_days` = 14

## `b2b.dev-data` — `quotes-b2b-v1` synthetic dataset (B2BDevDataSeeder)

Dev-gated synthetic dataset. `SeedGuard` blocks production; the seeder also
short-circuits when `IHostEnvironment.IsDevelopment()` is false. Idempotent —
re-runs no-op once the canary company id (`b2b00000-...-001`) is present.

What it seeds (all under stable predictable GUIDs):

* **3 companies**:
  * `b2b00000-...-001` — KSA, `approver_required=true`, `po_required=true`,
    `unique_po_required=true`, state=`active`.
  * `b2b00000-...-002` — KSA, `approver_required=false`, state=`active`.
  * `b2b00000-...-003` — EG, state=`pending-verification`.
* **2 branches** on the approver-required company.
* **6 memberships**: 1 admin + 1 buyer + 2 approvers on the canary company,
  1 admin each on the other two.
* **4 invitations** — one in each `CompanyInvitationState`
  (`pending`, `accepted`, `declined`, `expired`).
* **8 quotes** — one in each `QuoteState`
  (`requested`, `drafted`, `revised`, `pending-approver`, `accepted`,
  `rejected`, `expired`, `withdrawn`).
* **2 repeat-order templates** anchored to the synthetic accepted quote.

The dataset is intended for storefront / admin demos and manual QA, not for
automated tests (each integration test seeds its own minimal fixture).

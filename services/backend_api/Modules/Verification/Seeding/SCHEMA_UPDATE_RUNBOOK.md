# Verification Schema Update Runbook

Spec 020 task T103. How to publish a new version of a per-market verification
schema without breaking in-flight submissions.

## Invariants the DB enforces

- `verification_market_schemas` PK = `(market_code, version)`.
- Partial unique index `UX_verification_market_schemas_active_per_market` on
  `(market_code)` filtered to `effective_to IS NULL` — at most one active row
  per market.
- A verification's `(market_code, schema_version)` FK is `Restrict` — old
  versions cannot be deleted while any verification still snapshots them.

## The promotion procedure

A schema bump (new required field, tighter regex, different enum) lands as ONE
transaction:

```sql
BEGIN;
  -- 1. Retire the active version.
  UPDATE verification.verification_market_schemas
     SET "EffectiveTo" = now()
   WHERE "MarketCode" = 'ksa'
     AND "Version"    = 1
     AND "EffectiveTo" IS NULL;

  -- 2. Insert the new active version.
  INSERT INTO verification.verification_market_schemas (
      "MarketCode", "Version", "EffectiveFrom", "EffectiveTo",
      "RequiredFields", "AllowedDocumentTypes",
      "RetentionMonths", "CooldownDays", "ExpiryDays",
      "ReminderWindowsDays", "SlaDecisionBusinessDays",
      "SlaWarningBusinessDays", "HolidaysList"
  ) VALUES (
      'ksa', 2, now(), NULL,
      '[ ... v2 required_fields jsonb ... ]'::jsonb,
      '["application/pdf","image/jpeg","image/png","image/heic"]'::jsonb,
      24, 7, 365,
      '[30,14,7,1]'::jsonb, 2, 1,
      '[]'::jsonb
  );
COMMIT;
```

The same Tx pattern is encoded in `MarketSchemaVersioningTests.PublishV2Async`
and verified end-to-end in the matching unit tests.

## What in-flight rows do

Every `verifications` row holds `schema_version` snapshotted at submission. A
schema bump does NOT mutate existing rows — they continue to:

- Render in the reviewer detail with their original required_fields + labels
  (FR-026 / SC-010 — verified by
  `MarketSchemaVersioningTests.V1_verification_detail_renders_v1_schema_after_v2_publish`).
- Validate against the original schema if they re-enter validation (e.g.,
  resubmit-with-info).

New submissions after the bump validate against the NEW active schema.

## Idempotency rules

- Re-running the promotion script must be safe. Detect with a guard query
  before the UPDATE/INSERT pair:
  ```sql
  SELECT 1 FROM verification.verification_market_schemas
   WHERE "MarketCode" = 'ksa' AND "Version" = 2;
  -- If a row exists, ABORT — v2 is already published.
  ```
- The seeder (`VerificationReferenceDataSeeder`) handles initial v1 publish
  with a per-row `try-on-conflict` swallow (CR R2-4) — it stays no-op on
  re-run.

## Testing checklist before promoting

1. Capture the v2 jsonb in a fixture and run
   `MarketSchemaVersioningTests` against a fresh container — all passes.
2. Confirm the new `RequiredFieldSpec` shape parses on the C# side (check
   `SubmitVerificationValidator.ValidateAgainstSchema` behavior with the new
   patterns + enum values).
3. Confirm the customer-app form renderer can consume the new
   `required_fields` payload via `GET /api/customer/verifications/schema`.
4. Add ICU keys for any new `labelKeyEn` / `labelKeyAr` to
   `verification.{en,ar}.icu` and queue AR strings in
   `AR_EDITORIAL_REVIEW.md`.
5. Bump retention / cooldown / SLA only with explicit product approval — these
   numbers govern compliance windows.

## What NEVER changes between versions

- The schema's `MarketCode` (each market has its own version sequence).
- The state machine — `Verification.state` semantics are the same regardless
  of which schema version a row uses.
- The eligibility cache shape — the read-side query is version-agnostic.

If any of these need to change, that's a SPEC-level amendment, not a schema bump.

# Staging data policy

Staging holds **synthetic data only**. This policy is enforced by CI (`seed-pii-guard`) and is non-negotiable.

## Forbidden sources

- Production database dumps (full or partial).
- Real customer names, emails, phone numbers, national IDs, addresses, payment details.
- Screenshots or exports lifted from live sessions.
- Copy-paste from vendor CSVs that contain real buyer identifiers.

## Allowed sources

- `Bogus` (English) for non-user-visible fields.
- Curated phrase banks (JSON under `services/backend_api/Features/Seeding/Datasets/PhraseBanks/`) for editorial-grade Arabic.
- Hand-authored fixtures committed to the repo.

## PII guard (CI)

`.github/workflows/lint-format.yml › seed-pii-guard` scans `services/backend_api/Features/Seeding/**` for:

- Egyptian / KSA phone shapes (`+20…`, `+9665…`).
- Real consumer email domains (`@gmail`, `@hotmail`, `@yahoo`, `@outlook`).
- 14-digit sequences (Egyptian national ID shape).

A hit fails the PR. Replace with curated synthetic values.

## Reset cadence

- Staging database is reset weekly (Sunday 02:00 Asia/Riyadh).
- Reset = drop schema, run migrations, re-run seeders in `apply` mode.
- Seed checksums in `seed_applied` reset with the schema.

## Retention

- Logs: 30 days.
- Audit table rows: not wiped (non-production audit retention = same as production policy).
- Uploaded files (`stored_files` + blob): wiped on reset.

## Who can change this

Edits to this policy require product-leadership approval and propagate per Principle 32.

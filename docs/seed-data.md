# Seed data

Synthetic data loader for local and staging environments. **Never runs in Production**.

## Contract

Every seeder implements `ISeeder`:

```csharp
public interface ISeeder
{
    string Name { get; }                       // stable, kebab-case; appears in seed_applied
    int Version { get; }                       // bump to re-run a modified seeder
    IReadOnlyList<string> DependsOn { get; }   // other seeder Names
    Task ApplyAsync(SeedContext ctx, CancellationToken ct);
}
```

`SeedRunner` topologically sorts by `DependsOn`, computes `SHA256(Name|Version|DatasetSize)`, and writes a row to `public.seed_applied` on success. Rerun is idempotent: a matching `(Name, Version, Environment)` row skips the seeder.

## Modes

```bash
scripts/dev/seed.sh apply     # default — applies pending seeders
scripts/dev/seed.sh fresh     # clears seed_applied, re-runs all (dev/staging only)
scripts/dev/seed.sh dry-run   # logs what would run, writes nothing
```

## Per-spec seeders

Each Phase 1B spec ships its own seeder under `services/backend_api/Features/Seeding/Seeders/<spec>/`. Phase 1D specs that ship a seeder co-locate it with the module under `services/backend_api/Modules/<Module>/Seeding/`:

| Spec | Seeder name                     | DependsOn                            |
|------|---------------------------------|--------------------------------------|
| 004  | `identity-v1`                   | —                                    |
| 005  | `catalog-v1`                    | `identity-v1`                        |
| 006  | `search-v1`                     | `catalog-v1`                         |
| 007  | `pricing-v1`                    | `catalog-v1`                         |
| 008  | `inventory-v1`                  | `catalog-v1`                         |
| 020  | `verification.reference-data`   | —                                    |
| 020  | `verification.dev-data`         | `verification.reference-data`        |

### `verification-v1` synthetic dataset (spec 020, T113 / T114)

Two seeders ship under `services/backend_api/Modules/Verification/Seeding/`:

- **`verification.reference-data`** (`VerificationReferenceDataSeeder`) — environment-agnostic; idempotently inserts the two `verification_market_schemas` rows the module is built around: KSA v1 (retention 24 months) and EG v1 (retention 36 months). Both ship the V1 required-fields jsonb (`profession` enum + `regulator_identifier` regex), reminder windows `[30, 14, 7, 1]`, SLA decision = 2 business days, SLA warning = 1 business day, allowed document types `[pdf, jpeg, png, heic]`. Run via `dotnet run --project services/backend_api -- seed --mode=apply --tag=verification-reference`. Re-runs are no-ops (`(Name, Version, Environment)` row in `seed_applied`).
- **`verification.dev-data`** (`VerificationDevDataSeeder`) — Dev-gated; short-circuits unless `IHostEnvironment.IsDevelopment()` is true (defense in depth on top of `SeedGuard`). Seeds 10 synthetic customers (`11111111-...-001` through `11111111-...-010`) with verification rows covering every V1 state plus the supersession/renewal edge (customer `008` carries two rows — a superseded original and its renewal-approved successor — to demonstrate the FR-020 link):

| Customer | Market | State                              | Notes                                                        |
|----------|--------|------------------------------------|--------------------------------------------------------------|
| `001`    | KSA    | `submitted`                        | Dentist, fresh submission, no decision yet.                  |
| `002`    | KSA    | `in-review`                        | Dental lab tech, reviewer claimed.                           |
| `003`    | KSA    | `info-requested`                   | Dental student, SLA timer paused.                            |
| `004`    | KSA    | `approved` (near-expiry)           | Dentist; falls inside the 14-day reminder window.            |
| `005`    | KSA    | `rejected` (active cooldown)       | Dentist; `cooldown_until` is in the future.                  |
| `006`    | KSA    | `expired`                          | Dentist; auto-transitioned by the expiry worker.             |
| `007`    | KSA    | `revoked`                          | Dentist; FR-009 — no cooldown applies on next submission.    |
| `008`    | KSA    | `superseded` → renewal `approved`  | Dentist; demonstrates the FR-020 supersession link.          |
| `009`    | EG     | `void` (account-locked)            | Dentist; covers FR-038 lifecycle path.                       |
| `010`    | EG     | `approved` (mid-life)              | Clinic buyer; healthy active approval, no reminders pending. |

Each row carries one synthetic `VerificationDocument` and the matching `VerificationStateTransition` history. Customer ids are deterministic UUIDs so end-to-end demos and manual QA can target a specific state without re-walking the state machine. The seeder is idempotent — re-running against an already-seeded database is a no-op.

Run via `dotnet run --project services/backend_api -- seed --mode=apply --tag=verification-dev` (or `--mode=fresh` in Dev/Staging to wipe `seed_applied` and reapply). Production never runs this seeder: both `SeedGuard` and the in-handler `IsDevelopment()` check block it.

## Dataset sizes

Controlled by `Seeding:DatasetSize` (env override: `Seeding__DatasetSize`).

| Size     | Intended use             |
|----------|--------------------------|
| `small`  | Local dev, unit smoke    |
| `medium` | Staging baseline         |
| `large`  | Perf / soak testing      |

## Adding a seeder

1. Create `Features/Seeding/Seeders/<spec>/<Name>Seeder.cs` implementing `ISeeder`.
2. Register in `SeedingServiceCollectionExtensions` (`services.AddScoped<ISeeder, ...>()`).
3. Use `Bogus` with locales from `BogusLocales`; **source user-visible Arabic from curated phrase banks**, not Bogus `ar_*` (see Principle 4 — editorial-grade).
4. Emit no PII: phone numbers, real emails, national IDs. The `seed-pii-guard` CI job enforces this.
5. Bump `Version` when you change seeded shapes.

## Staging data policy

See `docs/staging-data-policy.md` — PII rules, reset cadence, retention, forbidden sources.

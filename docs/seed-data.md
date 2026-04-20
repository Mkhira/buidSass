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

Each Phase 1B spec ships its own seeder under `services/backend_api/Features/Seeding/Seeders/<spec>/`:

| Spec | Seeder name    | DependsOn                  |
|------|----------------|----------------------------|
| 004  | `identity-v1`  | —                          |
| 005  | `catalog-v1`   | `identity-v1`              |
| 006  | `search-v1`    | `catalog-v1`               |
| 007  | `pricing-v1`   | `catalog-v1`               |
| 008  | `inventory-v1` | `catalog-v1`               |

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

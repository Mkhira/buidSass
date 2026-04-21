# Quickstart — Identity and Access (Spec 004)

**Audience**: the engineer (or agent) who picks up `/speckit-tasks` for this spec. Walks the module end-to-end in ~30 minutes from a fresh checkout.

---

## Prerequisites

- Repo at HEAD of branch `004-identity-and-access`.
- A1 local environment running (`scripts/dev/up`) — Postgres 16, Meilisearch, and seed framework are live.
- `.env.local` populated from `docs/local-setup.md`.
- You're on **Lane A** (Claude/Codex). Lane B (GLM) does not touch this spec.

---

## 30-minute walk-through

### 1 · Review the spec
- Read `spec.md` top-to-bottom (42 FRs, 13 SCs, 5 clarifications).
- Read `plan.md` Constitution Check + Complexity Tracking — if you disagree with any row, raise it *before* starting tasks.

### 2 · Sketch the module layout
```
services/backend_api/Modules/Identity/
├── Primitives/
├── Customer/<slice>/…          # 12 slices
├── Admin/<slice>/…             # 13 slices
├── Authorization/
├── Entities/
├── Persistence/
└── Seeding/
```
Slice pattern (ADR-003): one folder per HTTP endpoint, each with `Request.cs` · `Validator.cs` · `Handler.cs` · `Endpoint.cs` (+ a local `*.Tests.cs` nearby under `tests/Identity.Tests/…`).

### 3 · Lay down Primitives first
- `SurfaceKind`, `MarketCode`, `Argon2idHasher`, `BreachListChecker`, `JwtIssuer`, `RefreshTokenRevocationStore`, `RateLimitPolicies`, 9 `*StateMachine.cs`.
- All slices depend on this folder. Do not start slices until Primitives compiles and the state-machine unit tests are green.

### 4 · Persistence + migration
- 17 entities in `Entities/`, each with `IEntityTypeConfiguration<T>` in `Persistence/Configurations/`.
- `IdentityDbContext` registered via ADR-004's `AddIdentityModule(builder)` extension.
- Generate initial migration: `dotnet ef migrations add Identity_Initial -p services/backend_api -o Modules/Identity/Persistence/Migrations`.
- Apply: `dotnet ef database update` against the A1 Postgres.

### 5 · Seeder + CLI bootstrap
- `IdentityReferenceDataSeeder` registers via the A1 `ISeeder` contract; idempotent across envs.
- `IdentityDevDataSeeder` guarded by `SeedGuard` — runs in Dev only.
- `SeedAdminCliCommand` wired into `services/backend_api/Program.cs` as a `dotnet run -- seed-admin --email <e>` entry point. Assert that HTTP server does not start when invoked this way.

### 6 · Slices
Order within each surface so later slices compile cleanly on earlier ones:

**Customer**: register → email/confirm → otp/request → otp/verify → sign-in → session/refresh → sign-out → password/reset-request → password/reset-complete → password/change → sessions (list+revoke) → me.

**Admin**: invitation/accept → mfa/totp/enroll → mfa/totp/confirm → sign-in → mfa/challenge → mfa/step-up(+confirm) → session/refresh → sign-out → password/* → invitations (create/revoke) → accounts/sessions (list/revoke) → accounts/role/patch → accounts/mfa/reset → me.

### 7 · Authorization wire-up
- `[RequirePermission("…")]` attribute filter + `PolicyEvaluator`.
- `authorization_audit` row emitted on every decision via `IAuthorizationAuditEmitter`.
- Role→permission matrix seeded in `IdentityReferenceDataSeeder` (`platform.super_admin` gets `*`; `platform.finance` gets the finance subset; `customer.standard` gets the customer subset).

### 8 · Rate limiters + revocation cache
- Register each policy in `RateLimitPolicies.RegisterAll(builder)`.
- Background service `RefreshRevocationCacheWorker` refreshes the in-proc bloom filter every 15 s from `revoked_refresh_tokens`.

### 9 · AR/EN editorial bundles
- Add `Messages/identity.ar.icu` and `identity.en.icu`. Every `reasonCode` referenced in the contract file maps to a pair of keys. Missing AR variant fails the `IdentityMessagesCompletenessTests` suite.

### 10 · Tests — levels
- **Unit**: state-machine transitions (all 9), Argon2id round-trip, TOTP window, breach-list matcher, phone normalizer.
- **Integration** (Testcontainers): happy paths for each endpoint + the high-value failure modes (lockout, replay, enumeration timing).
- **Contract**: one test per Acceptance Scenario in `spec.md`. Run via `dotnet test --filter Category=Contract`.
- **Property**: refresh-token rotation invariant; breach-list bloom false-positive bound.

---

## Validation that you've met the spec

Run from repo root:

```bash
# all tests
dotnet test services/backend_api/

# contract coverage — asserts every acceptance scenario is covered
dotnet test services/backend_api/ --filter Category=Contract

# audit spot check — every state-machine transition in integration test produces an audit row
./scripts/dev/identity-audit-spot-check.sh      # added in tasks phase J

# fingerprint (Guardrail #3) — must include constitution + ADR fingerprints
./scripts/compute-fingerprint.sh
```

---

## Definition of Done (DoD v1.0)

See `docs/dod.md`. For this spec specifically:

- [ ] All 42 FRs have at least one contract test.
- [ ] All 13 SCs have at least one measurable check (unit, integration, or audit spot-check).
- [ ] All 9 state machines enumerate states + transitions + actors + failure + retry (P24).
- [ ] Zero plaintext secrets in logs (verified with `./scripts/dev/scan-plaintext-secrets.sh`).
- [ ] AR editorial review label on PR (every user-facing string has an AR counterpart).
- [ ] Constitution + ADR fingerprint attached.
- [ ] Migration applied cleanly on a fresh DB AND on a DB seeded with A1 reference data.
- [ ] `seed-admin` CLI exercised against a scratch Staging DB + audit row verified.

---

## Hand-off

Once DoD is green, open the PR. Spec 005 (`catalog-foundations`) will depend on `identity.accounts` + the authorization pipeline; it starts as soon as 004 merges.

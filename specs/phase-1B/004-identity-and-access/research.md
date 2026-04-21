# Research — Identity and Access (Spec 004)

**Date**: 2026-04-22 · **Status**: All NEEDS CLARIFICATION resolved · **Inputs**: spec.md (post-clarify), constitution v1.0.0, ADR-004/009/010, spec 003 foundations.

Each decision below states *what was chosen*, *why*, and *what was rejected*. Anything not captured here remains implementation-level and will surface as a `/speckit-tasks` item, not a research gap.

---

## R1 · Argon2id password hasher

**Decision**: `Konscious.Security.Cryptography.Argon2` v1.3.x. Tiered parameters.

- **Customer**: `memory = 64 MiB`, `iterations = 3`, `parallelism = 2`, `saltLength = 16 B`, `hashLength = 32 B`. Target p95 hash time ≈ 120 ms on baseline backend VM (Azure Ddsv5).
- **Admin**: `memory = 96 MiB`, `iterations = 4`, `parallelism = 2`, `saltLength = 16 B`, `hashLength = 32 B`. Target p95 hash time ≈ 180 ms.

**Rationale**: Parameters meet the OWASP 2024 Argon2id guidance floor (≥ 46 MiB, ≥ t=1 for m=64 MiB class). Admin tier is lifted per Clarification Q4 (admin is blast-radius multiplier). Parameters are stored alongside the hash (`$argon2id$v=19$m=...,t=...,p=...$salt$hash`) so future tuning does not break existing stored hashes — verification re-derives with the stored cost; any user whose hash uses < current floor is lazy-rehashed on next successful sign-in.

**Rejected**:
- `BCrypt.Net-Next` — bcrypt is not memory-hard; cannot meet modern GPU-resistance goal. Already implicitly excluded by spec Assumption.
- PBKDF2 (via `Rfc2898DeriveBytes`) — FIPS-friendly but similarly not memory-hard.
- `System.Security.Cryptography.Argon2` — not present in .NET 9 BCL.

**Verification plan**: Property test asserts hash verification round-trip, and a microbench asserts p95 ≤ target + 30 %; if the CI bench machine is slower, the test records an advisory skip and flags for operator tuning rather than failing (tuning is an ops concern, not a correctness regression).

---

## R2 · JWT issuer + revocation store

**Decision**:
- Issuer: `Microsoft.IdentityModel.Tokens` + `System.IdentityModel.Tokens.Jwt` via `Microsoft.AspNetCore.Authentication.JwtBearer` v9.x.
- Algorithm: ES256 (ECDSA P-256). Separate key pair per surface (`customer`, `admin`), rotated quarterly; both current and previous public keys published via internal JWKS for grace-period verification.
- Access-token TTL: customer 15 min, admin 5 min (FR-013).
- Refresh token: opaque 256-bit random, stored hashed (SHA-256 + per-row salt) in `refresh_tokens` table; rotating on every use (old row marked `Consumed`, new row `Active`). Customer idle 30 d / admin idle 8 h (FR-013).
- Revocation store: Postgres table `revoked_refresh_tokens` (token-hash, revoked-at, reason). In-process bloom filter in each API instance, refreshed every 15 s and on write (local instance updates immediately, others converge in ≤ 15 s). Validation order: bloom negative → accept; bloom positive → DB confirm. Worst-case propagation ≤ 15 s + DB round-trip ≪ SC-004's 60 s budget.

**Rationale**: ES256 gives ~16× smaller signatures than RS256 and native constant-time verification; JWKS rotation lets us rotate keys without downtime. Separate key material per surface enforces SC-011 at the cryptographic layer (a customer token is literally unverifiable against admin keys). Rotation + opaque refresh is the standard OWASP recommendation; a Postgres-backed revocation store keeps launch infrastructure simple (no Redis dependency now — can be added later if p99 degrades).

**Rejected**:
- Stateless JWT with only short TTL — cannot meet FR-012 explicit revocation.
- Opaque access tokens (no JWT) — forfeits the JWT tooling the Flutter customer app will rely on in spec 014; also incurs one DB lookup per request.
- Redis revocation cache — introduces new infra before we have p99 evidence it's needed; Postgres + bloom meets the SLA.

---

## R3 · TOTP admin MFA

**Decision**: `Otp.NET` v1.4.x.

- HOTP/TOTP per RFC 6238: SHA-1, 6-digit code, 30 s period, ±1 window (absorbs ±30 s clock skew).
- Shared secret: 160-bit random, stored encrypted at rest via `IDataProtectionProvider` with a key that lives outside the module (Azure Key Vault in Staging/Prod, file-system DPAPI in Dev).
- 10 single-use recovery codes issued at enrollment, each stored hashed (Argon2id with `m=32 MiB, t=2, p=2`); user sees plaintext exactly once.
- Replay guard: `(factor_id, window_counter)` uniqueness enforced in `admin_mfa_replay_guard` table with 5-minute TTL sweep.

**Rationale**: SHA-1 is RFC-mandated for authenticator-app compatibility (Google Authenticator, Authy, 1Password, Microsoft Authenticator). `Otp.NET` is the most-used OSS .NET TOTP library with active maintenance. Encrypted-at-rest secret satisfies FR-016 "no plaintext secrets ever persisted."

**Rejected**:
- SHA-256 TOTP — better cryptographically but breaks authenticator-app interop.
- WebAuthn/passkey — spec's Out of Scope; deferred to Phase 1.5.

---

## R4 · Breach-list check (offline HIBP)

**Decision**: Download the HIBP "Pwned Passwords" top-100k list once per quarter via a CLI task (`dotnet run --project tools/BreachListRefresh`); ship the hashed list as a `.txt.gz` embedded resource in the Identity module. On password submission: SHA-1 the candidate, check bloom filter (in-memory at module startup), fall back to exact lookup on positive.

**Rationale**: FR-008 requires breach-list rejection without a run-time network dependency (residency — ADR-010 — and offline-capability in Staging CI). 100k covers the overwhelming majority of credential-stuffing wordlists; larger sets bloat the image without materially improving block rate. Quarterly refresh cadence is documented in `docs/design-agent-skills.md`-style tooling-ops notes (new file added in this spec's implementation phase).

**Rejected**:
- HIBP k-anonymity API at run time — outbound network per request conflicts with ADR-010 data-residency posture for credential material even though only a hash prefix leaves.
- Full 850M-line Pwned Passwords dump — 35 GB uncompressed; infeasible to embed.

---

## R5 · Phone normalization + market inference

**Decision**: `libphonenumber-csharp` v8.x. Parse with country hint from the user-selected market (Clarification Q5). If parse fails or country mismatches market, return structured reason `identity.phone.market_mismatch` per Edge Case #1.

**Rationale**: libphonenumber is the canonical phone-parsing library; it knows EG (+20) and KSA (+966) carrier prefixes and mobile-vs-landline distinctions. The library powers country-code pre-fill in the registration flow.

**Rejected**: Regex-based normalization — fails on landline/mobile disambiguation, carrier-code edge cases, and number-portability formatting.

---

## R6 · OTP dispatcher seam (ADR-009 is Proposed)

**Decision**: Ship `IOtpChallengeDispatcher` with three implementations:

1. `ConsoleOtpDispatcher` — Dev only, writes challenge + code to stdout + Serilog `Information` scope. Compile-time guarded by `#if DEV_OTP_SINK` and startup-asserted disabled in `Staging|Production`.
2. `UnifonicOtpDispatcher` — SMS via Unifonic (common KSA stack, ADR-009 narrowed option). Adapter only; real credentials + per-market routing decisions belong to Phase 1D spec 025 per spec §Out of Scope.
3. `SesEmailDispatcher` — SES email for email-OTP + verification-link mails. Adapter only; real routing likewise deferred to spec 025.

Dispatcher selection is DI-bound at module registration; Dev wires #1, Staging/Prod wire null-implementations (`NotConfiguredOtpDispatcher` that throws on send) until spec 025 lands, so staging-environment smoke tests will surface missing provider configuration loudly rather than silently dropping OTPs.

**Rationale**: Spec's Out of Scope explicitly defers provider selection; this plan ships the seam and a safe default. Matches the A1 pattern of shipping tooling without pre-committing to production providers.

**Rejected**: Hardcoding Twilio/Vonage/Sinch — ADR-009 is Proposed (narrowed to SES + Unifonic + FCM); locking choice here pre-empts the Stage-7 decision window.

---

## R7 · Rate-limit policies

**Decision**: `System.Threading.RateLimiting` built into .NET 9.

Tiered policies per surface (FR-019, Edge Case #11):

| Policy | Window | Permits | Scope key |
|---|---|---|---|
| `CustomerOtpRequest` | sliding 60 min | 5 | `market_code + phone_e164` |
| `CustomerSignIn` | sliding 15 min | 10 | `market_code + email_normalized` + client IP |
| `AdminSignIn` | sliding 15 min | 5 | `email_normalized` + client IP |
| `AdminOtpStepUp` | sliding 60 min | 3 | `admin_id` |
| `PasswordResetRequest` | sliding 60 min | 3 | `email_normalized` |

Limiter state: in-process partitioned limiter (bounded memory per instance). Cross-instance fairness is approximate in launch; acceptable because SC-012's target (99 % block rate at the stated thresholds) is per-session-attacker, not per-global-rate, and the 2× inflation across N=2 instances still falls well under the attacker-economics breakpoint.

**Rationale**: Built-in limiter avoids Redis dependency for launch. Distributed limiting is a Phase 1F hardening item (spec 029).

**Rejected**: AspNetCoreRateLimit package — unmaintained, superseded by built-in limiter in .NET 7+. Redis-backed limiter — infra we don't need yet.

---

## R8 · Dev seeder + operator CLI bootstrap (FR-024pre)

**Decision**:

- **Dev**: `IdentityDevDataSeeder : ISeeder` (A1 framework) runs at module startup when `ASPNETCORE_ENVIRONMENT=Development`, gated by `SeedGuard`. Idempotent — re-runs update the super-admin's TOTP enrollment if already present, never creates a second super-admin. Default credentials are non-secret (documented in `docs/local-setup.md`) and TOTP secret is re-derived deterministically from `DEV_SEED_SALT` env var so Dev test clients can scan once.
- **Staging / Prod**: `SeedAdminCliCommand` exposed as `dotnet services/backend_api -- seed-admin --email <e> [--force]`. One-shot: aborts if any super-admin already exists, unless `--force` + double-confirmation. Emits audit event `identity.admin.bootstrap` with actor = `system:cli`. Operator runs once per environment at first deploy; command is idempotent under `--force` (re-provisions TOTP but does not duplicate the row).

**Rationale**: Clarification Q3 resolved tiered bootstrap. Seeder + CLI share the same provisioning core (`IdentityBootstrapService`) so drift between Dev and Prod super-admin creation is impossible.

**Rejected**: Single runtime seeder in all envs — carries Prod footgun (accidental re-seed of Prod admin). HTTP bootstrap endpoint — circular auth dependency, documented in Complexity Tracking.

---

## R9 · Anti-enumeration + constant-time responses

**Decision**: All of register, sign-in, password-reset-request, email-verification-confirm return a uniform shape within a constant time budget:

- Always complete a "fake Argon2id verify" when the email doesn't exist, using a pre-computed dummy hash, so both branches pay the same latency.
- Always emit the generic "if that email is registered, you'll receive …" payload on password-reset regardless of user existence.
- Registration with a taken email returns the same "registration received, check your email" shell and dispatches a *different* email to the existing user (account-exists notice) instead of a verify link.

**Rationale**: SC-010's ≤ 5 % enumeration success rate requires both response-body parity *and* timing parity. The "fake verify" pattern is the defensive standard.

**Rejected**: Only response-body parity — vulnerable to side-channel timing attacks, measurable in automated test.

---

## R10 · State machine formalization

**Decision**: Each of the 9 state machines is implemented as a `sealed class <Name>StateMachine` with:

1. `Enum States` — exhaustive.
2. `IReadOnlyDictionary<States, IReadOnlySet<Transition>> Transitions` — declarative.
3. `record Transition(States To, ActorKind Actor, TriggerKind Trigger, RetryPolicy Retry)`.
4. `Result<States> TryTransition(States from, TriggerKind trigger, ActorContext actor)` — single entry point; audit-emits on success, returns structured failure on reject.

Data model file enumerates every state, transition, actor, and failure — satisfying Principle 24.

**Rationale**: P24 demands explicit state models with transitions, triggers, actors, failure, and retry. Codifying in one class per machine (vs. scattered `if (status == "X")` branches) gives test coverage a single surface and audit emission a single hook.

**Rejected**: Stateless library (e.g. `Stateless` NuGet) — useful but adds a dependency and hides audit hooks behind library callbacks; writing our own is ~200 LOC per machine and keeps P25 audit emission explicit.

---

## R11 · Testing approach

**Decision**:

- Unit: xUnit + FluentAssertions. State-machine transitions, Argon2id round-trip, breach-list matcher, phone normalizer, TOTP window, rate-limit policy selection.
- Integration: `WebApplicationFactory<Program>` + Testcontainers Postgres 16. Every HTTP endpoint, every state machine transition triggered via HTTP, every audit emission asserted against `audit_log_entries`.
- Contract: one test per Acceptance Scenario in spec.md. Assert HTTP status, response shape, audit row, state machine resulting state.
- Property: `FsCheck` for breach-list bloom-filter false-positive rate bound, rate-limit monotonic counting, and JWT rotation invariant (old refresh never accepted after rotation).

**Rationale**: Matches spec 003's testing pattern. No SQLite shortcut — Testcontainers catches Postgres-specific issues (citext, soft-delete filter, RLS if added later).

---

## R12 · AR/EN editorial surface

**Decision**: Every user-facing reason code (email subjects, OTP SMS body, error messages served to spec 014/015's UI) is defined in `Modules/Identity/Messages/identity.<locale>.icu` ICU bundle, consumed through spec 003's `MessageFormat.NET` wrapper. A contract test asserts every reason code has both `ar` and `en` keys; missing AR variant fails the test. PRs touching these bundles get the `needs-ar-editorial-review` label.

**Rationale**: Principle 4 + impeccable-brand overlay. Prevents English-only copy leakage that already burned other specs in similar projects.

**Rejected**: `IStringLocalizer` with .resx files — lacks ICU plural/gender handling; spec 003 standardized on MessageFormat.NET.

---

## Open items handed off to `/speckit-tasks`

- Exact EF migration plan (initial migration name, foreign-key cascade behavior per FK) — task-level.
- JWKS hosting path (`/.well-known/jwks.json`) for internal service-to-service verification — task-level.
- Admin-invitation email template copy (AR/EN) — task-level, requires editorial pass.
- Recovery-code UI copy — out of scope (spec 015 UI).

**No blocking unknowns remain.** Proceed to data-model.md.

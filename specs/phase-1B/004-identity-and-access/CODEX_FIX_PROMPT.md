# Spec 004 Identity-and-Access — Remediation Prompt for GPT-5.3-Codex

You are working in the `004-identity-and-access` branch of the Dental Commerce Platform monorepo. An earlier Codex run implemented every task in `specs/phase-1B/004-identity-and-access/tasks.md`, but a deep security review uncovered **catastrophic security bugs, major spec non-compliance, and broken user-facing workflows**. Fix every finding below. Do **not** rework anything not listed unless it is required by a listed fix.

## Ground rules

1. Every fix MUST stay within the vertical-slice layout (`services/backend_api/Modules/Identity/{Customer,Admin,Authorization,Persistence,Primitives,Seeding,Entities,Messages}`).
2. The spec is authoritative. Re-read **`specs/phase-1B/004-identity-and-access/spec.md`** (FR-001 through FR-035, SC-001 through SC-013) before you start. Flag any spec/plan contradiction instead of guessing.
3. The Constitution (`CLAUDE.md`) is supreme. Principles 4, 5, 8, 9, 24, 25, 27, 28 all bind this module.
4. **Do not weaken existing tests.** Update them to reflect the fixed behavior and add coverage for each fix.
5. When a fix requires a new EF migration, add it next to `20260422092858_Identity_Initial.cs`; do NOT modify the initial migration.
6. Never skip pre-commit hooks, never `--no-verify`, never ship plaintext secrets (passwords / OTPs / TOTP secrets / refresh tokens / invitation tokens / email verification tokens) to any log sink, response body, or telemetry stream (SC-005 is blocking).
7. Use `scripts/dev/scan-plaintext-secrets.sh` to validate log output after changes.
8. For each fix, commit in a logically coherent group with a `fix(identity,spec-004,T###):` message referencing the task ID and the finding number below.

---

## P0 — Critical security bugs (ship-blocking)

### F-01. Register endpoint leaks the email verification token in the HTTP response
**Where**: `services/backend_api/Modules/Identity/Customer/Register/Endpoint.cs:63` — `Results.Accepted(value: new RegisterAcceptedResponse(result.EmailVerificationToken!))`.
**Impact**: Any attacker who registers with a victim's email receives the raw confirmation token in the 202 payload and can activate the account themselves. This bypasses the entire email-verification surface and violates FR-007 + FR-011.
**Fix**:
- `Results.Accepted` MUST return only a stable, non-sensitive payload (e.g. `{ status: "pending_email_verification" }`). Never return the raw token in any response.
- The raw token must be dispatched to the email provider through a new `IIdentityEmailDispatcher` abstraction (stub + `NotConfigured` variant, parallel to `IOtpChallengeDispatcher`). Ship a `ConsoleEmailDispatcher` gated by `IHostEnvironment.IsDevelopment()` that writes the token only to the Serilog console sink in Dev.
- Update `RegisterAcceptedResponse` and the contract tests accordingly. Remove `EmailVerificationToken` from `RegisterHandlerResult` and pass it to the dispatcher inside the handler instead.

### F-02. OTP request endpoint leaks the raw OTP code in the HTTP response
**Where**: `services/backend_api/Modules/Identity/Customer/RequestOtp/Handler.cs:80` + `Endpoint.cs:57` — `result.DevCode!` is returned in `RequestOtpAcceptedResponse`.
**Impact**: Any attacker who can POST a phone number receives the raw OTP in the JSON response. Phone verification, password reset, step-up — all OTP-gated flows are defeated. Violates FR-019, FR-020, FR-033, SC-005.
**Fix**:
- Never return the raw OTP code from `RequestOtpHandler`. Delete `DevCode` from `RequestOtpHandlerResult`.
- Dispatch the code strictly through `IOtpChallengeDispatcher`.
- `ConsoleOtpDispatcher` may write the code to Serilog in Dev only; `NotConfiguredOtpDispatcher` must throw a `NotConfiguredException` mapped to 503 with a generic reason code (no environment leakage).
- Audit integration tests: ensure body assertions disallow any 6- or 8-digit numeric string.

### F-03. Password-reset-request and admin-invitation never deliver the token
**Where**:
- `services/backend_api/Modules/Identity/Customer/RequestPasswordReset/Handler.cs:29` — `CreateOpaqueToken()` is called inside `HashString(...)` and the raw token is discarded.
- `services/backend_api/Modules/Identity/Admin/InviteAdmin/Handler.cs:32` — identical pattern for invitation tokens.
**Impact**: Password reset and admin onboarding are **functionally broken**. A real user can never redeem the flow because no one holds the raw token. Violates FR-010, FR-022, FR-023.
**Fix**:
- Generate the raw token, hash it for storage, and dispatch the raw token via the new `IIdentityEmailDispatcher` (password reset) / same abstraction (admin invitation email) or the OTP dispatcher if the spec intends SMS for reset.
- For password-reset: keep response 202 on success AND 202 on unknown identifier (FR-011 anti-enumeration); equalize timing (see F-34).
- For admin invitations: the response MUST NOT echo the raw token; only the invitation id is acceptable.
- Add integration tests that assert the dispatcher received the raw token exactly once per issued record.

### F-04. SeedAdminCliCommand provisions super-admin with a hardcoded password in every environment
**Where**: `services/backend_api/Modules/Identity/Seeding/SeedAdminCliCommand.cs:54` — `hasher.HashPassword("AdminOnly!12345", SurfaceKind.Admin)`.
**Impact**: A production operator running `seed-admin` in Staging/Prod gets a deterministic admin password that passes every admin sign-in. Combined with F-05 (MFA tier bypass), this is a complete platform takeover. Violates FR-024pre.
**Fix**:
- Remove the hardcoded password.
- The CLI MUST read an initial password from a mandatory argument (`--initial-password` or `--initial-password-file`) and MUST refuse if no value is supplied. The password MUST meet the admin tier policy (FR-008 admin — 12+ chars, 3 classes, breach-list). Validation MUST reject weak values.
- Set `Account.Status = "pending_password_rotation"` (new state — see F-35 / state-machine update). On first admin sign-in, force a password change before issuing any session.
- Remove the `--force` flag. FR-024pre explicitly forbids reprovisioning when a super-admin exists. If the operator needs recovery, they use a separate audit-logged `reset-admin-mfa` path (already scaffolded).
- Check "existing super-admin" by JOINing `account_roles`/`roles` on `Code = 'platform.super_admin'`, not by `Surface == "admin" && MarketCode == "platform"`.
- Environment gate: refuse to run when `Environment.IsDevelopment()` (Dev uses the A1 seed framework per FR-024pre clause (a)).
- The admin created by this CLI MUST also land in `admin_mfa_factors` in `pending_confirmation` state with a server-issued provisioning token; first sign-in after password rotation forces TOTP enrollment before any privileged action. Do NOT skip MFA enrollment.

### F-05. Admin MFA tier is not enforced on sign-in
**Where**: `services/backend_api/Modules/Identity/Admin/SignIn/Handler.cs:60–73`.
**Impact**: `AdminSignInHandler` issues a full admin session whenever no confirmed TOTP factor exists. A super-admin or finance-viewer with no MFA enrolment simply bypasses MFA. Violates FR-024a, FR-024b, SC-013.
**Fix**:
- Resolve the admin's role codes before deciding MFA policy.
- If any role is in the MFA-required tier (configurable: `Identity:Mfa:RequiredRoles`, defaulting to `["platform.super_admin", "platform.finance_viewer"]`), and no active confirmed factor exists, return `412 Precondition Required` with `reasonCode = "identity.mfa.enrollment_required"` and an enrolment path. DO NOT issue a session.
- If the admin IS in the tier and a confirmed factor exists, require TOTP completion (already partly implemented).
- If the admin is OUTSIDE the tier, issue the admin session but mark it as **lacking step-up**; the step-up path (already partly present) attaches `step_up_valid_until` on demand.
- Tier membership changes MUST force re-enrolment (audit `identity.mfa.tier_changed`, flag `requires_mfa_recheck_until` on affected factors).
- Add an integration test: a super-admin with no TOTP factor receives 412 and cannot reach any `[RequirePermission]` endpoint.

### F-06. TotpSecretCodec silently returns plaintext on decrypt failure
**Where**: `services/backend_api/Modules/Identity/Admin/Common/TotpSecretCodec.cs:9–31`.
**Impact**: On any DataProtection key rotation or corrupted payload, `Decode` falls back to returning the raw bytes as the TOTP secret. An attacker who can plant raw Base32 bytes into `admin_mfa_factors.secret_encrypted` bypasses encryption entirely. Additionally, the fallback masks the real "keys are gone" error; MFA will silently accept wrong codes after a key rotation.
**Fix**:
- Remove the fallback. `Decode` MUST throw `TotpSecretUnprotectFailed` on any `Unprotect` error; the caller MUST fail closed (refuse sign-in, surface an operator-escalation error).
- Encrypted payloads MUST carry a fixed magic-bytes header + version so `Unprotect` failures are distinguishable from payload-format drift.
- Add a unit test that corrupted payloads throw and sign-in is refused (no "accidental plaintext").

### F-07. JWT signing keys are ephemeral per-process, non-configurable in the happy path
**Where**:
- `services/backend_api/Modules/Identity/Primitives/JwtIssuer.cs:175–192` (`IdentityJwtKeyMaterial.GetOrCreate`).
- `services/backend_api/Modules/Identity/IdentityModule.cs:159` — `ConfigureJwtBearer` always pulls from `IdentityJwtKeyMaterial.GetOrCreate(surface)` regardless of whether `IdentityJwtOptions.PrivateKeyPem` was configured.
**Impact**:
- In-memory ECDsa key is generated per-process. Tokens issued by instance A are rejected by instance B. Session validity is lost on every restart. Violates FR-013 and FR-017.
- Worse: `JwtIssuer` honours `configured.PrivateKeyPem` but `ConfigureJwtBearer` hardcodes the ephemeral key, so even when a PEM is configured, **validation uses the fallback key** → every well-formed token is rejected.
**Fix**:
- Delete `IdentityJwtKeyMaterial` or relegate it to test-only.
- `IdentityJwtSurfaceOptions` MUST require `PrivateKeyPem` in Staging/Production (fail-fast at startup via `IValidateOptions<T>`); Development/Test may use a file-based fallback in `infra/dev-keys/`. Add corresponding entries to `.gitignore`.
- Both `JwtIssuer.CreateSurface` and `IdentityModule.ConfigureJwtBearer` MUST consume the SAME `TokenSigningSurface` via DI (lift `TokenSigningSurface` build into an `IdentityJwtKeyProvider : IHostedService` registered as singleton; both validator and issuer resolve from it).
- Support key rotation: issue tokens with the current key's `kid`; validate against a list of acceptable keys (current + n most recent retired). Expose the JWKS at `/.well-known/jwks.json` per surface.
- Fix the `AccessTokenMinutes` default: admin surface default MUST be **5 minutes** per FR-013, not 15. Customer default remains 15.
- Integration test: two test factory instances sharing the same PEM produce cross-validating tokens.

### F-08. Duplicate-register issues an email-verification challenge on the victim's live account
**Where**: `services/backend_api/Modules/Identity/Customer/Register/Handler.cs:93–101`.
**Impact**: When the email or phone already exists, the handler (a) creates a spurious "shadow" deleted account and (b) **inserts a new `EmailVerificationChallenge` whose `AccountId` points at the existing victim's account** and returns the raw token (F-01). An attacker can therefore force-confirm a victim's email or overwrite an existing unfinished verification. Violates FR-011 and edge-case "register with existing identifier → reject without leaking … still guide legitimate owner to recovery".
**Fix**:
- On duplicate: do NOT create the shadow account. Do NOT create any row touching the existing account. Just run a constant-time dummy Argon2id hash and a dummy in-memory buffer allocation to equalize timing.
- Do NOT dispatch any token to the existing email address from the register path (silent drop is acceptable; the "legitimate owner guidance" lives in the password-reset flow and the support path, not here).
- Emit a security audit event `account.register.duplicate_rejected` with `ActorId = null`, `TargetIdentifier = hashed-email`, actor IP hash, and correlation id. Never reveal the target account id.
- Reuse the non-duplicate code path's DB write pattern for timing equalization — run the same `EmailVerificationChallenge` `Add` against a throwaway in-memory context or emit a constant-time sleep rather than divergent DB writes.

### F-09. `[RequirePermission]` / `[RequireStepUp]` MVC filters are never invoked on Minimal API endpoints
**Where**: 
- `services/backend_api/Modules/Identity/Authorization/Filters/RequirePermissionAttribute.cs` — declared as `IAsyncAuthorizationFilter` (MVC controller pipeline).
- Every endpoint in the module uses `MapPost`/`MapGet` (Minimal API). MVC filters do NOT run there.
- `services/backend_api/Modules/Identity/Admin/Common/AdminSuperAdminGuard.cs` exists as an ad-hoc replacement but is invoked manually only in a handful of endpoints (`InviteAdmin`, `RevokeInvitation`, `ChangeAdminRole`, `RevokeAdminSession`) — and it hardcodes a single permission (see F-32).
**Impact**: Any endpoint that "looks protected" in tasks.md is likely **unprotected in practice**. The attribute gives a false sense of security.
**Fix**:
- Convert both attributes to `IEndpointFilter` and register them via `.AddEndpointFilter<RequirePermissionEndpointFilter>()` on a new fluent helper (e.g. `.RequirePermission("identity.admin.invite")` extension). Matching conversion for step-up.
- Wire every admin endpoint (`InviteAdmin`, `RevokeInvitation`, `ChangeAdminRole`, `ListAdminSessions`, `RevokeAdminSession`, `ListAdminMfaFactors`, `ResetAdminMfa`, `RotateTotp`) to the appropriate permission code.
- Wire every customer endpoint that must be authenticated (`Me`, `SetLocale`, `ListSessions`, `RevokeSession`, `ChangePassword`, `SignOut`, `RefreshSession`) to `.RequireAuthorization(AuthenticationSchemes = "CustomerJwt")` AND `.RequirePermission("identity.customer.self")` (seed a permission for customer self-serve).
- Add integration tests verifying that every endpoint without the required permission returns 403 and emits a deny row.

### F-10. `authorization_audit` table is dead schema
**Where**: `services/backend_api/Modules/Identity/Authorization/AuthorizationAuditEmitter.cs` only publishes to `IAuditEventPublisher` (generic audit log). `IdentityDbContext.AuthorizationAudits` is never written to.
**Impact**: FR-027 requires "log the denial with actor, resource, and required permission" at the dedicated audit boundary. Spec 003's generic audit log is a cross-cutting sink, not the dedicated denial index that downstream specs 005–013 expect to query.
**Fix**:
- `AuthorizationAuditEmitter.EmitDecisionAsync` MUST insert an `AuthorizationAudit` row (via a scoped `IdentityDbContext`) in addition to publishing the cross-cutting audit event.
- Wrap the DB insert and the audit publish in the same transaction if the audit publisher supports transactional participation; otherwise insert first, publish on success, and swallow-publish failures into telemetry (not into the user response).
- Add an integration test `AuthorizationAudit_EveryDenyHasRow` that actually queries `identity.authorization_audit`.
- Also populate `rate_limit_events` on rate-limit rejection — see F-15 fix.

### F-11. `RefreshTokenRevocationStore.RevokeAsync` swallows DB failures silently
**Where**: `services/backend_api/Modules/Identity/Primitives/RefreshTokenRevocationStore.cs:88–109`.
**Impact**: When a user calls "sign-out" or "revoke session", the insert into `revoked_refresh_tokens` can fail (transient DB issue, uniqueness conflict on a concurrent path). The method logs a warning and updates the in-memory cache only. On next startup the token appears active again → revoke is effectively reversible. Violates FR-017.
**Fix**:
- Propagate DB failure. The caller MUST surface a 500/503 to the user so the client retries; do not update the in-memory cache when the DB write fails.
- Make revoke idempotent via `INSERT ... ON CONFLICT DO NOTHING` (already present) and a follow-up `SELECT` to confirm persistence before updating the cache.
- Add a retry-policy wrapper (`Polly`) — 3 attempts with jitter — before surfacing the error.

---

## P1 — High-severity functional / compliance bugs

### F-12. Customer sign-in only accepts email (phone sign-in missing)
**Where**: `services/backend_api/Modules/Identity/Customer/SignIn/Handler.cs:19–24`.
**Impact**: FR-012 requires "email-or-phone plus password on the customer surface". Implementation looks up only by `EmailNormalized`.
**Fix**: Accept an `Identifier` field. Detect phone vs email (normalize via `PhoneNormalizer` if it looks like phone). Look up `Surface == "customer" AND (EmailNormalized = :id OR PhoneE164 = :id)`. Preserve constant-time behavior between identifier types.

### F-13. Customer progressive lockout is a single 15-minute tier, not 4 progressive tiers
**Where**: `services/backend_api/Modules/Identity/Customer/SignIn/Handler.cs:89–102`.
**Impact**: FR-018 requires progressive lockout at 5 consecutive failures each tier: 1-min → 5-min → 30-min → admin-unlock. Implementation: single 15-min lockout after 5 failures; no admin-unlock state.
**Fix**:
- Extend `LockoutState` with a `tier` counter (0..4) and a `cooldown_index` column.
- Progression: +5 fails since tier's `first_failed_at` advances the tier; tier 4 sets `requires_admin_unlock = true` and a `locked_until = null` flag that only an admin endpoint can reset (`identity.admin.unlock`).
- Migration required.
- Update lockout tests and add explicit tier-progression tests.

### F-14. Admin access token TTL default is 15 min instead of 5 min
**Where**: `services/backend_api/Modules/Identity/Primitives/JwtIssuer.cs:172` (`AccessTokenMinutes = 15`).
**Impact**: FR-013 requires admin access = 5 min. Fallback grants 3× the allowed window.
**Fix**: Differentiate defaults per surface: customer = 15, admin = 5. Add `AdminAccessTokenMinutes` and `CustomerAccessTokenMinutes` separately, or fail startup if not explicitly configured in Staging/Prod.

### F-15. Rate-limit policies do not match spec thresholds
**Where**: `services/backend_api/Modules/Identity/Primitives/RateLimitPolicies.cs`.
| Scope | Spec (FR-018/019) | Current policy | Status |
|---|---|---|---|
| Customer sign-in per IP | 20/hour | 10/15-min (= 40/hr) | Wrong |
| Admin sign-in per IP | 10/hour | 5/15-min (= 20/hr) | Wrong |
| Customer OTP per identifier | 3/10-min | 5/hour | Wrong |
| Admin OTP per identifier | 2/hour | 3/hour | Wrong |
| Per-challenge attempt cap | 3 | Entity default 5 | Wrong (see F-27) |

**Fix**:
- Align permit windows to the exact numbers in FR-018/FR-019.
- Partition the customer-OTP and admin-OTP limits by **identifier** (phone/email), not subject+IP. For the OTP request endpoint, the client is anonymous; the limiter key MUST be `sha256(hmac(secret, phone/email))` derived from the request body.
- Customer and admin sign-in limits stay keyed by `IP + normalized identifier`.
- Password-reset limit should be per-identifier-OR-per-IP, matching industry norms.
- On rejection, insert a `RateLimitEvent` row (via a small `IRateLimitAuditSink`) so the `rate_limit_events` table is actually populated.

### F-16. OTP request: no per-identifier rate limit, no prior-challenge invalidation
**Where**: `services/backend_api/Modules/Identity/Customer/RequestOtp/Handler.cs:40–58`.
**Impact**: Attacker creates unbounded pending challenges for the same number. Spec says per-identifier cap; implementation relies on endpoint limiter alone (which is IP-keyed).
**Fix**:
- Before insert: invalidate any prior `pending` challenge for the same `(Purpose, DestinationHash, AccountId?)` tuple → set to `superseded`.
- Enforce per-identifier rate limit in the handler too (defense in depth), reading from `otp_challenges` count in the last N minutes.
- Populate `MaxAttempts = 3` (not default 5 — see F-27).

### F-17. OTP challenges addressable by challenge-id alone
**Where**: `services/backend_api/Modules/Identity/Customer/VerifyOtp/Handler.cs:20`.
**Impact**: Any leak of `challengeId` (server logs, browser history, error surface) lets an attacker brute-force just the 6-digit code. Challenges should bind to the identifier the code was sent to.
**Fix**:
- `VerifyOtpRequest` MUST include the identifier (phone/email). Verify `challenge.DestinationHash == HashString(normalizedIdentifier)` in constant time alongside the code hash.
- If client already holds an `AccountId` (signed-in step-up flow), bind challenge to `account_id` and reject cross-account verification.

### F-18. Refresh endpoint does an O(n) scan across top-4096 active/consumed tokens
**Where**: `services/backend_api/Modules/Identity/Customer/RefreshSession/Handler.cs:19–34` and the analogous admin path.
**Impact**:
- Linear scan on every refresh: Postgres fetch of up to 4,096 rows + in-proc SHA256 per candidate. With 10k+ active refresh tokens (easy on a real platform), legitimate refresh tokens fall outside the top-4096 and **refresh fails**.
- CPU amplifier for DoS.
- Incompatible with spec's "silently refresh without re-prompting" (FR-014).
**Fix**:
- Redesign the refresh-token storage: split into two fields — a server-stable `token_id` (random 128-bit, indexed & unique) and a `token_secret_hash` (HMAC-SHA256 of the secret with a server-side pepper). The refresh token string becomes `{token_id}.{token_secret}`.
- Lookup: `WHERE token_id = :id` — O(log n). Then `CryptographicOperations.FixedTimeEquals(HMAC(secret, pepper), stored_hash)`.
- Drop `TokenSalt` per row.
- Migration: add `token_id`, `token_secret_hash`; retire `token_hash` and `token_salt` behind a guarded read path for any in-flight tokens, or force a re-login after deploy (acceptable on pre-launch).
- Apply the same treatment to `email_verification_challenges` and `password_reset_tokens`.
- Drop the `Take(4096)` / `Take(256)` scans everywhere.

### F-19. ConfirmEmail scans 256 most recent challenges
**Where**: `services/backend_api/Modules/Identity/Customer/ConfirmEmail/Handler.cs:19–25`.
**Fix**: Same redesign as F-18 — `token_id.token_secret` with direct lookup.

### F-20. Revocation cache stays stale for up to 15 seconds on user-initiated revokes
**Where**: `services/backend_api/Modules/Identity/Primitives/RefreshRevocationCacheWorker.cs`, `Customer/SignOut/Handler.cs`, `Customer/CompletePasswordReset/Handler.cs`, `Customer/RevokeSession/Handler.cs`, admin equivalents.
**Impact**: Spec SC-007 requires 60-second propagation; current code often bypasses `RefreshTokenRevocationStore.RevokeAsync` and writes to `RevokedRefreshTokens` directly, leaving the bloom filter and in-proc `_snapshot` stale until the next worker cycle.
**Fix**:
- Every revoke path MUST go through `IRefreshTokenRevocationStore.RevokeAsync(tokenHash, reason, actorId, ct)`. Remove direct `dbContext.RevokedRefreshTokens.Add(...)` calls in handlers.
- For session-level revoke (fan-out over all of a user's refresh tokens), expose a bulk variant: `RevokeBySessionAsync(sessionId, reason, actorId, ct)` that loops + updates the cache atomically.
- Store the in-process revocation set behind a `ConcurrentDictionary<byte[], byte>` (custom byte-array equality comparer) — simpler and faster than `HashSet + lock`.

### F-21. Admin invitation does not validate role scope; no duplicate-check; no dispatch
**Where**: `services/backend_api/Modules/Identity/Admin/InviteAdmin/Handler.cs`.
**Impact**:
- Any role whose code exists (including customer roles) can be attached, resulting in admin accounts with customer permissions.
- No uniqueness on `(email_normalized, status='pending')` — invitation spam.
- Raw token discarded (F-03).
**Fix**:
- Validate `role.Scope == "platform"` AND `role.System` is true (admin roles are system roles).
- Reject if any `AdminInvitation` with `email_normalized = :email AND status = 'pending' AND expires_at > now()` exists; require the inviter to revoke-then-reinvite.
- Dispatch raw token via `IIdentityEmailDispatcher`.
- Add integration test: invitation to a customer role is rejected with `identity.invitation.invalid_role_scope`.

### F-22. No deactivation enforcement / no account-status gating on sign-in and refresh
**Where**: `Customer/SignIn/Handler.cs`, `Admin/SignIn/Handler.cs`, `Customer/RefreshSession/Handler.cs`, admin refresh equivalent (if any).
**Impact**: FR-024 + edge cases require deactivated accounts to fail closed across surfaces, mid-session too. Current handlers don't check `account.Status` at all.
**Fix**:
- On sign-in: reject if `account.Status NOT IN ("active", "pending_password_rotation")`. Specifically reject `deleted`, `disabled`, `locked`, `pending_email_verification` (with the right localized reason code each).
- On refresh: reload the account; if not `active`, revoke the whole session + refresh chain.
- Admin refresh path should mirror this.

### F-23. RefreshSession does not consult session status, revocation cache, or account status
**Where**: `Customer/RefreshSession/Handler.cs:45–53`.
**Fix**:
- After resolving the session by id, refuse if `session.Status != "active"`.
- Check `ITokenRevocationCache.IsRevokedAsync(matchedToken.TokenHash, ct)` → fail closed on hit.
- Device binding: store a `client_fingerprint_hash` (HMAC of UA + IP) on the session row at issuance. On refresh, compare the current fingerprint in constant time; on mismatch emit `identity.refresh.fingerprint_mismatch` and revoke the chain (spec edge case: "refresh token replayed from different device must be refused and the chain invalidated").

### F-24. `Account` entity is missing spec-mandated fields
**Where**: `services/backend_api/Modules/Identity/Entities/IdentityEntities.cs` (Account class).
**Impact**:
- FR-004: **professional_verification_status** (enum: unverified / pending / verified / rejected / expired) + `last_updated_at` — missing.
- FR-005: optional **company_account_ref** (Guid?) + role-shape capable of buyer/approver — missing.
- FR-001a: **market_code** is present, but no immutability enforcement after account activation (spec says "immutable once active except via audited admin action").
**Fix**:
- Add the columns + migration.
- `professional_verification_status` defaults to `"unverified"`, `professional_verification_status_updated_at` nullable.
- `company_account_id` Guid? with a reserved FK target that spec 021 will define (leave FK open for now; add an index).
- Enforce `market_code` immutability via a check in `IdentitySaveChangesInterceptor` or a DB trigger: if `account.Status == "active"` and `Entry.OriginalValues["market_code"] != Entry.CurrentValues["market_code"]`, throw unless an `IAdminMarketChangeContext` is active.

### F-25. OTP verification purpose handling incomplete, wrong audit semantics
**Where**: `services/backend_api/Modules/Identity/Customer/VerifyOtp/Handler.cs:83–140`.
**Impact**:
- `registration_phone` and `signin_customer` are the only supported purposes. `password_reset_phone` and `step_up_customer` unsupported.
- On success the handler always emits `"phone.verified"` audit action, even for sign-in purpose (wrong semantics).
- `Attempts += 1` on success (increments attempts even for the successful attempt, poisoning retry analytics).
- Does not transition `account.Status` from `pending_phone_verification` → `active` when phone verification is the last prerequisite.
**Fix**:
- Switch on `challenge.Purpose`: `registration_phone`, `signin_customer`, `password_reset_phone`, `step_up_customer`, etc. Audit actions: `phone.verified`, `customer.signin.succeeded`, `password_reset.phone_verified`, `step_up.passed`.
- Do not increment `Attempts` on a successful verify.
- After success, if the account's only outstanding verification was this one, transition `Status = active` via `AccountStateMachine`.
- Add tests for every purpose.

### F-26. Password-reset completion: missing min-length check, no audit, no transaction, concurrent-use race
**Where**: `services/backend_api/Modules/Identity/Customer/CompletePasswordReset/Handler.cs`.
**Fix**:
- Validate min-length 10 + breach list (already) + admin surface would require FR-008 admin tier (split paths if admin reset ever ships).
- Wrap password swap + token completion + session revocations in `dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable)`.
- Guard concurrent consumption via an optimistic-concurrency token (`xmin` on Postgres via Npgsql `UseXminAsConcurrencyToken`) OR a conditional update `UPDATE password_reset_tokens SET status='completed' WHERE id=:id AND status='pending' RETURNING ...` — only the winner proceeds.
- Emit audit `password.reset_completed` with actor=account, correlation id.
- Update revocation cache via `IRefreshTokenRevocationStore.RevokeAsync` (F-20).

### F-27. OTP per-challenge attempt cap is 5 in code, spec requires 3
**Where**: `services/backend_api/Modules/Identity/Entities/IdentityEntities.cs:71` — `MaxAttempts = 5`.
**Fix**: Default `MaxAttempts = 3` for both surfaces per FR-019. Issue admin OTPs with 8-digit code / 3-min expiry (`CodeLength = 8`, `ExpiresAt = now + 3min`). Issue customer OTPs with 6-digit / 5-min expiry (already). Add differentiation in `RequestOtpHandler` by surface.

### F-28. `IdentitySaveChangesInterceptor` emits a generic, actor-less audit event for every row change
**Where**: `services/backend_api/Modules/Identity/Persistence/IdentitySaveChangesInterceptor.cs`.
**Impact**:
- Emits `identity.added` / `identity.modified` / `identity.deleted` with `ActorId = SystemActorId` (placeholder) and null before/after state for **every** entity — `AuthorizationAudit`, `RateLimitEvent`, `AdminMfaReplayGuard`, etc. Audit log becomes noise-dominated.
- Real audit semantics (FR-021 events like `account.created`, `email.verified`, `admin.role.changed`) are emitted separately by handlers; the interceptor's events duplicate these with worse fidelity.
- Actor is never resolved from `HttpContext`.
**Fix**:
- Remove the generic audit emission in the interceptor. Keep the interceptor ONLY for `UpdatedAt` stamping (and soft-delete propagation, if any).
- Every handler is already responsible for explicit audit events — tighten those to cover FR-021's 14 events exhaustively.
- Add a missing-event test: every FR-021 event exercised in integration tests MUST produce a corresponding row in `audit_log_entries`.

### F-29. Permission claims baked into the JWT prevent "next request" propagation of role changes
**Where**: `Customer/Common/CustomerAuthSessionService.cs:81–103` and admin equivalent.
**Impact**: FR-028 requires role/permission changes to take effect "no later than the next request after propagation". With permission claims embedded in a 15-min access token, a revoked admin permission persists until the next token refresh.
**Fix (choose one, document in `research.md`)**:
- **Option A (preferred)**: keep only a `permission_version` claim; resolve the effective permission set from DB per-request (cached in `IMemoryCache` for 5 seconds keyed by `(account_id, permission_version)` with cache-invalidation on role change).
- **Option B**: When a role change lands, mark every active session's refresh token `needs_refresh = true`; at refresh time the server consults the latest DB state. Access tokens within their 5-minute (admin) / 15-minute (customer) window continue to honor the old permissions — document this explicitly in the spec's Assumptions.
- The current state is neither option and is **non-compliant**.

### F-30. `AdminSuperAdminGuard` hardcodes one permission for all privileged operations
**Where**: `services/backend_api/Modules/Identity/Admin/Common/AdminSuperAdminGuard.cs:18` — always `"identity.admin.session.manage"`.
**Fix**:
- Replace with a parameterized `RequireAdminPrivilegedAsync(HttpContext, permissionCode, requireStepUp)` helper.
- Each admin endpoint supplies its own permission code: `identity.admin.invite`, `identity.admin.invitation.revoke`, `identity.admin.role.change`, `identity.admin.session.revoke`, `identity.admin.mfa.reset`, etc.
- Seed the new permissions in `IdentityReferenceDataSeeder.cs` and map them to `platform.super_admin`.
- Drop the implicit "user must be super-admin" DB lookup (policy evaluator + permission set is authoritative); "super_admin" becomes just the role that holds all `identity.admin.*` permissions.

### F-31. `AdminPartialAuthTokenStore` + `AdminMfaChallengeStore` are in-process singletons
**Where**: `services/backend_api/Modules/Identity/Admin/Common/AdminEphemeralStores.cs`.
**Impact**: Not durable (restart loses flow), not distributed (horizontal scale breaks MFA flow), no attempt cap on MFA challenges.
**Fix**:
- Move both stores to Postgres-backed tables (`admin_partial_auth_tokens`, `admin_mfa_challenges`) with `(id, account_id, expires_at, consumed_at)`. Bind challenge on `TryGet` to include `FixedTimeEquals` on the challenge id.
- Add an `attempts` counter to MFA challenges; after `MaxAttempts = 3` failures, mark the challenge `exhausted` and require a new sign-in.
- Consume the challenge on successful MFA (set `consumed_at`).
- Re-use the existing `IRefreshTokenRevocationStore` pattern for short cache reads if hot enough.

---

## P2 — Medium-severity correctness / hygiene

### F-32. Register endpoint blocks the thread for 3 seconds with `Thread.SpinWait` after every response
**Where**: `services/backend_api/Modules/Identity/Customer/Register/Endpoint.cs:66–86` — 3-second floor with busy-wait tail.
**Impact**: Anti-enumeration timing pad is way too aggressive AND uses thread-blocking SpinWait in an async pipeline.
**Fix**:
- Reduce minimum to 350–500 ms (enough to swallow Argon2id jitter).
- Replace `Thread.SpinWait` with `await Task.Delay(remaining, ct)` only. No spin.
- Apply the same budget to password-reset-request, otp-request, sign-in paths.

### F-33. No audit events for sign-in success/failure, lockout, admin session revocation, MFA events
**Where**: All sign-in / MFA handlers.
**Impact**: FR-021 specifies 14 structured events; current coverage ≤ 6.
**Fix**: Add audit emissions for:
- `customer.signin.succeeded`, `customer.signin.failed`, `customer.lockout_triggered`, `customer.lockout_cleared`
- `admin.signin.succeeded`, `admin.signin.failed`, `admin.lockout_triggered`
- `admin.mfa.enrolment_started`, `admin.mfa.enrolment_confirmed`, `admin.mfa.verification_succeeded`, `admin.mfa.verification_failed`, `admin.mfa.recovery_code_issued`, `admin.mfa.recovery_code_consumed`, `admin.mfa.recovery_code_regenerated`, `admin.mfa.reset_by_super_admin`
- `admin.stepup.issued`, `admin.stepup.passed`, `admin.stepup.failed`
- `identity.permission_granted`, `identity.permission_revoked`, `admin.account.deactivated`
- `rate_limit.rejected` per surface
Each event MUST include `actor_id`, `target_id` (where applicable), `surface`, `correlation_id`, before/after snapshots on state changes.

### F-34. Anti-enumeration timing between "identifier-exists" and "identifier-doesn't-exist" diverges
**Where**: All sign-in, password-reset-request, OTP-request handlers.
**Fix**:
- Introduce a `ConstantTimeOperation.EqualizeAsync(Func<Task>, TimeSpan budget)` helper and wrap each identity lookup path in it.
- The non-existent branch MUST perform a dummy Argon2id hash AND a dummy DB read (`SELECT 1 FROM identity.accounts LIMIT 1`) to match the existing-account branch's observable latency profile.
- Add SC-011 enforcement: add `EnumerationResistanceTests` expanding coverage to recovery + login flows (not just registration), with 200-sample p95 assertions.

### F-35. `AccountStateMachine` lacks a `pending_password_rotation` state used by F-04
**Where**: `services/backend_api/Modules/Identity/Primitives/StateMachines/AccountStateMachine.cs`.
**Fix**: Add state + transitions:
- `pending_password_rotation` → `Active` (on forced password change)
- `Active` → `pending_password_rotation` (on admin-initiated password rotation demand; spec 1D concern, leave wired but unreachable here)
- Add transition unit tests.

### F-36. Client IP hash uses unsalted SHA256
**Where**: `CustomerAuthSessionService.cs:114`, `AdminAuthSessionService.cs:106`.
**Impact**: SHA256 of an IP is trivially rainbow-tabled (2^32 IPv4 inputs). The hash provides no meaningful privacy.
**Fix**: HMAC-SHA256 with a per-environment pepper (configured in `Identity:ClientSecurity:IpPepper`, ≥ 32 random bytes). Use the same helper for `destination_hash` in OTP challenges.

### F-37. Custom bloom filter `Math.Abs(int.MinValue)` is undefined (returns `int.MinValue`)
**Where**: `RefreshTokenRevocationStore.cs:185–187`, `BreachListChecker.cs:110–113`.
**Fix**: Use `(uint)BitConverter.ToUInt32(...) % (uint)_size` or `Math.Abs(x & int.MaxValue)`. Better: replace custom bloom with `Kwaffy.BloomFilter` NuGet or a vetted implementation.

### F-38. `CustomerAuthSessionService.ResolvePermissionClaims` does sync DB calls in an async path
**Where**: `CustomerAuthSessionService.cs:81–103`.
**Fix**: Make async, return `Task<IReadOnlyCollection<Claim>>`, propagate up to `CreateForNewSession`/`CreateForExistingSession`.

### F-39. `AdminMfaReplayGuard` grows unbounded; `RevokedRefreshTokens` grows forever
**Fix**:
- Add a `BackgroundService` that purges `admin_mfa_replay_guard` rows older than 10 minutes (RFC 6238 window) and `revoked_refresh_tokens` rows where `revoked_at < now() - interval '90 days'` (refresh TTL + safety).
- Purge is idempotent; run every 15 minutes.
- Index `(revoked_at)` and `(observed_at)` respectively.

### F-40. Connection string fallback is hardcoded; duplicate `NpgsqlDataSource` in `RefreshTokenRevocationStore`
**Where**: `IdentityModule.cs:57`, `RefreshTokenRevocationStore.cs:23`.
**Fix**:
- Centralize the connection string resolution in `Configuration.AddLayeredConfiguration` (A1). Fail-fast if unset in any environment but Test.
- Reuse a single `NpgsqlDataSource` registered in DI (`services.AddSingleton<NpgsqlDataSource>`) across the revocation store and EF.

### F-41. Password-reset token expiry 20 min; email-verification token expiry 15 min
**Fix**: Password-reset tokens are one-shot, delivered asynchronously — extend to 60 min (industry norm). Email-verification: extend to 24 hours for registration flow (users don't always click immediately). Document in `research.md`.

### F-42. Test harness race on environment variables
**Where**: `IdentityTestFactory.cs:29–30`, `43`.
**Fix**: Do NOT mutate `Environment.SetEnvironmentVariable`. Use `IConfiguration` overrides already wired in `ConfigureAppConfiguration`. Remove the env-var round-trip.

### F-43. `Recovery codes` never accepted on the MFA completion path
**Where**: `services/backend_api/Modules/Identity/Admin/CompleteMfaChallenge/Handler.cs`.
**Fix**: When `request.Code` length != 6 OR does not match TOTP, attempt recovery-code consumption via Argon2id-verify against each `RecoveryCodesHash[i]` in constant time; mark consumed on success. Emit `admin.mfa.recovery_code_consumed`. Return 200 with the same `AdminAuthSessionResponse`.

### F-44. No test coverage for AR/EN parity across new messages
**Where**: `tests/Identity.Tests/Unit/IdentityMessagesCompletenessTests.cs`.
**Fix**: After you add the new reason codes (F-04, F-05, F-13, F-21, F-25, F-33, F-43), update the test assertions so that every `identity.*` reason code emitted anywhere in the module has both `ar` and `en` entries. Fail the test if any key is missing in either locale.

---

## Acceptance criteria (what "done" looks like)

1. `dotnet build services/backend_api/ -warnaserror` is clean.
2. `dotnet format services/backend_api/` is a no-op.
3. `dotnet test services/backend_api/ --filter Category!=Skip` is fully green. Every newly added test is not skipped.
4. `scripts/dev/scan-plaintext-secrets.sh` exits 0 (no plaintext passwords, OTPs, TOTP secrets, refresh tokens, email-verification tokens, or invitation tokens in any log sink or HTTP response).
5. `scripts/dev/identity-audit-spot-check.sh` exits 0 with all FR-021 + FR-024e events present in the integration suite.
6. A single fresh `docker compose up` followed by `dotnet ef database update` applies cleanly from scratch.
7. The `openapi.identity.json` artifact is regenerated and every response schema change is reflected.
8. Every fix has a unit or integration test that would fail on the pre-fix code and passes post-fix.

## Workflow

1. Branch from `004-identity-and-access` as-is (do NOT rebase over main). Create logically-grouped commits.
2. Update `tasks.md` status markers only if you complete a remediation task end-to-end (add a **Remediation-####** line; do not unmark the original T### checkboxes unless the remediation invalidates their output).
3. Open a single PR titled `fix(spec-004): security + compliance remediation (F-01..F-44)`. Paste the new fingerprint into the PR description (`scripts/compute-fingerprint.sh`).
4. When you are uncertain whether a fix conflicts with the spec, **stop and surface the contradiction** rather than silently deviating. Post the open question in the PR description under a `### Open questions` heading.

## Do not touch (out of scope for this remediation)

- `services/backend_api/Modules/AuditLog/**`
- `services/backend_api/Modules/Storage/**`
- `services/backend_api/Modules/Pdf/**`
- `services/backend_api/Modules/Observability/**`
- `apps/admin_web/**`, `apps/customer_flutter/**` (these come in Phase 1C)
- Any file under `specs/phase-1B/005-*..013-*`
- The impeccable skills tree (CLAUDE.md D1 rule — spec 004 is backend-only)

Stay tight. Each fix above is bounded; don't expand scope or refactor unrelated code. If a fix touches a shared primitive that other specs consume, note the surface change in `CHANGELOG.md` under the identity module.

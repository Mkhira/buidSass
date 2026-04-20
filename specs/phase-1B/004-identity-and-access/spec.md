# Feature Specification: Identity and Access

**Feature Branch**: `004-identity-and-access`
**Created**: 2026-04-20
**Status**: Draft
**Input**: User description: "Phase 1B spec 004 · identity-and-access — customer + admin authentication, OTP, RBAC role framework (per docs/implementation-plan.md §Phase 1B)"

**Phase**: 1B — Core Commerce
**Depends on**: Phase 1A (specs 001, 002, 003 at DoD — shared kernel, audit-log, storage, PDF, localization, contracts pipeline)
**Enables**: 005 (catalog), 009 (cart eligibility), 010 (checkout restricted-product gate), 015 (admin foundation), 020 (verification), 025 (notifications OTP channel)
**Constitution anchors**: Principles 3 (auth gate before checkout), 4 (AR + EN), 6 (multi-vendor-ready role model), 8 (restricted-product eligibility hook), 20 (admin app separation), 24 (state machines), 25 (audit on role changes)

---

## Clarifications

### Session 2026-04-20

- Q: Identifier uniqueness rule — is email/phone unique globally, per-market, or composite? → A: Email globally unique and phone globally unique, each independent of market code.
- Q: Account lockout recovery — time unlock, proof-only, or progressive? → A: Progressive — time unlock on first lockout; proof-of-control (password reset or fresh OTP step-up) on re-lockout within a rolling window.
- Q: Data residency for identity records across EG and KSA? → A: Single-region per ADR-010 (Azure Saudi Arabia Central) for both markets, partitioned logically by market_code.
- Q: Concurrent-session policy per surface? → A: Customer surface allows unlimited concurrent sessions; admin surface allows a single active session per admin (newest login revokes older sessions).
- Q: Customer self-service account deletion / right to erasure at launch? → A: Scaffold the deletion-request state and anonymization operation now; defer the customer-facing self-service UX to Phase 1.5.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Customer Registration with Verified Contact (Priority: P1)

A prospective buyer (dentist, lab, student, or consumer) in Egypt or KSA opens the mobile app or web storefront, provides an email or phone number plus a password, and completes a one-time passcode (OTP) challenge that confirms control of the contact channel. Once confirmed, the account is usable for browsing personalized content and progressing to checkout.

**Why this priority**: Without a registered, contact-verified customer account no downstream commerce flow (cart persistence, checkout, order, invoice, verification, B2B, support) can function. This story is the minimum viable slice — registration + contact verification — that unlocks every other Phase 1B spec.

**Independent Test**: A fresh user can (a) register with email or phone, (b) receive and enter an OTP, (c) reach an authenticated session, and (d) be rejected from checkout only by the session/authorization gate, never by missing identity. Testable end-to-end against the backend alone (no UI dependency) by calling the registration, OTP-send, OTP-verify, and session endpoints in sequence.

**Acceptance Scenarios**:

1. **Given** a visitor with no account, **When** they submit a valid email + password + market (EG or KSA), **Then** the system creates an unverified account, sends an OTP to the email, and returns a pending-verification state.
2. **Given** a visitor with no account, **When** they submit a valid phone number + password + market, **Then** the system creates an unverified account, sends an OTP to the phone, and returns a pending-verification state.
3. **Given** a pending-verification account, **When** the correct OTP is submitted within the validity window, **Then** the account becomes active and an authenticated session is returned.
4. **Given** a pending-verification account, **When** an incorrect OTP is submitted three times, **Then** the OTP is invalidated and the user must request a new one.
5. **Given** an email or phone already tied to an active account, **When** a new registration is attempted with the same identifier, **Then** the system rejects the request with a clear, localized (AR + EN) message and does not leak whether the identifier exists (uniform response) unless the user proves control via password-reset.

---

### User Story 2 — Returning Customer Login and Session Management (Priority: P1)

A returning customer authenticates with email-or-phone + password, receives a short-lived access token plus a refresh token, and can silently refresh the session while the app is in use. The customer can log out from the current device and can remotely revoke other active sessions from a future account screen.

**Why this priority**: Session reliability is the second non-negotiable. Every authenticated action in every later spec reads the session, checks permissions, and logs the actor. Token refresh and revocation are what make the authenticated experience safe on mobile (long-lived sessions) and compliant with Principle 25 (auditable actor identity).

**Independent Test**: With a single seeded active account, a client can (a) log in, (b) call an authenticated echo endpoint with the access token, (c) refresh once the access token expires, (d) revoke a named session, and (e) observe that the revoked refresh token no longer mints new access tokens. Fully testable without any other domain.

**Acceptance Scenarios**:

1. **Given** an active account with correct credentials, **When** the customer logs in, **Then** the system returns an access token (short-lived) and a refresh token (longer-lived) bound to a session record.
2. **Given** a valid, non-expired refresh token, **When** the client requests a token refresh, **Then** the system issues a new access token and rotates the refresh token.
3. **Given** a revoked refresh token, **When** the client requests a token refresh, **Then** the system rejects the request and requires a fresh login.
4. **Given** five consecutive failed login attempts for the same identifier, **When** a sixth attempt is made, **Then** the account is temporarily locked for a cool-down window and the actor sees a localized lock message without disclosure of underlying cause.
5. **Given** an authenticated session, **When** the customer logs out, **Then** the current refresh token is revoked and subsequent refresh calls with it fail.

---

### User Story 3 — Password Reset via Contact Channel (Priority: P2)

A customer who has forgotten their password requests a reset from the login screen using their email or phone. The system sends a single-use, time-boxed reset token to that channel. The customer submits a new password that meets policy, and the reset token is consumed.

**Why this priority**: Required for any realistic go-live but not blocking the first round of authenticated testing. Phased after P1 because registration + login form the core bootstrap path.

**Independent Test**: Given a seeded active account, the password-reset-request → reset-token-verify → new-password-set → login-with-new-password sequence is fully testable with no other domain involved.

**Acceptance Scenarios**:

1. **Given** an active account, **When** the customer requests a password reset by email, **Then** the system sends a single-use reset link/token to that email, regardless of whether the account is verified (but never reveals non-existence of the account).
2. **Given** a valid, unused reset token within its validity window, **When** the customer submits a new password meeting the policy, **Then** the password is updated, the token is consumed, and all existing sessions for that account are revoked.
3. **Given** an expired or already-used reset token, **When** the customer submits a new password, **Then** the system rejects the attempt with a localized message and requires a fresh reset request.

---

### User Story 4 — Admin Authentication on a Separate Surface (Priority: P1)

An admin operator (catalog, inventory, verification, support, finance, or a future role) signs in through the separate admin web application using credentials issued by an existing admin. The admin session is isolated from the customer surface: different host/app, different cookie/token scope, and stricter session policy (shorter idle timeout, mandatory re-auth for sensitive actions).

**Why this priority**: Constitution Principle 20 requires a separate admin app, and every later admin-facing spec (015 admin-foundation, 016 admin-catalog, 017 admin-inventory, 018 admin-orders, 019 admin-customers, 020 verification, 022 reviews-moderation, 023 support-tickets, 024 cms) depends on admin auth existing in Phase 1B.

**Independent Test**: Given a seeded admin user, the admin-login → admin-authenticated-echo → admin-logout sequence succeeds only on the admin surface. The same credentials against the customer surface fail. A customer token presented to an admin endpoint is rejected.

**Acceptance Scenarios**:

1. **Given** a seeded admin account with correct credentials, **When** the admin authenticates on the admin surface, **Then** the system returns an admin session with an admin-scoped token and a shorter idle-timeout policy than the customer session.
2. **Given** an admin session idle beyond the admin idle-timeout, **When** the admin attempts a protected action, **Then** the system forces re-authentication before proceeding.
3. **Given** a valid customer token, **When** it is presented to an admin endpoint, **Then** the system rejects it with an authorization error and records the attempt as an auditable security event.
4. **Given** an admin marked as disabled, **When** they attempt to log in, **Then** the system rejects the attempt with a localized message and records the event.

---

### User Story 5 — Role-Based Access Control Framework (Priority: P1)

Every admin user carries one or more roles; each role is a named bundle of permissions; each protected endpoint declares the permission it requires; the system rejects any call whose bearer lacks the required permission. Roles and permissions are seeded for the minimum launch admin matrix (catalog, inventory, orders, customers, verification, support, finance, super-admin) and can be expanded without code changes to endpoints.

**Why this priority**: RBAC is the load-bearing authorization primitive for every admin spec in Phases 1B–1E. Without it, each later spec would have to invent its own gate, violating Principle 28 (explicit, structured, low-ambiguity specs).

**Independent Test**: With seeded roles and a seeded set of protected fixture endpoints, the role × endpoint matrix can be exercised exhaustively: a user in role R calling an endpoint requiring permission P succeeds iff R grants P. Fully testable in isolation via a fixture harness that registers synthetic endpoints.

**Acceptance Scenarios**:

1. **Given** a seeded role that grants permission `catalog.read`, **When** an admin in that role calls an endpoint requiring `catalog.read`, **Then** the call succeeds.
2. **Given** the same seeded role, **When** that admin calls an endpoint requiring `catalog.write`, **Then** the call is rejected with an authorization error and the attempt is logged.
3. **Given** a role change applied to an admin, **When** that admin's next request arrives, **Then** the new permission set takes effect no later than their next token refresh, and the role change is written to the audit log with actor, target, before, and after values.
4. **Given** a super-admin role, **When** the super-admin acts on any protected resource, **Then** the action is allowed but still written to the audit log with full context.

---

### User Story 6 — Phone OTP Service Abstraction (Priority: P2)

The system can send phone OTPs through a provider abstraction so that, in Phase 1B, OTPs work against a deterministic test provider (for dev + CI), and in Phase 1E spec 025 the real SMS provider can be plugged in without changing any caller. Rate limits and replay protection apply regardless of provider.

**Why this priority**: Needed for P1 registration story on the phone channel, but the real provider is explicitly deferred to 1E. The abstraction has to exist now so every caller is already provider-agnostic.

**Independent Test**: With the test provider wired in, an OTP request yields a record retrievable by a test-only fixture hook, and OTP-verify accepts that record. Switching the configured provider key to a second dummy provider exercises the same API surface without caller change.

**Acceptance Scenarios**:

1. **Given** the test OTP provider is configured, **When** an OTP is requested for a phone number, **Then** a record is created with the code, target, purpose, validity window, and attempt counter, and the provider returns a synthetic success.
2. **Given** the same phone number, **When** more than the allowed number of OTPs are requested within the rate-limit window, **Then** further requests are rejected with a localized rate-limit message.
3. **Given** an OTP already consumed, **When** it is submitted again, **Then** the verification fails (replay protection).

---

### Edge Cases

- Registration with an email whose format is valid but whose domain does not accept mail: OTP send fails; account remains in pending-verification with a clear retry path.
- Phone number submitted without a market-valid international prefix: rejected before OTP send with a localized field error.
- Two simultaneous registration attempts on the same identifier (race): exactly one account is created; the other attempt receives the uniform "identifier already in use" response.
- OTP requested, then the contact channel is changed mid-flow: in-flight OTPs are invalidated.
- Password reset token issued and the user's sessions are active on multiple devices: successful reset revokes all sessions across devices.
- Admin disabled mid-session: next request on the admin surface is rejected, the refresh token is revoked, and the revocation is audited.
- Role removed while a session is active: at next token refresh, the removed permissions no longer apply; if the session is accessed before refresh, the claim cache must have an upper bound measured in minutes, documented in the session policy.
- Locale header mismatches account locale: all user-facing messages (success, error, rate-limit, lockout) still render in the requested locale, falling back to the account locale, then to the market default.
- Customer uses the same email as an admin: customer and admin identities are separate, stored in separate tables or at minimum separate records; a collision on the raw identifier is allowed across surfaces but never within a surface.

---

## Requirements *(mandatory)*

### Functional Requirements

**Registration and verification**

- **FR-001**: System MUST allow customer registration with either an email address or a phone number, a password, a market code (EG or KSA), and a preferred locale (ar or en).
- **FR-002**: System MUST store passwords using a modern password-hashing function with a per-record salt and cost parameters appropriate for server-side verification (Argon2id family baseline).
- **FR-003**: System MUST create the account in a pending-verification state on first registration and block non-public actions (checkout, order placement, profile edits beyond basics) until the contact channel is verified.
- **FR-004**: System MUST issue a one-time passcode of documented length and validity window to the chosen contact channel on registration, on verification-resend, and on any flow that requires step-up verification.
- **FR-005**: System MUST reject OTP verification after the documented maximum number of incorrect attempts and require re-issuance.
- **FR-006**: System MUST return a uniform response for registration attempts using an identifier already in use so that identifier enumeration is not possible without proof of control.
- **FR-006a**: System MUST enforce global uniqueness of email addresses across all customer accounts and global uniqueness of phone numbers across all customer accounts, independent of market code. The same identifier MUST NOT be reused across EG and KSA customer accounts.

**Login and session management**

- **FR-007**: System MUST allow login by either email or phone + password and return an access token and a refresh token bound to a session record on success.
- **FR-008**: System MUST enforce a progressive lockout policy: repeated failed attempts on the same identifier lock the account for a cool-down window; the UX MUST NOT disclose whether the identifier exists.
- **FR-008a**: System MUST unlock a first-tier lockout automatically after the documented cool-down window elapses. A second lockout on the same account within a documented rolling window MUST escalate to a proof-of-control requirement: the account remains locked until the owner completes either a password reset or a fresh OTP step-up on a verified contact channel. Every lockout and every unlock (automatic or proof-based) MUST be written to the audit log and MUST be eligible to trigger a suspicious-activity notification via spec 025.
- **FR-009**: System MUST issue short-lived access tokens and longer-lived refresh tokens; token lifetimes MUST be configured per surface (customer vs admin) and MUST differ.
- **FR-010**: System MUST support refresh-token rotation on every successful refresh; the previous refresh token MUST be invalidated.
- **FR-011**: System MUST support session revocation: logout, forced logout on password change, forced logout on admin disable, and remote revoke by session id from an account session list.
- **FR-011a**: On the customer surface, the system MUST allow unlimited concurrent sessions per account across devices; every session MUST appear in the account's session list with enough device/context metadata (device summary, last-refreshed-at, ip-hash) for the owner to identify and remotely revoke it.
- **FR-011b**: On the admin surface, the system MUST permit only a single active session per admin user at any time; a successful login MUST revoke all prior active sessions belonging to that admin and MUST write the forced-revocation to the audit log.
- **FR-012**: System MUST cap the staleness of permission claims carried in a token; any role or permission change MUST take effect no later than the next token refresh.

**Password reset**

- **FR-013**: Users MUST be able to request a password reset by email or phone; the system MUST issue a single-use, time-boxed reset token to that channel.
- **FR-014**: System MUST require the new password to meet the documented password policy (length, character-class mix, no recent reuse at least of last N).
- **FR-015**: System MUST revoke all existing sessions for an account on successful password change or password reset.

**Admin surface separation**

- **FR-016**: System MUST expose customer auth and admin auth as two distinct surfaces with distinct issuers, audiences, cookie scopes, and endpoint prefixes; tokens from one surface MUST be rejected on the other.
- **FR-017**: System MUST enforce a shorter idle timeout on admin sessions than on customer sessions and MUST require step-up re-authentication before a documented set of sensitive admin actions (at minimum: role changes, permission changes, admin enable/disable, password reset on behalf of another user).
- **FR-018**: System MUST NOT allow self-service admin registration; admin accounts are created by an existing admin with the appropriate permission or via a seeded bootstrap path.

**RBAC framework**

- **FR-019**: System MUST model users, roles, permissions, and role-assignments such that a user can hold zero or more roles and a role grants zero or more permissions.
- **FR-020**: System MUST declare, per protected endpoint, the permission required to access it; endpoints without an explicit declaration MUST default to deny.
- **FR-021**: System MUST seed the minimum launch admin role matrix covering catalog, inventory, orders, customers, verification, support, finance, and super-admin; each seed role MUST be version-controlled in a migration-like artifact.
- **FR-022**: System MUST emit an audit-log event for every role creation, role update, role deletion, role assignment, role revocation, permission creation, permission update, admin enable, admin disable, password reset (initiated and completed), and failed authorization attempt, including actor, target, reason (where applicable), before, and after values per Principle 25.

**OTP provider abstraction**

- **FR-023**: System MUST encapsulate OTP delivery (phone channel) behind a provider abstraction with a deterministic test provider available in non-production environments.
- **FR-024**: System MUST apply rate limits per contact identifier and per source IP on OTP requests, independent of provider.
- **FR-025**: System MUST reject reuse of any OTP that has already been consumed or invalidated.

**Localization and market awareness**

- **FR-026**: System MUST render every user-facing message (validation, error, success, rate-limit, lockout, reset-link copy) in Arabic and English with editorial-grade copy, selecting locale by request header, falling back to account locale, then to the market default per Principle 4.
- **FR-027**: System MUST tag every account with a market code (EG or KSA) at creation; market-aware behavior for downstream specs MUST read this tag rather than hardcode branches.

**Multi-vendor-ready shape**

- **FR-028**: System MUST model roles and permissions in a shape that allows future vendor-scoped roles (role scoped to a vendor id) without schema breakage per Principle 6.

**Data residency and retention**

- **FR-029**: All identity records — customer accounts, admin accounts, password hashes, OTP records, password-reset tokens, sessions, refresh tokens, and emitted audit events — MUST be stored in the single ADR-010 region (Azure Saudi Arabia Central) for both EG and KSA markets, partitioned logically by `market_code` rather than by physical region.
- **FR-030**: System MUST model an account-deletion lifecycle on the customer account with at least the states `active → deletion-requested → anonymized`, recording requested-at, requested-by, scheduled-for, and completed-at. The `anonymized` transition MUST clear or irreversibly hash direct PII (email, phone, name, password hash, reset tokens, OTP records, session records) while retaining a stable internal id so downstream immutable records (orders per spec 011, tax invoices per spec 012) continue to reference a well-defined "anonymized customer" placeholder without integrity loss.
- **FR-031**: The customer-facing self-service deletion UX is explicitly deferred to Phase 1.5; at launch, the deletion-request state MAY be entered only by an authorized admin acting on a customer request, and every state transition MUST be written to the audit log per Principle 25.

### Key Entities

- **User (Customer)**: identified by a stable internal id; carries email (optional), phone (optional), password hash, status (pending-verification, active, locked, deletion-requested, anonymized, disabled), market code, preferred locale, created-at, last-login-at, deletion-requested-at (nullable), anonymized-at (nullable). At least one of email or phone MUST be present while the account is in a pre-anonymized state; both MAY be present. Email is globally unique across all non-anonymized customer accounts; phone is globally unique across all non-anonymized customer accounts — uniqueness is independent of market code. Once anonymized, direct PII is cleared or irreversibly hashed and the stable internal id is retained for downstream referential integrity.
- **User (Admin)**: identified by a stable internal id; carries email, password hash, status (active, disabled), assigned roles. Separate from Customer either by table or by typed record; never shares an authentication surface with Customer.
- **Role**: named bundle of permissions; has a stable key (e.g., `catalog-editor`), a display name (AR + EN), a scope field (`global` at launch; reserved for future `vendor`), and versioning metadata.
- **Permission**: finely named capability (e.g., `catalog.read`, `catalog.write`, `orders.refund.initiate`); stable key + display label (AR + EN); grouped by domain.
- **Role-Assignment**: link between a user and a role; records granted-by, granted-at, reason.
- **Session**: record bound to a refresh-token family; carries user id, surface (customer/admin), device/user-agent summary, ip-hash, created-at, last-refreshed-at, revoked-at, revoke-reason.
- **Access Token**: short-lived bearer value carrying user id, surface, permissions (as claims or claim reference), session id, issued-at, expires-at. Carried over the wire; never persisted server-side beyond the session linkage.
- **Refresh Token**: longer-lived, server-tracked, single-use-per-refresh record; rotated on every refresh; revocable individually or per-user.
- **OTP Record**: contact target (email or phone), purpose (registration, reset, step-up), code hash, attempt counter, expires-at, consumed-at, issuing provider key.
- **Password Reset Token**: single-use, time-boxed; tied to a user; purpose = password-reset; records issued-at, consumed-at.
- **Audit Event** (emitted, not owned): actor, target, action, reason, before, after, correlation id — consumed by the Phase 1A audit-log module.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A new customer can complete registration and contact verification end-to-end in under two minutes in either locale (AR or EN) under normal network conditions.
- **SC-002**: 99% of successful login requests complete (from credentials submitted to session returned) in under one second under nominal load on the launch infrastructure baseline.
- **SC-003**: Zero protected endpoints are reachable without the required permission, as verified by an automated role × endpoint matrix test over every registered protected endpoint.
- **SC-004**: 100% of identity events in the scope of Principle 25 (role changes, admin enable/disable, password reset initiated/completed, failed authorization) produce an audit-log entry with actor, target, reason, before, after, and correlation id on the first release candidate build.
- **SC-005**: Every user-facing identity message (success, error, rate-limit, lockout, reset-link copy) has an Arabic and an English variant signed off by the editorial reviewer before the spec's DoD.
- **SC-006**: Password hashing parameters are strong enough that verifying a single candidate against a hash takes at least 100 ms of server CPU time on the baseline infrastructure.
- **SC-007**: The OTP provider boundary is swappable: switching the configured provider from the test provider to a second dummy provider requires zero changes to callers and zero contract changes to request/verify endpoints.
- **SC-008**: Role and permission changes propagate to active sessions no later than the session's next token refresh, with the documented maximum staleness window met in 99% of measurements under load.

---

## Assumptions

- The Phase 1A shared-foundations layer (spec 003) is at DoD: error envelope, correlation-id middleware, structured logging, audit-log module, storage abstraction, PDF abstraction, localization scaffolding, and the `packages/shared_contracts` pipeline are available and consumed here without re-implementation.
- The real SMS/email/push provider is deferred to Phase 1E spec 025 per the implementation plan; this spec ships a test provider for dev + CI and documents the contract the real provider will implement.
- Social-login / SSO / OAuth2-third-party (Google, Apple, etc.) is out of scope for launch unless explicitly reopened; password + OTP is the launch authentication matrix.
- Two-factor authentication beyond contact-channel OTP on registration and step-up actions is out of scope for launch; it is a candidate for Phase 1.5.
- Admin bootstrap is handled by a seed migration producing a super-admin in every environment; self-service admin registration is explicitly not a launch feature.
- Password policy thresholds (exact minimum length, character-class mix, recent-reuse window) are chosen from the project's security baseline and documented in the plan; the spec fixes the requirement shape, not the exact numbers.
- Rate-limit thresholds (login attempts before lockout, OTP requests per window, reset-token requests per window) are chosen from the project's security baseline and documented in the plan; the spec fixes the requirement shape.
- The customer and admin surfaces are hosted at different origins or at minimum under different path prefixes with different cookie scopes; infrastructure arrangement is a plan-level decision.
- Vendor-scoped roles (Principle 6 multi-vendor-ready) are represented by a scope field whose only launch value is `global`; no vendor-scoped assignments are issued in Phase 1.
- WhatsApp as an OTP channel is deferred to Phase 1.5 (spec 1.5-f) per the implementation plan; launch OTP channels are email and SMS.

---

## Dependencies

- **001 · governance-and-setup** — branch protections, CODEOWNERS, constitution/ADR fingerprint on PRs.
- **002 · architecture-and-contracts** — OpenAPI pipeline, contract-diff CI, ERD baseline, domain layout.
- **003 · shared-foundations** — audit-log module, error envelope, correlation id, localization + RTL, storage abstraction (for future profile photo / admin asset), `packages/shared_contracts` consumer flow.

**Consumed by (forward-looking; informational only)**:

- **005 · catalog** — admin auth + RBAC for catalog editors; restricted-product metadata links to verification state owned here.
- **009 · cart / 010 · checkout** — authenticated-actor requirement before checkout; restricted-product eligibility gate reads identity + verification.
- **015 · admin-foundation** — consumes admin auth + RBAC directly; every admin spec thereafter depends on this.
- **020 · verification** — attaches verification state to the User (Customer) entity defined here.
- **025 · notifications** — implements the real OTP providers behind the abstraction defined here.

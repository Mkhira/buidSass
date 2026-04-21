# Feature Specification: Identity and Access

**Feature Branch**: `004-identity-and-access`
**Created**: 2026-04-22
**Status**: Draft
**Constitution**: v1.0.0
**Phase**: 1B (Core Commerce · Milestone 2)
**Depends on**: Phase 1A (specs 001–003 + A1) at DoD
**Input**: User description: "Phase 1B, spec 004 — identity-and-access. Deliver customer + admin authentication, phone/email verification via OTP abstraction, Argon2id password handling, session management (access + refresh with revocation), role + permission data model, and RBAC middleware for the bilingual Egypt + KSA dental commerce platform. Separate customer and admin auth surfaces. Usable by downstream specs 005–013."

## Clarifications

### Session 2026-04-22

- Q: Admin MFA posture in V1 → A: Tiered — TOTP authenticator-app mandatory for super-admin and finance roles; email/phone OTP step-up for other admin roles; recovery codes issued at enrolment.
- Q: Concrete session lifetimes → A: Tiered by surface — customer access 15 min / refresh 30 days; admin access 5 min / refresh 8 hours; both credentials rotate on use and are server-revocable.
- Q: Initial super-admin bootstrap per environment → A: Tiered by environment — Dev/local uses the A1 seed framework under SeedGuard; Staging and Production require an operator-run CLI one-shot (`seed-admin`) that refuses if any super-admin already exists and emits an audit event on success.
- Q: Security thresholds (lockout / OTP / password policy) → A: Tiered by surface. **Customer** — password min 10 chars with breach-list check (no forced complexity), progressive lockout at 5 consecutive failures (1-min → 5-min → 30-min → admin-unlock on successive tiers), per-IP 20/hr; OTP 6-digit / 5-min TTL / 3 attempts per challenge / 3-per-10-min issuance. **Admin** — password min 12 chars with breach-list + forced complexity; 3 failures → 30-min lockout; per-IP 10/hr; OTP 8-digit / 3-min TTL / 3 attempts per challenge / 2-per-hour issuance. Aligned with NIST 800-63B for customers; hardened for admin blast radius.
- Q: Market-of-record assignment at registration → A: User-selected at registration with phone country-code pre-fill (pre-fill does not lock the choice); immutable once the account is active except via an audited admin-assisted change. Market value feeds every downstream spec that reads `market_code` (VAT, COD, notifications, legal pages).

## Primary outcomes

1. Every customer — dentist, clinic staff, lab, student, consumer — can register, verify, log in, recover access, and sign out in Arabic or English across mobile, web, and email/SMS touchpoints.
2. Every admin operator authenticates through a surface that is physically and functionally separate from the customer surface, with its own provisioning, recovery, and session controls.
3. Every downstream spec (cart, checkout, orders, admin modules) can gate actions by role and permission, carry identity in a portable session claim, and emit an audit trail on every identity or authorization change.
4. Future marketplace and B2B expansion (multi-user company accounts, verified professional purchases, quote approvers, multi-branch buyers) is possible without rewriting the identity model.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — A dental professional in KSA or Egypt creates an account and signs in bilingually (Priority: P1)

As a **dentist, clinic buyer, lab technician, or dental student in KSA or Egypt**, I need to register for an account with my phone or email, verify it, and sign in — with every label, error, OTP message, and reset email readable and correct in both Arabic (RTL) and English — so that I can place orders, request quotes, and manage my account without being blocked by language, market, or technology friction.

**Why this priority**: This is the gateway to every transacting flow. Every downstream spec (cart, checkout, orders, quotes, verification) depends on authenticated customers existing. Without it, no revenue path works.

**Independent Test**: Fresh install of the mobile app in Arabic locale in KSA. Complete registration using a KSA phone number, receive the OTP, verify, set a password, and sign in. Repeat in English locale on the web storefront in Egypt using an email identifier. Both journeys succeed end-to-end without language fallbacks, English-only errors, or LTR-mirrored RTL screens.

**Acceptance Scenarios**:

1. **Given** a new visitor in Arabic locale with a valid KSA phone number, **When** they submit registration and provide the OTP they receive, **Then** an account is created and they are signed in with a session usable on both the mobile app and the web storefront.
2. **Given** a new visitor in English locale with a valid email address, **When** they submit registration and confirm the verification email, **Then** an account is created and they are signed in.
3. **Given** an account exists, **When** the user signs out and signs in again with email or phone plus password, **Then** access is restored and the previous session is invalidated on that device.
4. **Given** the user forgets their password, **When** they request reset and follow the single-use link or code, **Then** they can set a new password and the old password is invalidated everywhere.
5. **Given** the user enters the wrong password repeatedly past the lockout threshold, **When** they try again, **Then** they are blocked for a bounded cooldown with a clear, localized message and the account is not silently compromised.
6. **Given** any screen, email, SMS, or PDF produced by this flow, **When** rendered in Arabic, **Then** all strings are editorial-grade Arabic (not machine-translated), numerals and dates are locale-correct, and layout mirrors fully to RTL.

---

### User Story 2 — An admin operator signs in through a controlled surface separate from customers (Priority: P1)

As an **admin operator** (catalog editor, order manager, verification reviewer, super-admin), I need to authenticate through an admin surface that is distinct from the customer app — with its own URL, its own provisioning path, its own recovery flow, and its own session policy — so that a customer-account breach can never escalate to administrative access and so that privileged sessions can be audited and revoked independently.

**Why this priority**: Admin access is a blast-radius multiplier. A single compromised admin can affect catalog, pricing, orders, or customer data. Keeping admin auth on a separate surface with its own provisioning and session controls is the minimum defensive posture for a launching commerce platform.

**Independent Test**: From a customer account signed in on the mobile app, try to reach any admin surface or admin API. Confirm there is no path — the customer session is not accepted on the admin surface. From the admin surface, sign in with admin credentials and confirm the resulting session is not accepted on the customer app. Provision a second admin through the invite path and confirm it succeeds only when initiated by an existing admin with the correct permission.

**Acceptance Scenarios**:

1. **Given** an admin account exists, **When** the admin signs in on the admin surface, **Then** a session is issued that is valid only on admin endpoints and is rejected by any customer endpoint.
2. **Given** a signed-in customer attempts to reach an admin endpoint or the admin login URL, **When** the request is made, **Then** it is rejected with no leak of admin-surface existence.
3. **Given** a new admin must be onboarded, **When** an existing admin with provisioning permission issues an invite, **Then** the invitee can accept, set a password, and is activated with the role the issuer assigned — self-signup is not possible on the admin surface.
4. **Given** an admin session exists, **When** a super-admin revokes it, **Then** the session ceases to authenticate subsequent requests within the revocation propagation window and the action is recorded in the audit log with actor, target, and reason.
5. **Given** an admin's role or permissions change, **When** the change is saved, **Then** the audit log records actor, target admin, before/after state, and the new set of permissions takes effect for that admin on their next request.

---

### User Story 3 — Every protected action is gated by a permission check with a complete audit trail (Priority: P1)

As a **platform owner**, I need every customer-restricted and admin-restricted action to be gated by an explicit permission check — and I need every role change, permission change, login, failed-login burst, password change, and session revocation to be recorded in the audit log with actor, target, timestamp, and before/after state — so that I can answer "who did what and when" on demand, meet compliance obligations, and detect identity-based abuse.

**Why this priority**: Constitution Principle 25 makes audit mandatory for role, permission, and verification decisions. Without it, no downstream admin or finance module is compliant. This story makes 004 the place where auditable identity behavior is established once for the whole platform.

**Independent Test**: Perform a representative set of identity and authorization events — register, log in, fail login three times, reset password, revoke session, create admin, grant role, change permissions, demote admin. Read the audit log and confirm each event is present with actor, target, timestamp, before/after state where applicable, and correlation id. Attempt a privileged action with a role that lacks the required permission and confirm it is rejected and the denial is logged.

**Acceptance Scenarios**:

1. **Given** a user attempts any protected action, **When** the request is evaluated, **Then** the decision is driven by an explicit permission check against the identity's resolved permission set — not by a role-name string match or a hardcoded bypass.
2. **Given** any of these events occurs — role assignment, role revocation, permission grant or revoke, successful admin login, admin password change, admin session revocation, admin account creation, admin account deactivation, privileged-action denial — **When** the event completes, **Then** it is emitted to the audit log with actor identity, target identity, timestamp, correlation id, and before/after permission snapshot where applicable.
3. **Given** a burst of failed logins on an account, **When** the lockout threshold is reached, **Then** the lockout event is recorded in the audit log and the account's security timeline reflects the burst for administrative review.
4. **Given** an identity has been deactivated, **When** it attempts any action, **Then** access is refused consistently across customer and admin surfaces, and the denial is logged.

---

### User Story 4 — Phone verification and OTP work reliably across both markets with a provider-agnostic contract (Priority: P1)

As a **new customer in Egypt or KSA**, I need phone verification and any OTP-driven flow (phone verification at registration, phone-based password reset, future step-up challenges) to work reliably with a clear OTP, a clear expiry, and a clear retry path — and as a **platform owner** I need the OTP provider to be swappable in Phase 1E (spec 025) without rewriting identity code — so that today we can launch with a stub/provider and later switch providers per market without regressions.

**Why this priority**: OTP-by-phone is the default verification path in both markets. If it is flaky or tightly coupled to one provider, the platform cannot launch. If it leaks via replay, the platform cannot stay trusted. The abstraction also unblocks spec 025 from day one.

**Independent Test**: Trigger an OTP from the customer app. Confirm the OTP is delivered within the target SLA (or to the local stub sink in dev/staging), is bounded in length and expiry, cannot be used twice, cannot be brute-forced past the attempt cap, and cannot be resent above the rate limit. Swap the OTP adapter at the seam (stub → dev provider → a second stub) without any change to call sites in registration, password reset, or session challenges.

**Acceptance Scenarios**:

1. **Given** a valid phone number for EG or KSA, **When** the user requests an OTP, **Then** exactly one OTP is delivered and it expires within a bounded lifetime.
2. **Given** an OTP has been consumed once, **When** it is submitted again, **Then** it is rejected and the attempt is logged.
3. **Given** a user requests multiple OTPs in quick succession, **When** the per-identity or per-phone rate limit is exceeded, **Then** additional requests are refused with a clear, localized message and a cooldown indication.
4. **Given** an OTP is wrong, **When** it is submitted past the per-challenge attempt cap, **Then** the challenge is invalidated and a new OTP must be requested.
5. **Given** the OTP provider adapter is changed at the seam, **When** any OTP flow runs, **Then** the flow succeeds with identical call sites and identical observable contract.

---

### User Story 5 — Sessions are controllable: multi-device, revocable, and refreshable without re-login (Priority: P2)

As a **signed-in customer**, I want to stay signed in across my devices without re-entering my password every time the app opens, and as a **platform owner** I need to be able to revoke any individual session or all of a user's sessions if a device is lost or abuse is suspected — so that convenience and security coexist.

**Why this priority**: Customers will abandon an app that re-prompts for password on every open. At the same time, a compromised device must be revocable. Both properties depend on a refresh-token model with server-side revocation.

**Independent Test**: Sign in on two devices. Confirm both stay signed in across app restarts without re-entering the password. Revoke one session from the user's settings. Confirm that session stops working within the propagation window and the other device continues to work. Revoke all sessions. Confirm both devices are signed out and a new sign-in is required.

**Acceptance Scenarios**:

1. **Given** a signed-in customer, **When** their short-lived access credential expires, **Then** the client silently refreshes it using the long-lived refresh credential without prompting for the password.
2. **Given** an active session, **When** the user or an admin revokes it, **Then** the session stops authenticating subsequent requests within the revocation propagation window.
3. **Given** a refresh credential has been revoked or has expired, **When** a refresh is attempted, **Then** it is refused and the user is returned to the sign-in surface with a clear, localized message.
4. **Given** a user signs in on a new device, **When** the session is issued, **Then** it is listed in the user's "active sessions" view with device description, location hint, and last-used timestamp.
5. **Given** the user chooses "sign out of all devices", **When** they confirm, **Then** every active session and refresh credential for that user is revoked and only a fresh sign-in can re-establish access.

---

### User Story 6 — Identity supports future B2B and restricted-product flows without rework (Priority: P2)

As a **platform owner**, I need the identity model to carry the hooks that Constitution Principles 6, 8, and 9 require — professional-verification status for restricted-product eligibility, optional company-account linkage, and future multi-user buyer/approver shape — so that specs 020 (verification), 021 (B2B), and the restricted-purchase checks in 009–010 can compose on top of identity without a migration.

**Why this priority**: Retrofitting identity after launch is the single most expensive refactor on a commerce platform. Designing these hooks in V1 with clear semantics costs little now and saves everything later. However, the full flows (verification review, company admin console, approver routing) are NOT in 004 — only the identity-side hooks are.

**Independent Test**: Inspect the identity model and confirm it exposes a professional-verification status, an optional company-account reference, and a role/permission shape that can represent a buyer-vs-approver distinction — without spec 004 implementing any verification UI, any company-management UI, or any approval routing. Downstream specs 009, 010, 020, and 021 should be able to consume these hooks unchanged.

**Acceptance Scenarios**:

1. **Given** a customer identity, **When** any consumer of identity queries it, **Then** a professional-verification status is exposed (e.g., unverified / pending / verified / rejected / expired) with a last-updated timestamp, and eligibility decisions for restricted products can be made against it.
2. **Given** a future spec (020) sets or changes a customer's professional-verification status, **When** the change lands, **Then** it is audit-logged and any downstream gate sees the new status without 004 code changes.
3. **Given** the identity model, **When** a future spec links a customer to a company account with a buyer or approver role, **Then** the linkage fits the existing role/permission shape with no schema redesign.
4. **Given** spec 004 is delivered, **When** running the platform, **Then** no spec-020 verification UI, no spec-021 company-management UI, and no approval routing is implemented here — only the identity-side hooks.

---

### Edge Cases

- User enters a phone number in one market format (e.g., KSA `+966…`) while the market picker (FR-001a) is set to EG. The two MUST NOT silently be reconciled; the registration UI MUST surface the mismatch clearly in the user's locale, require the user to confirm either the phone country code or the market picker value, and not persist any identity until the user has confirmed one consistent choice.
- User registers with email, then later adds a phone. Or vice versa. The identity must remain single-record; email and phone are alternative identifiers on the same identity.
- User attempts to register a phone or email already attached to another account. The system must reject without leaking whether the identifier exists (anti-enumeration) while still guiding the legitimate owner to recovery.
- Clock skew on the client device invalidates short-lived access tokens moments after issue. The client refresh path must handle this without surfacing an error to the user.
- Account was deactivated mid-session. Every in-flight privileged action must be refused on the very next request after deactivation propagates.
- The OTP provider is temporarily unavailable. The user must see a localized, actionable error; identity does not silently fall back to an unverified account.
- Password reset token is used twice (e.g., a click on two tabs). Only the first use succeeds; the second is refused and the second attempt is logged.
- A refresh token is replayed from a device other than the one it was issued to. The replay must be refused and the chain invalidated.
- A customer role is revoked mid-session. The next request must enforce the new permission set, not the cached one.
- Registration is attempted with a weak password (e.g., below policy minimum, in a common-breach list). It must be refused with a clear, localized reason.
- A user attempts sign-in with credentials belonging to a deactivated admin surface. Admin and customer lookups must not cross-leak.
- A customer attempts to hit the admin surface URL. The surface must behave as if it does not exist rather than echoing "admin login" to unauthenticated traffic.
- A user in Arabic locale triggers an error path that has only an English string. This is non-compliant — every identity-facing string must resolve bilingually.

## Requirements *(mandatory)*

### Functional Requirements

#### Identity model and uniqueness

- **FR-001**: System MUST model identities as a single record per person that MAY carry both an email and a phone as alternate primary identifiers, plus display metadata (locale preference, market of record, display name).
- **FR-001a**: System MUST require the user to **explicitly select their market of record** (Egypt or KSA) at registration. When the user registers with a phone number, the market picker MUST be pre-filled from the phone country code (`+966` → KSA, `+20` → EG) but the pre-fill MUST remain user-overridable before submission. The chosen `market_code` MUST be stored on the identity and MUST drive every downstream market-aware behavior (VAT in spec 012, COD eligibility in spec 010, shipping methods in spec 014 of 1E, notification routing in spec 025, legal pages). Once the account is active, the `market_code` MUST be immutable for the end user; changes MUST require an audit-logged admin action ("admin:identity:change-market") and MUST be rare — admin tooling for the change ships in Phase 1D or later, not in 004.
- **FR-002**: System MUST treat identifiers (email, phone) as case-and-format-normalized and globally unique across the identity store.
- **FR-003**: System MUST separate **customer identity** and **admin identity** into distinct authentication surfaces with distinct credentials, distinct provisioning paths, and distinct session scopes — a credential valid on one surface MUST NOT authenticate on the other.
- **FR-004**: System MUST expose a **professional-verification status** on every customer identity (unverified / pending / verified / rejected / expired) with a last-updated timestamp, consumed by downstream restricted-product gating (Principles 6 and 8). Verification review UI is explicitly NOT in scope for 004.
- **FR-005**: System MUST expose an **optional company-account reference** on every customer identity (nullable; Principle 9), with a role shape capable of representing buyer vs approver in future specs. Company-management UI and approval routing are explicitly NOT in scope for 004.

#### Registration, verification, and recovery

- **FR-006**: Users MUST be able to register with either an email or a phone number; at least one verified identifier MUST exist before an account is considered usable for transacting.
- **FR-007**: System MUST verify the chosen identifier with a one-time challenge (email link or phone OTP) before the account is activated.
- **FR-008**: System MUST enforce a tiered password policy. **Customer surface**: minimum length 10 characters, no forced character-class complexity, compared against a deny-list of known-breached and trivially-weak passwords (per NIST 800-63B guidance). **Admin surface**: minimum length 12 characters, forced complexity (requires characters from at least three classes — upper, lower, digit, symbol), same breach deny-list. Rejections MUST explain the reason in the user's locale; MUST NOT reveal which specific rule failed beyond what the user needs to correct the input.
- **FR-009**: System MUST hash passwords with a memory-hard, salted algorithm and MUST NOT ever store or log plaintext passwords, plaintext OTPs, or reversible derivatives.
- **FR-010**: Users MUST be able to reset a forgotten password via a single-use, time-bounded token delivered to the verified identifier; using the token MUST invalidate all existing sessions for that identity.
- **FR-011**: System MUST resist account enumeration in registration, recovery, and login error responses (same timing and same localized message whether or not the identifier exists).

#### Login, sessions, and refresh

- **FR-012**: Users MUST be able to sign in with email-or-phone plus password on the customer surface, and with email plus password (phone optional) on the admin surface.
- **FR-013**: System MUST issue an access credential plus a server-revocable refresh credential on every successful sign-in with tiered lifetimes per surface: **customer surface** = 15 minute access / 30 day refresh; **admin surface** = 5 minute access / 8 hour refresh. Both refresh credentials MUST rotate on use (single-use), and replay of a consumed refresh credential MUST invalidate the entire refresh chain for that session per FR-017.
- **FR-014**: System MUST allow clients to silently obtain a new access credential using a valid refresh credential without re-prompting for password, up until the refresh credential expires or is revoked.
- **FR-015**: Users MUST be able to view their active sessions (device description, last-used timestamp, approximate location) and revoke any individual session or all sessions at once.
- **FR-016**: Admins MUST be able to revoke any user's session(s) for cause; revocations MUST be audit-logged with actor, target, and reason.
- **FR-017**: System MUST enforce session revocation within a bounded propagation window and MUST refuse replayed or cross-device refresh credentials.

#### Failure handling and abuse resistance

- **FR-018**: System MUST apply a tiered lockout policy on consecutive failed password attempts. **Customer surface**: progressive — after 5 consecutive failures a 1-minute cooldown; after the next 5, 5 minutes; after the next 5, 30 minutes; after the next 5, admin-unlock required (self-clearing does not resume automatically past the fourth tier). **Admin surface**: after 3 consecutive failures, a 30-minute lockout, no progressive escalation and no self-clearing reset before the cooldown expires. System MUST additionally apply a per-IP password-attempt rate limit of 20/hour on the customer surface and 10/hour on the admin surface, counted independently of per-account state. Every lockout and per-IP block MUST be audit-logged with actor, surface, identifier, and reason.
- **FR-019**: System MUST rate-limit OTP issuance per identifier and per requesting client and MUST cap per-challenge attempts. **Customer surface**: maximum 3 OTP issuances per identifier per 10 minutes; maximum 3 incorrect attempts per challenge. **Admin surface**: maximum 2 OTP issuances per identifier per hour; maximum 3 incorrect attempts per challenge. Violations MUST return a clear, localized error and a cooldown indication without revealing whether the identifier exists.
- **FR-020**: OTPs MUST be single-use and resistant to replay (invalidated on first successful use or after the per-challenge attempt cap is reached). **Customer surface** OTPs MUST be 6 decimal digits with a 5-minute expiry. **Admin surface** OTPs (step-up only — not for the MFA-required tier, which uses TOTP per FR-024b) MUST be 8 decimal digits with a 3-minute expiry.
- **FR-021**: System MUST emit structured security events for: successful login, failed login, lockout, password change, password reset, OTP issued, OTP consumed, OTP failed, session revocation, admin role change, admin permission change, admin account created, admin account deactivated, privileged-action denial.

#### Admin provisioning

- **FR-022**: Admin accounts MUST be created only by invitation from an existing admin who holds the "manage admins" permission; self-signup MUST NOT be possible on the admin surface.
- **FR-023**: Admin invitations MUST be single-use, time-bounded, and revocable; acceptance MUST require the invitee to set their password and the issuing admin's chosen role MUST be applied at acceptance time.
- **FR-024**: System MUST support deactivating an admin (preserving their audit history) and MUST refuse all future authentication attempts by a deactivated admin across all surfaces.
- **FR-024pre**: The first super-admin in each environment MUST be bootstrapped by an environment-aware mechanism: (a) **Development**: A1 seed framework seeder (`identity-v1`) under SeedGuard creates a deterministic super-admin with rotating first-sign-in credentials; (b) **Staging and Production**: an operator-run CLI one-shot command (`seed-admin --email <...> --phone <...>`) that MUST refuse to execute if any super-admin already exists in the target environment, MUST emit an `admin.bootstrap` audit event on success, MUST NOT be exposed as an HTTP endpoint, and MUST require the operator to present a one-time initial password delivered out-of-band that is force-rotated on first sign-in. Staging and Production MUST NOT ship with any seeded or default super-admin credentials; if no super-admin exists, the admin surface MUST remain unavailable until the CLI one-shot has run.

#### Admin multi-factor authentication (tiered)

- **FR-024a**: System MUST maintain a configurable tier of admin roles that require multi-factor authentication beyond password. At V1 the required tier contains **super-admin** and **finance-viewer**; adding or removing a role from the tier MUST be audit-logged and MUST force affected admins to complete enrolment (or re-confirm enrolment) on their next sign-in before any access is granted.
- **FR-024b**: Admins in the MFA-required tier MUST enrol a TOTP (RFC 6238, authenticator-app compatible) factor and MUST present a valid TOTP code on every admin sign-in; sign-in MUST fail closed if enrolment is incomplete for a role that requires it, with a clear, localized explanation and an admin-initiated enrolment path.
- **FR-024c**: Enrolment MUST issue a bounded set of single-use recovery codes delivered exactly once to the admin at enrolment time; recovery codes MUST be hashed at rest, single-use, and invalidated on use; the holder MAY regenerate recovery codes after successful TOTP verification, and a super-admin MAY reset another admin's MFA factor as an audit-logged recovery action.
- **FR-024d**: Admin roles outside the MFA-required tier MUST sign in with password (plus FR-011 anti-enumeration guarantees) and MUST pass an email/phone OTP step-up challenge before executing any **privileged action** — defined in V1 as: role change, permission change, admin invitation, admin deactivation, refund decision, finance data export, catalog price change, promotion publish, and verification-status override.
- **FR-024e**: Every MFA event (TOTP enrolment, TOTP verification success/failure, recovery-code issued, recovery-code consumed, recovery-code regenerated, MFA reset by super-admin, step-up challenge issued, step-up challenge passed/failed) MUST be emitted to the audit log with actor, target admin, event type, and timestamp per FR-021.

#### Authorization (RBAC) and permission resolution

- **FR-025**: System MUST model **roles** as named bundles of **permissions** and MUST assign zero-or-more roles to each identity; customer and admin roles MUST be separately namespaced.
- **FR-026**: System MUST resolve an identity's effective permission set at request time and MUST carry a machine-verifiable permission claim in the session credential so downstream services can gate actions without a round-trip per call.
- **FR-027**: System MUST refuse any privileged action whose required permission is not present in the caller's effective set and MUST log the denial with actor, resource, and required permission.
- **FR-028**: Permission and role changes MUST take effect no later than the next request after propagation, MUST be audit-logged with before/after state, and MUST be resistant to stale-cache bypasses.
- **FR-029**: System MUST seed a minimum set of admin roles at deploy time (e.g., super-admin, catalog-admin, order-admin, verification-reviewer, support-agent, finance-viewer) sufficient for specs 005–013 and 020 to compose on.

#### Bilingual, market-aware, and accessibility surface

- **FR-030**: Every user-facing identity string — screens, emails, SMS, PDF receipts of identity events, error messages — MUST be delivered bilingually (Arabic + English), with editorial-grade Arabic (never machine-translated) and full RTL support (Principle 4).
- **FR-031**: Every identity date, time, phone, and numeric format MUST be locale-and-market-aware (EG vs KSA), never hardcoded to `en-US` or Gregorian-only.
- **FR-032**: Every identity flow MUST satisfy Principle 27: define loading, empty, error, success, and recovery states, with accessibility considerations (focus management, screen-reader labels, contrast compliance).

#### Provider abstraction and downstream composability

- **FR-033**: OTP delivery MUST go through a provider abstraction with a stable contract so that spec 025 can swap providers per market without changing identity call sites or data model.
- **FR-034**: Email delivery for identity events (verification, password reset, invitation, security notice) MUST go through an abstraction so that spec 025 can swap providers without changing identity call sites.
- **FR-035**: Identity MUST expose a stable, documented contract (entities, events, claims, permissions) that specs 005–013, 014, 015, and 020–024 can consume without needing changes back into 004.

### Key Entities

- **Identity**: A single person with a stable internal id, locale preference, market of record, professional-verification status, optional company-account reference, zero-or-more credentials, zero-or-more roles, and an audit history. Customer identity and admin identity are distinct shapes with distinct storage boundaries.
- **Credential**: A password on an identity, with algorithm, salt, and last-rotated-at metadata. Never exposed.
- **Role**: A named bundle of permissions. Separately namespaced for customer and admin.
- **Permission**: A single atomic capability (e.g., "orders:read", "verification:decide"). The resolution unit of every authorization check.
- **Session**: An authenticated context for one device, with a short-lived access credential and a server-revocable, rotating refresh credential; carries device description, last-used timestamp, and location hint.
- **OTP Challenge**: A time-bounded, single-use challenge issued to a phone or email for registration verification, password reset, or future step-up.
- **Admin Invitation**: A single-use, time-bounded provisioning artifact issued by a privileged admin, carrying the role to be applied at acceptance.
- **Admin MFA Factor**: A TOTP-authenticator enrolment (shared secret + metadata) plus a bounded set of hashed, single-use recovery codes bound to an admin identity. Required for admins in the MFA-required tier (super-admin, finance-viewer); enrolment and every use are audit-logged.
- **Identity Audit Event**: An append-only record of a security-relevant event (actor, target, event type, before/after where applicable, timestamp, correlation id). Consumed by spec 003's audit module.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A new customer in KSA or Egypt can complete registration, verify their phone or email, and reach a signed-in state in under **2 minutes** in either Arabic or English locale.
- **SC-002**: 99% of sign-in attempts with correct credentials complete within **2 seconds** end-to-end, measured at the customer surface.
- **SC-003**: 95% of OTP messages are delivered within **30 seconds** of request in each supported market; delivery-failure messaging is shown to the user within **5 seconds** when the provider fails.
- **SC-004**: A user who forgets their password recovers access within **2 minutes** of starting the reset flow on a device with network.
- **SC-005**: Zero plaintext passwords, zero plaintext OTPs, and zero reversible credential derivatives are ever written to any database, log sink, or telemetry stream (verified continuously).
- **SC-006**: Zero customer sessions are accepted by any admin endpoint and zero admin sessions are accepted by any customer endpoint, verified by automated end-to-end cross-surface tests in CI.
- **SC-007**: Session revocation takes effect for 100% of revoked sessions within the documented propagation window (target: under **60 seconds**).
- **SC-008**: Every event in FR-021 is present in the audit log within **5 seconds** of occurrence, with actor, target, timestamp, and before/after state where applicable.
- **SC-009**: Every user-facing identity string is available in both Arabic and English with 0 missing keys; every Arabic string is marked editorially reviewed before the spec exits DoD.
- **SC-010**: The identity contract (entities, events, permission names, session claim shape) is consumed unchanged by specs 005–013 with 0 back-edits to 004 during those specs' implementation.
- **SC-011**: Account-enumeration tests (registration, recovery, login) cannot distinguish existing vs non-existing identifiers by timing or response content at 95% statistical confidence.
- **SC-012**: Under a burst of automated failed-login attempts, the target account reaches lockout at the configured threshold (customer: 5 consecutive; admin: 3 consecutive) with 100% accuracy across 1,000 simulated bursts, and no correctly-authenticated parallel user on the same surface experiences degraded latency above the SC-002 target.
- **SC-013**: 100% of admins whose role is in the MFA-required tier (super-admin, finance-viewer at V1) hold an active TOTP enrolment before that role is effective; 0 privileged admin actions execute on behalf of an admin lacking required enrolment; 100% of privileged actions by admins outside the MFA-required tier are preceded by a passed OTP step-up challenge within the step-up validity window.

## Assumptions

- **Password algorithm**: Argon2id (memory-hard, salted) is the chosen hash per Phase 1B plan task 2; concrete parameters (memory, iterations, parallelism) are tuned in plan/implementation and validated against a target wall-clock cost on prod-equivalent hardware.
- **Session credential shape** (values locked by Clarifications 2026-04-22 Q2): Access and refresh credentials are JWTs (per Phase 1B plan task 3). **Customer surface**: access TTL = 15 minutes, refresh TTL = 30 days. **Admin surface**: access TTL = 5 minutes, refresh TTL = 8 hours. Refresh credentials rotate on every use; replay of a consumed refresh credential invalidates the whole chain (FR-017). All revocations are backed by a server-side revocation store (below) — JWT expiry alone is insufficient.
- **Session revocation mechanism**: A server-side revocation store (not pure JWT expiry) makes "sign out of all devices" and admin forced-revocation possible within the propagation window. JWT expiry alone is insufficient.
- **OTP provider in 004**: A dev/stub adapter ships with 004 behind the provider abstraction; real per-market providers (SES, Unifonic, FCM candidates per ADR-009) are selected and wired in Phase 1E spec 025. ADR-009 remains Proposed until then.
- **Password reset and verification delivery**: Email and SMS both flow through provider abstractions; local delivery in dev goes to a visible sink (console / local mailbox) so QA and seed data work without external providers.
- **Admin MFA** (locked by Clarifications 2026-04-22 Q1): **Tiered by role.** Super-admin and finance-viewer MUST enrol in TOTP and present a TOTP code on every admin sign-in, with recovery codes issued at enrolment. Other admin roles (catalog-admin, order-admin, verification-reviewer, support-agent) sign in with password only and pass an email/phone OTP step-up before any privileged action. The MFA-required tier is configuration, not hardcoded — expansion is audit-logged and forces re-enrolment. Full TOTP-for-all-admins and WebAuthn/passkey support revisit in Phase 1.5 if warranted.
- **Admin provisioning** (bootstrap locked by Clarifications 2026-04-22 Q3): The first super-admin in each environment is bootstrapped per FR-024pre — Dev via the A1 seed framework under SeedGuard; Staging and Production via an operator-run `seed-admin` CLI one-shot that refuses when a super-admin already exists and emits `admin.bootstrap` to the audit log. Thereafter all admins are created only by invitation (FR-022–FR-023). No admin self-signup path exists on any environment.
- **Company accounts**: The identity model exposes a nullable company-account reference and a role shape that can represent buyer vs approver; full company-management and approver-routing UX live in Phase 1D specs (notably 021) and do not ship in 004.
- **Professional verification**: 004 exposes the verification-status field and the audit hook; the verification review UI, document upload, and decision workflow live in Phase 1D spec 020.
- **Markets** (assignment locked by Clarifications 2026-04-22 Q5): Egypt and KSA only at launch. Market of record is user-selected at registration per FR-001a, pre-filled from phone country code when phone-first, immutable post-activation except via an audit-logged admin-assisted change. Phone-number format validation and OTP routing are market-aware; neither market is hardcoded in identity logic — market configuration drives the behavior per Constitution Principle 5.
- **B2C vs B2B surface**: The same customer surface serves consumers, professionals, and company buyers. Role and professional-verification status — not a separate app — govern feature exposure per Principle 9.
- **Deployment**: All identity data resides in Azure Saudi Arabia Central per ADR-010 with `market_code` on tenant-owned entities.
- **No UI work in 004**: Any customer-facing UI for identity (registration screen, sign-in screen, session list) is either stubbed or defer to Phase 1C spec 014 `customer-app-shell` and the admin UI to Phase 1C spec 015 `admin-foundation`. 004 ships the identity contract, data model, state behavior, and HTTP endpoints; UI composition is later.
- **Seed data**: A `identity-v1` seeder scaffold is authored in 004 per Phase 1A A1 seed framework — a super-admin plus a small set of deterministic synthetic customer identities with varied locales, markets, and verification states — matching `docs/staging-data-policy.md`.

## Dependencies

- **Phase 1A · spec 001 `governance-and-setup`** at DoD — supplies CI skeleton, CODEOWNERS, context-fingerprint verification.
- **Phase 1A · spec 002 `architecture-and-contracts`** at DoD — supplies the shared contract convention (vertical slice + MediatR per ADR-003) and the state-model conventions (Principle 24) that identity will follow for session and OTP challenge states.
- **Phase 1A · spec 003 `shared-foundations`** at DoD — supplies the audit-log module that 004 consumes for all events in FR-021, the file-storage abstraction (not used by 004 itself but referenced by 020), and the test utilities.
- **Phase 1A · A1 environments / Docker / seed** at DoD — supplies three-environment runtime, `ISeeder` contract, and `scripts/dev/up.sh` that the `identity-v1` seeder plugs into.
- **ADR-004 EF Core 9 + code-first migrations** — identity tables follow this ORM convention.
- **ADR-010 Azure KSA residency** — identity data is pinned to this region.
- **Constitution v1.0.0** — Principles 4, 5, 6, 8, 9, 22, 24, 25, 27, 28, 29, 31 all bind this spec.

## Out of Scope (defer explicitly)

- **Verification review workflow** (document upload, reviewer decision UI, rejected-reason catalog) — Phase 1D **spec 020**.
- **Company account management UI and approver routing** — Phase 1D **spec 021**.
- **OTP provider selection and per-market production wiring** — Phase 1E **spec 025** (ADR-009 resolves there).
- **Customer-surface identity UI** (registration screen, sign-in screen, password-reset screen, session-list screen, account-settings screen) — Phase 1C **spec 014**.
- **Admin-surface identity UI** (admin login, invite screen, admin session list, role-editor screen) — Phase 1C **spec 015** (and role editor may land later in admin foundation).
- **SSO / federated login** (Google, Apple, corporate SAML) — not in Phase 1; revisited in Phase 1.5.
- **TOTP/authenticator-app MFA** — see Assumptions on admin MFA; revisited in Phase 1.5 unless raised in `/speckit-clarify`.
- **Biometric unlock on mobile** — Phase 1C spec 014 concern (not identity-contract level).
- **Account deletion and data-export per GDPR-style requests** — Phase 1F hardening concern.

## Phase Assignment and Traceability

- **Phase**: 1B (Core Commerce)
- **Milestone**: 2 (Identity + catalog + search)
- **Lane owner**: Lane A (Claude / Codex) — backend-first
- **Constitution principles in force**: 4 (AR/RTL), 5 (market config), 6 (multi-vendor-ready), 8 (restricted products), 9 (B2B), 22 (tech lock), 24 (state machines: session, OTP), 25 (audit), 27 (UX quality), 28 (AI-build standard), 29 (required spec output), 31 (supremacy)
- **ADRs in force**: ADR-001 (monorepo layout), ADR-003 (vertical slice + MediatR), ADR-004 (EF Core 9), ADR-010 (Azure KSA residency). ADR-009 (OTP provider) remains Proposed; consumed in spec 025.
- **Downstream consumers**: 005 catalog (owner_id), 006 search (indexer actor), 007-a pricing (business pricing per identity), 008 inventory (reservations by identity), 009 cart, 010 checkout, 011 orders, 012 invoices, 013 returns, 014 customer app shell, 015 admin foundation, 020 verification, 021 B2B, 025 notifications, 027 payments.

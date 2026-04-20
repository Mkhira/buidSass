# Identity Domain Events — Audit Log Contract

**Feature**: `specs/phase-1B/004-identity-and-access/spec.md`
**Owned-by**: spec 004 (emits) / spec 003 (persists)
**Transport**: MediatR `INotification` inside the monolith. A future bus swap is out of scope.

All events share the envelope:

```jsonc
{
  "actorId": "uuid | null",         // null for anonymous flows (e.g. registration)
  "actorType": "customer | admin | system",
  "targetId": "uuid | null",        // the subject being acted on
  "targetType": "customer | admin | role | permission | session | null",
  "actionKey": "identity.<event-name>",
  "reason": "string | null",
  "before": "object | null",        // PII redacted per redaction policy below
  "after": "object | null",
  "marketCode": "EG | KSA | null",
  "correlationId": "string",
  "occurredAt": "ISO-8601 timestamp"
}
```

**Redaction policy**: PII (`email`, `phone`, `name`, `password_hash`) is never written to `before`/`after`. Instead, the redacted form `"[redacted]"` is written and the stable id resolves to the actor/target. For `CustomerAnonymized`, all prior `before`/`after` PII in historical events is rewritten to `"[anonymized]"` by the audit-log module in a follow-up pass; raw events are not mutated.

---

## Event catalog

| Action key | Emitter | Notes |
|---|---|---|
| `identity.customer.registered` | `Register` handler | `actorType=system`, `targetType=customer`. No PII in `after`. |
| `identity.customer.verified` | `VerifyContact` handler | |
| `identity.customer.login.succeeded` | Customer `Login` handler | Includes `sessionId` in `after`. |
| `identity.customer.login.failed` | Customer `Login` handler | `actorType=system`, identifier replaced with a salted hash in `before.meta` for correlation without disclosure. |
| `identity.customer.account.locked` | Lockout service | `after.lockTier` ∈ {1, 2}. |
| `identity.customer.account.unlocked` | Lockout service | `after.unlockMethod` ∈ {time, proof}. |
| `identity.customer.password.reset.requested` | `RequestPasswordReset` | Emits regardless of identifier existence (uniform) — if no account, `targetId` is null and `before.meta.uniform=true`. |
| `identity.customer.password.reset.completed` | `CompletePasswordReset` | Sessions revoke event follows. |
| `identity.customer.session.revoked` | `Logout` / `Refresh` reuse / `Disable` / `PasswordReset` / Session admin action | `after.revokeReason`. |
| `identity.customer.deletion.requested` | `RequestDeletion` | `scheduledAnonymizationAt` in `after`. |
| `identity.customer.anonymized` | `Anonymize` | `before` is redacted; `after.idRetained=true`. |
| `identity.admin.created` | `CreateAdmin` | |
| `identity.admin.enabled` | `EnableAdmin` | |
| `identity.admin.disabled` | `DisableAdmin` | Sessions are revoked in-transaction. |
| `identity.admin.login.succeeded` | Admin `Login` | `after.supersededSessionIds` lists prior sessions revoked by newest-wins (FR-011b). |
| `identity.admin.login.failed` | Admin `Login` | |
| `identity.admin.session.revoked` | Various | `reason` ∈ `logout`, `superseded-by-new-login`, `idle-timeout`, `forced-by-role-change`, `refresh-reuse-detected`. |
| `identity.admin.stepup.issued` | `StepUp` | |
| `identity.admin.authorization.denied` | Authorization middleware | `before.requiredPolicy` and `before.requestPath` for forensic replay. |
| `identity.role.created` | `CreateRole` | System roles are seeded by migration and emit with `actorType=system`. |
| `identity.role.updated` | `UpdateRole` | `before.permissionKeys` and `after.permissionKeys` diffable. |
| `identity.role.deleted` | `DeleteRole` | System roles reject delete before emitting. |
| `identity.permission.seeded` | Migration | One event per permission added to the seed catalog. |
| `identity.role.assigned` | `UpdateAdminRoles` | |
| `identity.role.revoked` | `UpdateAdminRoles` | |

---

## Handler ↔ event mapping (trace for implementers)

- FR-022 (audit on role changes, admin enable/disable, password reset initiated/completed, failed authorization) is satisfied by: `role.*`, `admin.enabled`, `admin.disabled`, `customer.password.reset.*`, `admin.authorization.denied`.
- FR-008a (lockout/unlock audit) is satisfied by: `customer.account.locked`, `customer.account.unlocked`.
- FR-011b (admin single-session enforcement) is satisfied by: `admin.login.succeeded` + the follow-on `admin.session.revoked {reason: superseded-by-new-login}`.
- FR-030/031 (deletion + anonymization) is satisfied by: `customer.deletion.requested`, `customer.anonymized`.

Any new identity handler MUST ship with a registered event name in this catalog and a paired audit-log test.

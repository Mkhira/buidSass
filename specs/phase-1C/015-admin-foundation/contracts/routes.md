# Admin Routes

Route table covering the surfaces this spec ships. Future specs (016–019) extend this table without changing the shell.

| Path | Group | Permission required | Notes |
|---|---|---|---|
| `/login` | (auth) | — | Sign-in form. |
| `/mfa` | (auth) | partial-auth-token | TOTP entry; reached only via the partial-auth flow from spec 004. |
| `/mfa/enroll` | (auth) | partial-auth-token | First-time TOTP enrolment per spec 004. |
| `/reset` | (auth) | — | Password reset request. |
| `/reset/confirm?token=…` | (auth) | reset-token | Password reset confirm. |
| `/` | (admin) | session-active | Landing — today's tasks. |
| `/audit` | (admin) | `audit.read` | Audit-log reader (list). Accepts the following filter query params (see FR-018 for semantics): `actor`, `resourceType`, `resourceId`, `actionKey`, `marketScope` (`platform`/`ksa`/`eg`), `from`, `to` (ISO-8601 timestamps), `cursor` (pagination). All filters compose (AND) on the server. The URL is shareable; opening it from a saved view replays the filter set. |
| `/audit?resourceType=<Type>&resourceId=<id>` | (admin) | `audit.read` | The "view audit log for this resource" deep link from the `<AuditForResourceLink>` shell primitive (FR-028f). `<Type>` is one of: `Product`, `Category`, `Brand`, `Manufacturer`, `Sku`, `Warehouse`, `Batch`, `Reservation`, `Order`, `Refund`, `Invoice`, `Customer` — registry maintained in `contracts/audit-redaction.md` (resource-type column). |
| `/audit/[entryId]` | (admin) | `audit.read` | Detail panel as parallel route. |
| `/audit/[entryId]?permalink=1` | (admin) | `audit.read` | Same detail with a copy-confirmation toast on entry. |
| `/me` | (admin) | session-active | Read-only profile + saved-view management. |
| `/me/preferences` | (admin) | session-active | Saved views management UI (server-backed via spec 004). |
| `/__not-found` | (admin) | session-active | Unknown route inside (admin). |
| `/__forbidden` | (admin) | session-active | 403-style screen for permission-denied. |

## Middleware order (`app/middleware.ts`)

1. Resolve locale from cookie / `Accept-Language`. Set `dir` on `<html>`.
2. If route is in `(auth)` group → allow.
3. Else require an active session — read iron-session cookie. Missing / invalid → redirect to `/login?continueTo=<encoded>`.
4. If route declares `requiredPermissions` (via a static map in `lib/auth/permissions.ts`) → check the admin's permission set. Missing → redirect to `/__forbidden`.
5. Proceed to render.

## Permission ↔ route mapping (current)

| Route | Required permissions |
|---|---|
| `/audit` and children | `audit.read` |
| `/me`, `/me/preferences` | (session-active is sufficient) |
| (placeholders for 016–019) | declared by those specs in their own `routes.md` |

## Permalinks

Audit entry permalinks have the form:

```
https://admin.<env>.dental-commerce.com/audit/<entryId>?permalink=1&locale=<en|ar>
```

`?permalink=1` triggers the auto-toast confirmation; `&locale=…` is optional (the receiving admin's cookie wins if present).

# Admin Web

Next.js 14 admin shell for the Dental Commerce Platform — spec [015-admin-foundation](../../specs/phase-1C/015-admin-foundation/spec.md).

This app hosts the admin surface for catalog (016), inventory (017), orders (018), customers (019), and the future verification (020), B2B (021), CMS (022), and notifications (023) modules. It is a separate web application from the customer app per Constitution Principle 20.

## Prerequisites

| Tool | Minimum |
|---|---|
| Node.js | 20.x LTS |
| pnpm | 10.x (via corepack — `corepack enable pnpm`) |
| Docker Desktop | 4.30+ |
| Playwright browsers | `pnpm exec playwright install --with-deps` |

## First-time setup

```bash
cd apps/admin_web

# Install
pnpm install

# Generate API types from the .NET OpenAPI docs (lands T029 of spec 015 Phase 2)
pnpm gen:api

# Sanity checks
pnpm lint
pnpm typecheck
pnpm test
```

## Run against the local backend

```bash
# In one terminal — bring up the Phase 1B backend stack
cd ../../  # repo root
docker compose --profile admin up -d
curl -fsS http://localhost:5000/health  # spec 003 health endpoint

# Then
cd apps/admin_web
pnpm dev   # http://localhost:3001
```

## Environment variables

| Var | Default | Purpose |
|---|---|---|
| `BACKEND_URL` | `http://localhost:5000` | The .NET backend the auth proxy routes to. |
| `STORAGE_BASE_URL` | `http://localhost:5000` | Storage abstraction host (FR-028c CSP `connect-src`). |
| `IRON_SESSION_PASSWORD` | (32-char dev secret) | Cookie-encryption key. Rotated per FR-028d (see Operations below). |
| `IRON_SESSION_PASSWORD_PREV` | (unset) | Previous cookie-encryption key during a rotation window. |
| `NOTIFICATIONS_SSE_URL` | `${BACKEND_URL}/v1/admin/notifications/stream` | Stub-friendly until spec 023 ships. |
| `USE_STATIC_NAV_MANIFEST` | `1` | When `1`, sidebar loads from build-time static contribution files. Flip to `0` when spec 004's `/v1/admin/nav-manifest` endpoint ships (FR-028g). |

## Tests

```bash
pnpm test            # vitest unit / component
pnpm test:visual     # Playwright + Storybook visual regression (lands T046/T047)
pnpm test:a11y       # axe-playwright a11y (lands T046/T047)
pnpm lint:i18n       # FR-013 — no hard-coded user-facing strings
pnpm lint:rtl        # RTL hygiene — no physical margins outside shadcn/ui
pnpm test:e2e        # Playwright end-to-end (lands T067 / T074)
```

## Operations

### Rotating `IRON_SESSION_PASSWORD` (FR-028d)

Dual-secret window — does not log out active admins:

```bash
NEW=$(openssl rand -base64 24 | tr -d '/+=' | head -c 32)
# Step 1: deploy with both
IRON_SESSION_PASSWORD="$NEW"
IRON_SESSION_PASSWORD_PREV="<previous current value>"
# Step 2: wait one full refresh-token TTL (default 14 days from spec 004)
# Step 3: deploy again, removing IRON_SESSION_PASSWORD_PREV
unset IRON_SESSION_PASSWORD_PREV
```

Skipping the wait window logs out every admin still on the old secret.

### Cutting over from static nav-manifest to server-driven (FR-028g)

When spec 004's `/v1/admin/nav-manifest` endpoint lands, set `USE_STATIC_NAV_MANIFEST=0` and redeploy. No code change required.

## Constitutional locks (do not deviate without an ADR amendment)

- **ADR-006**: Next.js + shadcn/ui — no other admin frontend stacks.
- **Principle 7**: design tokens come from `packages/design_system` only — no inline hex literals.
- **Principle 4**: every shell surface ships in editorial-grade Arabic with full RTL.
- **Principle 25**: every audit-emitting action goes through spec 003's audit log; the reader is gated on `audit.read` with field-level redaction per [contracts/audit-redaction.md](../../specs/phase-1C/015-admin-foundation/contracts/audit-redaction.md).

## Known limitations on first launch

- **Notifications**: bell consumes a stub feed until spec 023 ships.
- **Saved views**: persist via spec 004's user-preferences endpoint when shipped; `localStorage` transitional fallback per the **Saved views storage** Assumption.
- **Mobile-web admin**: not optimised — assume ≥ 1280-px viewports.

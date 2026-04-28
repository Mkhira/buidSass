# Quickstart: Admin Foundation

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md)
**Date**: 2026-04-27

Local-dev onboarding for `apps/admin_web/`. The app does not yet exist — it lands when `/speckit-tasks` decomposes the work. This document is the contract for what "ready to develop" looks like.

---

## Prerequisites

| Tool | Minimum |
|---|---|
| Node.js | 20.x LTS |
| pnpm | 9.x |
| Docker Desktop | 4.30+ |
| Playwright browsers | installed via `pnpm exec playwright install --with-deps` |

## First-time setup

```bash
cd apps/admin_web

# Install
pnpm install --frozen-lockfile

# Generate API types from the .NET OpenAPI docs
pnpm gen:api

# Sanity checks (must pass on a fresh checkout)
pnpm lint
pnpm typecheck
pnpm test
```

## Run against the local backend

```bash
# In one terminal — bring up the Phase 1B backend stack
cd <repo-root>
docker compose --profile admin up -d

# Verify
curl -fsS http://localhost:5000/health
```

Then:

```bash
cd apps/admin_web
pnpm dev   # http://localhost:3001
```

Configurable via env (`apps/admin_web/.env.local`):

| Var | Default | Purpose |
|---|---|---|
| `BACKEND_URL` | `http://localhost:5000` | The .NET backend the proxy routes to. |
| `STORAGE_BASE_URL` | `http://localhost:5000` (dev mock) | Storage abstraction host for media + invoice + COA + export downloads. |
| `IRON_SESSION_PASSWORD` | (32-char dev secret) | Cookie-encryption key. Rotated per env per FR-028d. |
| `IRON_SESSION_PASSWORD_PREV` | (unset by default) | Previous cookie-encryption key during a rotation window. Set during steps 1–2 of the rotation runbook below; unset after step 3. Per FR-028d. |
| `NOTIFICATIONS_SSE_URL` | `${BACKEND_URL}/v1/admin/notifications/stream` | Stub-friendly until spec 023 ships. |
| `USE_STATIC_NAV_MANIFEST` | `1` | When `1`, the shell loads sidebar entries from build-time static contribution files. Flip to `0` once spec 004's `/v1/admin/nav-manifest` endpoint ships (FR-028g). |

## Tests

```bash
# Unit / component
pnpm test

# Visual regression (Playwright + Storybook snapshots)
pnpm test:visual
pnpm test:visual --update-snapshots   # only after intentional UI change

# Accessibility (axe across key pages)
pnpm test:a11y

# i18n lint (FR-013)
pnpm lint:i18n

# End-to-end (slower; not on every PR)
pnpm test:e2e
```

## Story-level smoke acceptance

Before opening a PR:

1. **Story 1 (P1) — Sign-in & shell**: log in as a super-admin (with MFA) and a market-scoped admin (no MFA); confirm sidebar entries differ; switch language; log out.
2. **Story 2 (P2) — Audit reader**: open `/audit`, apply each filter category in turn, open an entry, copy permalink, paste into a fresh tab, confirm the same entry resolves.
3. **Story 3 (P3) — AR-RTL**: switch language; walk every shell page + audit pages; confirm RTL + editorial Arabic + locale-correct numerals/dates.
4. **Story 4 (P4) — Bell**: trigger a server-side admin notification (or use the seeded stub); confirm bell badge updates near-real-time and entry deep-links correctly.

## CI pipeline (summary)

`.github/workflows/admin_web-ci.yml` runs on PRs touching `apps/admin_web/**` or `packages/design_system/**`:

1. `pnpm install --frozen-lockfile`
2. `pnpm lint` (ESLint + i18n + no-ad-hoc-fetch + no-physical-margins)
3. `pnpm typecheck`
4. `pnpm test`
5. `pnpm build-storybook && pnpm test:visual`
6. `pnpm test:a11y`
7. `pnpm build` (Next.js standalone build)

The advisory **`impeccable-scan`** (CLAUDE.md, `.github/workflows/impeccable-scan.yml`) targets this app's PRs. Phase 1F spec 029 promotes it to merge-blocking.

## Operations

### Rotating `IRON_SESSION_PASSWORD`

Per spec 015 FR-028d, rotation uses a **dual-secret window** so active sessions don't get logged out:

```bash
# Generate the new secret (any 32-char random string works)
NEW=$(openssl rand -base64 24 | tr -d '/+=' | head -c 32)

# Step 1: deploy with both secrets present, new = current value, prev = old value
IRON_SESSION_PASSWORD="$NEW"
IRON_SESSION_PASSWORD_PREV="<previous current value>"

# Step 2: wait one full refresh-token TTL (default 14 days from spec 004)
#         all sessions sealed under the old secret will have re-sealed under
#         the new secret on their first request after deploy

# Step 3: deploy again, removing IRON_SESSION_PASSWORD_PREV
unset IRON_SESSION_PASSWORD_PREV
```

The middleware decrypts with `IRON_SESSION_PASSWORD` first; on failure it falls back to `IRON_SESSION_PASSWORD_PREV`; a successful previous-secret read triggers an immediate re-seal under the current secret. **Skipping the wait window logs out every admin still on the old secret.** The runbook is enforced by ops process — there is no automated rotation in v1.

### Cutting over from static nav-manifest to server-driven

Spec 015 ships with the static fallback enabled (`USE_STATIC_NAV_MANIFEST=1` per `contracts/nav-manifest.md`). When spec 004's `/v1/admin/nav-manifest` endpoint lands, ops flips the env var to `0`, redeploys, and verifies the sidebar still renders correctly for at least one super-admin and one market-scoped admin. No code change required.

## Known limitations on first launch

- **Notifications**: bell consumes a stub feed until spec 023 ships.
- **Saved views**: persisted via spec 004's user-preferences endpoint. If that endpoint is missing on day 1, the spec falls back to localStorage and files an issue against 004.
- **Mobile-web admin**: not optimised — assume ≥ 1280-px viewports.

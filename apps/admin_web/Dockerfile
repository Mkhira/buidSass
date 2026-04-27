# T009: Multi-stage Dockerfile per Phase 1C-Infra plan.
#
# Three stages:
#   1. deps      — install pnpm + locked dependencies
#   2. builder   — produce the Next.js standalone build artifacts
#   3. runner    — minimal runtime image (non-root uid 10001)
#
# Output: `output: 'standalone'` (next.config.mjs) — the runtime image
# only carries .next/standalone + .next/static + public.

# ---- 1. deps -------------------------------------------------------------
FROM node:20-alpine AS deps
RUN apk add --no-cache libc6-compat
WORKDIR /app
RUN corepack enable && corepack prepare pnpm@10 --activate
COPY apps/admin_web/package.json apps/admin_web/pnpm-lock.yaml ./
RUN pnpm fetch --prod=false
COPY apps/admin_web/ ./
RUN pnpm install --frozen-lockfile --offline --ignore-scripts

# ---- 2. builder ----------------------------------------------------------
FROM node:20-alpine AS builder
WORKDIR /app
RUN corepack enable && corepack prepare pnpm@10 --activate
COPY --from=deps /app/node_modules ./node_modules
COPY apps/admin_web/ ./
COPY packages/design_system /packages/design_system
ENV NEXT_TELEMETRY_DISABLED=1
ENV NODE_ENV=production
RUN pnpm build

# ---- 3. runner -----------------------------------------------------------
FROM node:20-alpine AS runner
WORKDIR /app
ENV NODE_ENV=production
ENV NEXT_TELEMETRY_DISABLED=1
ENV PORT=3000
ENV HOSTNAME=0.0.0.0

# Non-root uid 10001 per Phase 1C-Infra hardening
RUN addgroup --system --gid 10001 nodejs && \
    adduser --system --uid 10001 --ingroup nodejs nextjs

COPY --from=builder --chown=nextjs:nodejs /app/.next/standalone ./
COPY --from=builder --chown=nextjs:nodejs /app/.next/static ./.next/static
COPY --from=builder --chown=nextjs:nodejs /app/public ./public

USER nextjs
EXPOSE 3000

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
  CMD wget --quiet --spider http://localhost:3000/ || exit 1

CMD ["node", "server.js"]

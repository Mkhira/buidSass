# Hand-off Note: Phase 1E → Specs 025 / 026 / 027

**From**: E1 — Infrastructure Integration (spec 028)
**To**: Spec 025 (Notifications), Spec 026 (Shipping), Spec 027 (Payments)
**Purpose**: one-page guide to "what E1 gives you" and "what you owe E1".

This document is the canonical hand-off between the infrastructure substrate
(E1) and the three provider-integration specs that ride on top of it. Read
this BEFORE you write your first line of code for 025/026/027.

---

## §1 — What E1 gives you

### 1.1 Per-environment Azure runtime

A clean-subscription apply of E1's `main.bicep` produces 17+ resources in each
of `rg-dental-stg-ksa` and `rg-dental-prd-ksa`:

- Postgres Flexible Server (private endpoint, no public access)
- Key Vault (RBAC-only, soft-delete + purge-protection)
- ACA managed environment with a backend + admin container apps + EF-migrate job
- Self-hosted Meilisearch container app + Azure File volume
- Log Analytics + App Insights workspace-based pipeline
- User-assigned managed identity (`id-aca-<env>`) attached to backend + admin
- Static Web App for the Flutter customer web bundle (`westeurope`)
- Action group + four metric alerts (deploy-failure, health-probe, high-5xx, kv-anomaly)

### 1.2 Secret slots already provisioned

For each of your three specs, E1 has provisioned the placeholder secret(s) with
sentinel value `__placeholder_set_by_E1__`. You'll find them in
`kv-dental-<env>` tagged `set_by_spec=E1` and `expected_real_value_in=<your-spec-id>`:

| Spec | Logical path | Storage-flattened name |
|---|---|---|
| 027 | `payments/sa/tbd-by-027/api-key` | `payments--sa--tbd-by-027--api-key` |
| 027 | `payments/sa/tbd-by-027/api-secret` | `payments--sa--tbd-by-027--api-secret` |
| 027 | `payments/sa/tbd-by-027/webhook-signing-key` | `payments--sa--tbd-by-027--webhook-signing-key` |
| 027 | `payments/eg/tbd-by-027/...` (3 secrets) | — |
| 026 | `shipping/sa/tbd-by-026/api-key` | `shipping--sa--tbd-by-026--api-key` |
| 026 | `shipping/eg/tbd-by-026/api-key` | `shipping--eg--tbd-by-026--api-key` |
| 025 | `notifications-email/multi/tbd-by-025/api-key` | `notifications-email--multi--tbd-by-025--api-key` |
| 025 | `notifications-sms/sa/tbd-by-025/api-key` | `notifications-sms--sa--tbd-by-025--api-key` |
| 025 | `notifications-sms/eg/tbd-by-025/api-key` | `notifications-sms--eg--tbd-by-025--api-key` |
| 025 | `notifications-push/multi/tbd-by-025/service-account-json` | `notifications-push--multi--tbd-by-025--service-account-json` |

### 1.3 Backend config bootstrap

The backend's `Program.cs` calls `builder.AddLayeredConfiguration()` (spec 003).
That extension reads `KEY_VAULT_URI` from the environment, authenticates as
`id-aca-<env>` (managed identity), and merges KV secrets into `IConfiguration`.
You DO NOT need to write any Azure SDK code — just read your secrets as you
would any other config value.

Example (in `Modules/Notifications/...`):

```csharp
// In your DI registration:
var emailApiKey = configuration["notifications-email--multi--<provider>--api-key"]
    ?? throw new InvalidOperationException("missing notifications email api key");
```

Note: the in-disk KV secret name (with `--`) is what `IConfiguration` exposes.
The logical path (`notifications-email/multi/...`) lives in docs, audit
events, and CI guards only.

### 1.4 Audit event surface

E1 publishes eight event types into `audit_log_entries`:

- `infra.iac.applied`
- `infra.drift.detected`
- `deploy.attempted` / `deploy.completed_succeeded` / `deploy.completed_failed`
- `deploy.rollback`
- `secret.rotated`
- `secret.placeholder_replaced`

The schema is in `specs/phase-1E/.../data-model.md` §3. **You do not write
these events** — E1 owns them. But you'll consume them when correlating a
deploy with provider-side metrics (e.g., "did this 5xx spike start at the
same time as the last deploy?").

### 1.5 CI gates already in place

- `lint-format-infra.yml` — bicep-lint, actionlint, shellcheck, tag-completeness,
  secret-naming, no-client-secret, no-secrets-in-appsettings, fingerprint.
- `deploy-staging.yml` — auto-runs on every `main` merge.
- `deploy-production.yml` — `workflow_dispatch` only, 2-of-N approval.
- `infra-drift.yml` — nightly drift detection.
- `audit-completeness.yml` — weekly deploy-pairing audit.

Your spec MUST NOT break any of these by adding HTTP endpoints under
`AuditEmit*` (T006 guard) or by committing real secret values to
`appsettings*.json` (Phase 5 guard).

---

## §2 — What you owe E1

### 2.1 Replace your placeholder secrets

When your spec picks a concrete provider (e.g., Paymob for payments-sa,
Bosta for shipping-eg, SES for notifications-email), you MUST:

1. **PIM-elevate** to Key Vault Secrets Officer on the target vault.
2. **Delete the placeholder** at `<domain>/<market>/tbd-by-<NNN>/<key>`.
3. **Create a new secret** at `<domain>/<market>/<your-provider>/<key>` with
   the real provider credential value, tagged with:
   - `set_by_spec=<NNN>` (your spec id, NOT E1)
   - `rotated_at=<ISO-8601 timestamp>`
   - `rotation_cadence_days=<90 unless otherwise specified>`
4. **Emit a `secret.placeholder_replaced`** audit event:
   ```bash
   bash scripts/azure/audit-emit.sh \
     --env staging \
     --event-type secret.placeholder_replaced \
     --payload '{"vault_name":"kv-dental-stg","secret_name":"<new-name>","replaced_by_spec":"<NNN>","actor_oid":"<your-oid>"}'
   ```

E1 does NOT auto-emit this event — your spec OWNS that emission (per the
note at the bottom of data-model.md §2).

### 2.2 Per-provider integration-specific audit events

Each of your three specs defines its own domain audit events (e.g.,
`payment.attempted`, `shipment.created`, `notification.sent`). These ride
alongside E1's events in the SAME `audit_log_entries` table. Your spec's
event_type values MUST be namespaced to avoid collision:
- `payments.*` for spec 027
- `shipping.*` for spec 026
- `notifications.*` for spec 025

E1 reserves the `infra.*`, `deploy.*`, and `secret.*` namespaces.

### 2.3 Health probe registration

If your provider integration exposes ANY new HTTP surface on the backend
(e.g., a webhook receiver at `/webhooks/payments/paymob`), the backend's
`/health` endpoint MUST continue to return 200 even if the provider is
unreachable. E1's smoke probe 01 fails the deploy if `/health` returns
non-200; do NOT couple your provider liveness to the platform health probe.

Use a SEPARATE liveness signal (e.g., `/health/payments` returning 200 + a
JSON body indicating provider reachability) for provider-specific probes.

---

## §3 — Quick-start checklist for your spec

Before opening your first PR against this hand-off contract:

- [ ] Read `infra/azure/DECISIONS.md` (the five clarify-locked + three
      deferred-default decisions you inherit).
- [ ] Read `infra/azure/RUNBOOK.md` §a (secret rotation procedure).
- [ ] Confirm your provider's webhook signing-key length + rotation cadence
      matches DECISIONS.md DD-2 (5-minute secret cache). If your provider
      rotates webhook signing keys faster than every 5 minutes, file a
      runbook-improvement issue and propose a per-secret TTL override.
- [ ] Audit `KEY_VAULT_URI` consumption: your code MUST use
      `IConfiguration` (NOT `IKeyVaultClient` directly). The
      `AddLayeredConfiguration` extension handles the rest.
- [ ] Emit a `secret.placeholder_replaced` audit event for EVERY placeholder
      slot you populate (one event per secret, not one per provider).

---

## §4 — Escalation

If your spec discovers an E1 limitation that blocks delivery:

1. **File an issue** with the `infrastructure-blocker` label.
2. **Tag** `@Mkhira` (platform-eng).
3. **Propose** the smallest possible amendment to spec 028 that unblocks you
   (per Principle 32 amendment procedure).

E1 is intentionally minimal-surface-area: changing it ripples into 025/026/027
+ 029. Resist temptation to "just add one knob" without going through the
amendment loop.

---

## §5 — Versioning

This hand-off note is **stable**. Breaking changes (e.g., changing
`AddLayeredConfiguration` semantics, removing an audit event type, changing
the secret-naming taxonomy regex) require:
- A spec 028 amendment.
- A coordinated migration plan for 025/026/027.
- A bumped version banner here.

**Version**: 1.0.0 — initial hand-off (E1 at exit).

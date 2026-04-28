# Client-emitted events

Same pattern as the customer app — events emitted into `TelemetryAdapter`. v1 = `NoopAdapter` (production) + `ConsoleAdapter` (Dev). Real provider lands in spec 023 / observability spec.

## Event vocabulary

| Event | Trigger | Properties |
|---|---|---|
| `admin.cold_start` | First Server-Component render after sign-in | `locale`, `market_scope`, `cold_load_ms` |
| `admin.login.started` | `/login` form submitted | — |
| `admin.login.success` | Spec 004 sign-in 2xx | — |
| `admin.login.failure` | Spec 004 sign-in 4xx | `reason_code` |
| `admin.mfa.required` | Spec 004 returns `mfa_required` | — |
| `admin.mfa.success` | Spec 004 MFA 2xx | — |
| `admin.mfa.failure` | Spec 004 MFA 4xx | `reason_code` |
| `admin.refresh.success` | Cookie rotation succeeded | — |
| `admin.refresh.failure` | Cookie rotation failed | `reason_code` |
| `admin.logout` | `/api/auth/logout` returned 2xx | — |
| `admin.locale.toggled` | Locale cookie changed via top-bar toggle | `from`, `to` |
| `admin.nav.entry.clicked` | Sidebar entry navigated to | `entry_id` |
| `admin.global_search.opened` | Global search affordance opened | — |
| `admin.global_search.queried` | Search submitted | `result_kind_count` |
| `admin.audit.list.opened` | `/audit` rendered | — |
| `admin.audit.filter.applied` | Filter changed | `filter_keys` (sorted strings, not values) |
| `admin.audit.entry.opened` | Detail panel opened | `entry_id_hash` (sha-256 truncated, not raw id) |
| `admin.audit.permalink.copied` | Permalink-copy succeeded | — |
| `admin.bell.opened` | Bell dropdown opened | `unread_count_at_open` |
| `admin.bell.entry.clicked` | Notification entry clicked | `kind_key` |
| `admin.bell.sse.connected` | SSE first message received | — |
| `admin.bell.sse.reconnect_attempt` | SSE reconnection started | `attempt_n` |
| `admin.bell.sse.fallback_to_polling` | SSE retries exhausted | — |
| `admin.error.boundary` | Unhandled exception caught by error boundary | `digest` (Next.js error digest, not the message) |

## PII guard rails

- Events MUST NOT carry email, full name, raw resource ids, before/after JSON, free-text user input, or filter values.
- `reason_code` values come from a closed enum (e.g. `identity.lockout.active`).
- `entry_id_hash` is `sha-256(entryId).slice(0, 16)` — not the raw id.
- `result_kind_count` is a count of matched kinds, not a list of result ids.
- Test `tests/unit/observability/pii-guard.test.ts` asserts every event's property set against this allow-list.

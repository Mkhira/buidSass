/**
 * T044 (FR-025 / contracts/client-events.md): TelemetryAdapter interface +
 * `NoopAdapter` (production default) + `ConsoleAdapter` (dev only).
 *
 * Real provider lands in spec 023 / observability spec. The interface
 * exists so wiring telemetry later is one composition-root swap, not a
 * feature-folder rewrite.
 *
 * PII guard rails are enforced by `tests/unit/observability/pii-guard.test.ts`
 * (T045) — the test asserts every event's property set against the
 * allow-list in `contracts/client-events.md`.
 */

export type TelemetryEvent =
  | { name: "admin.cold_start"; properties: { locale: "en" | "ar"; market_scope: "platform" | "ksa" | "eg"; cold_load_ms: number } }
  | { name: "admin.login.started"; properties?: never }
  | { name: "admin.login.success"; properties?: never }
  | { name: "admin.login.failure"; properties: { reason_code: string } }
  | { name: "admin.mfa.required"; properties?: never }
  | { name: "admin.mfa.success"; properties?: never }
  | { name: "admin.mfa.failure"; properties: { reason_code: string } }
  | { name: "admin.refresh.success"; properties?: never }
  | { name: "admin.refresh.failure"; properties: { reason_code: string } }
  | { name: "admin.logout"; properties?: never }
  | { name: "admin.locale.toggled"; properties: { from: "en" | "ar"; to: "en" | "ar" } }
  | { name: "admin.nav.entry.clicked"; properties: { entry_id: string } }
  | { name: "admin.global_search.opened"; properties?: never }
  | { name: "admin.global_search.queried"; properties: { result_kind_count: number } }
  | { name: "admin.audit.list.opened"; properties?: never }
  | { name: "admin.audit.filter.applied"; properties: { filter_keys: string[] } }
  | { name: "admin.audit.entry.opened"; properties: { entry_id_hash: string } }
  | { name: "admin.audit.permalink.copied"; properties?: never }
  | { name: "admin.bell.opened"; properties: { unread_count_at_open: number } }
  | { name: "admin.bell.entry.clicked"; properties: { kind_key: string } }
  | { name: "admin.bell.sse.connected"; properties?: never }
  | { name: "admin.bell.sse.reconnect_attempt"; properties: { attempt_n: number } }
  | { name: "admin.bell.sse.fallback_to_polling"; properties?: never }
  | { name: "admin.error.boundary"; properties: { digest: string } }
  | { name: "customers.pii.field.rendered"; properties: { mode: "masked" | "unmasked"; kind: "email" | "phone" | "generic" } };

export interface TelemetryAdapter {
  emit(event: TelemetryEvent): void;
}

export class NoopAdapter implements TelemetryAdapter {
  emit(_event: TelemetryEvent): void {
    // intentionally no-op
  }
}

export class ConsoleAdapter implements TelemetryAdapter {
  emit(event: TelemetryEvent): void {
    if (typeof console !== "undefined") {
      // eslint-disable-next-line no-console
      console.debug(`[telemetry] ${event.name}`, "properties" in event ? event.properties : undefined);
    }
  }
}

let activeAdapter: TelemetryAdapter = process.env.NODE_ENV === "production"
  ? new NoopAdapter()
  : new ConsoleAdapter();

export function setTelemetryAdapter(adapter: TelemetryAdapter): void {
  activeAdapter = adapter;
}

export function emitTelemetry(event: TelemetryEvent): void {
  activeAdapter.emit(event);
}

/**
 * Allow-list of properties per event name. The PII-guard test (T045)
 * enforces that no other property keys land in production.
 */
export const ALLOWED_PROPERTIES: Record<string, readonly string[]> = {
  "admin.cold_start": ["locale", "market_scope", "cold_load_ms"],
  "admin.login.started": [],
  "admin.login.success": [],
  "admin.login.failure": ["reason_code"],
  "admin.mfa.required": [],
  "admin.mfa.success": [],
  "admin.mfa.failure": ["reason_code"],
  "admin.refresh.success": [],
  "admin.refresh.failure": ["reason_code"],
  "admin.logout": [],
  "admin.locale.toggled": ["from", "to"],
  "admin.nav.entry.clicked": ["entry_id"],
  "admin.global_search.opened": [],
  "admin.global_search.queried": ["result_kind_count"],
  "admin.audit.list.opened": [],
  "admin.audit.filter.applied": ["filter_keys"],
  "admin.audit.entry.opened": ["entry_id_hash"],
  "admin.audit.permalink.copied": [],
  "admin.bell.opened": ["unread_count_at_open"],
  "admin.bell.entry.clicked": ["kind_key"],
  "admin.bell.sse.connected": [],
  "admin.bell.sse.reconnect_attempt": ["attempt_n"],
  "admin.bell.sse.fallback_to_polling": [],
  "admin.error.boundary": ["digest"],
  "customers.pii.field.rendered": ["mode", "kind"],
};

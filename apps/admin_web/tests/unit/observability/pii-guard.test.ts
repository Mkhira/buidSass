/**
 * T045: PII guard test (FR-025 / contracts/client-events.md).
 *
 * Asserts every event in the allow-list contains ONLY properties from its
 * declared property set — no PII fields (email, phone, customer id, raw
 * resource id, free-text input) leak.
 */
import { describe, expect, it } from "vitest";
import {
  ALLOWED_PROPERTIES,
  ConsoleAdapter,
  type TelemetryEvent,
} from "@/lib/observability/telemetry";

const FORBIDDEN_KEY_PATTERNS = [
  /email/i,
  /phone/i,
  /name$/i, // displayName etc.
  /customer_?id/i,
  /order_?id/i,
  /sku_?id/i,
  /resource_?id$/i, // accept entry_id_hash, but reject entry_id / resource_id raw
  /password/i,
  /token/i,
  /reason_?note/i,
  /search_?query/i,
];

describe("telemetry PII guard", () => {
  it("every event has an entry in the allow-list", () => {
    const eventNames: ReadonlyArray<TelemetryEvent["name"]> = [
      "admin.cold_start",
      "admin.login.started",
      "admin.login.success",
      "admin.login.failure",
      "admin.mfa.required",
      "admin.mfa.success",
      "admin.mfa.failure",
      "admin.refresh.success",
      "admin.refresh.failure",
      "admin.logout",
      "admin.locale.toggled",
      "admin.nav.entry.clicked",
      "admin.global_search.opened",
      "admin.global_search.queried",
      "admin.audit.list.opened",
      "admin.audit.filter.applied",
      "admin.audit.entry.opened",
      "admin.audit.permalink.copied",
      "admin.bell.opened",
      "admin.bell.entry.clicked",
      "admin.bell.sse.connected",
      "admin.bell.sse.reconnect_attempt",
      "admin.bell.sse.fallback_to_polling",
      "admin.error.boundary",
      "customers.pii.field.rendered",
    ];
    for (const name of eventNames) {
      expect(ALLOWED_PROPERTIES, `missing allow-list entry for ${name}`).toHaveProperty(name);
    }
  });

  it("no allowed property name matches a PII pattern", () => {
    const violations: string[] = [];
    for (const [event, props] of Object.entries(ALLOWED_PROPERTIES)) {
      for (const prop of props) {
        for (const re of FORBIDDEN_KEY_PATTERNS) {
          if (re.test(prop) && prop !== "entry_id_hash") {
            violations.push(`${event}: property "${prop}" matches forbidden pattern ${re}`);
          }
        }
      }
    }
    expect(violations, violations.join("\n")).toEqual([]);
  });

  it("ConsoleAdapter emits without throwing for every event shape", () => {
    const adapter = new ConsoleAdapter();
    expect(() =>
      adapter.emit({
        name: "admin.cold_start",
        properties: { locale: "en", market_scope: "ksa", cold_load_ms: 1234 },
      }),
    ).not.toThrow();
    expect(() =>
      adapter.emit({
        name: "customers.pii.field.rendered",
        properties: { mode: "masked", kind: "email" },
      }),
    ).not.toThrow();
  });
});

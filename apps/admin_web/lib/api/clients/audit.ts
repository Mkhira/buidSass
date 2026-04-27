/**
 * T029: Thin wrapper over spec 003's audit-read endpoint surface.
 */
import { proxyFetch } from "@/lib/api/proxy";

export interface AuditEntry {
  id: string;
  actor: { id: string; email: string; role: string };
  actionKey: string;
  resourceType: string;
  resourceId: string;
  marketScope: "platform" | "ksa" | "eg";
  correlationId: string;
  before: unknown;
  after: unknown;
  occurredAt: string; // ISO-8601
}

export interface AuditFilter {
  actor?: string;
  resourceType?: string;
  resourceId?: string;
  actionKey?: string;
  marketScope?: "platform" | "ksa" | "eg";
  from?: string; // ISO-8601
  to?: string; // ISO-8601
  cursor?: string;
}

export interface AuditPage {
  entries: AuditEntry[];
  nextCursor: string | null;
}

export const auditApi = {
  list: (filter: AuditFilter) => {
    const qs = new URLSearchParams();
    for (const [k, v] of Object.entries(filter)) {
      if (v !== undefined && v !== null && v !== "") qs.set(k, String(v));
    }
    const suffix = qs.toString() ? `?${qs.toString()}` : "";
    return proxyFetch<AuditPage>(`/v1/admin/audit-log${suffix}`);
  },
  byId: (entryId: string) => proxyFetch<AuditEntry>(`/v1/admin/audit-log/${encodeURIComponent(entryId)}`),
};

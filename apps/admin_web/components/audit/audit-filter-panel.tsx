/**
 * T063 (FR-018): AuditFilterPanel — Client Component for the audit
 * reader's compose-AND filter set. URL-synced — every filter change
 * pushes a new query string so the view is shareable + back-button-friendly.
 *
 * Filters: actor, resourceType, resourceId, actionKey, marketScope,
 * timeframe (from/to). Defaults to last 7 days when no timeframe is
 * present in the URL.
 */
"use client";

import { useRouter, useSearchParams } from "next/navigation";
import { useTranslations } from "next-intl";
import { useMemo, useState, useTransition } from "react";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { emitTelemetry } from "@/lib/observability/telemetry";

const FILTER_KEYS = ["actor", "resourceType", "resourceId", "actionKey", "marketScope", "from", "to"] as const;
type FilterKey = (typeof FILTER_KEYS)[number];
type FilterMap = Partial<Record<FilterKey, string>>;

const MARKETS = ["platform", "ksa", "eg"] as const;

function defaultLast7Days(): { from: string; to: string } {
  const to = new Date();
  const from = new Date(to.getTime() - 7 * 24 * 60 * 60 * 1000);
  return { from: from.toISOString(), to: to.toISOString() };
}

export interface AuditFilterPanelProps {
  /** Initial filter shape — provided by the Server Component from URL params. */
  initial: FilterMap;
}

export function AuditFilterPanel({ initial }: AuditFilterPanelProps) {
  const t = useTranslations("audit.filter");
  const router = useRouter();
  const searchParams = useSearchParams();
  const [draft, setDraft] = useState<FilterMap>(initial);
  const [isPending, startTransition] = useTransition();

  const activeKeys = useMemo(
    () => FILTER_KEYS.filter((k) => Boolean(initial[k])).sort(),
    [initial],
  );

  function update(key: FilterKey, value: string) {
    setDraft((prev) => ({ ...prev, [key]: value || undefined }));
  }

  function apply() {
    const params = new URLSearchParams(searchParams.toString());
    for (const key of FILTER_KEYS) {
      const value = draft[key];
      if (value && value.length > 0) params.set(key, value);
      else params.delete(key);
    }
    params.delete("cursor"); // any filter change resets pagination
    emitTelemetry({
      name: "admin.audit.filter.applied",
      properties: { filter_keys: FILTER_KEYS.filter((k) => draft[k]).slice().sort() },
    });
    startTransition(() => router.push(`/audit?${params.toString()}`));
  }

  function clear() {
    setDraft({});
    startTransition(() => router.push("/audit"));
  }

  return (
    <section
      aria-label={t("clear")}
      className="space-y-ds-md rounded-md border border-border bg-card p-ds-md"
    >
      <div className="grid gap-ds-md sm:grid-cols-2 lg:grid-cols-3">
        <FilterField id="actor" label={t("actor")} value={draft.actor ?? ""} onChange={(v) => update("actor", v)} />
        <FilterField
          id="resourceType"
          label={t("resource_type")}
          value={draft.resourceType ?? ""}
          onChange={(v) => update("resourceType", v)}
        />
        <FilterField
          id="resourceId"
          label={t("resource_id")}
          value={draft.resourceId ?? ""}
          onChange={(v) => update("resourceId", v)}
        />
        <FilterField
          id="actionKey"
          label={t("action")}
          value={draft.actionKey ?? ""}
          onChange={(v) => update("actionKey", v)}
        />
        <div className="space-y-ds-xs">
          <Label htmlFor="marketScope">{t("market")}</Label>
          <select
            id="marketScope"
            className="h-7 w-full rounded-md border border-border bg-background px-ds-sm text-sm"
            value={draft.marketScope ?? ""}
            onChange={(e) => update("marketScope", e.target.value)}
          >
            <option value=""></option>
            {MARKETS.map((m) => (
              <option key={m} value={m}>
                {m}
              </option>
            ))}
          </select>
        </div>
        <FilterField
          id="from"
          label={t("from")}
          type="datetime-local"
          value={isoToLocal(draft.from)}
          onChange={(v) => update("from", v ? new Date(v).toISOString() : "")}
        />
        <FilterField
          id="to"
          label={t("to")}
          type="datetime-local"
          value={isoToLocal(draft.to)}
          onChange={(v) => update("to", v ? new Date(v).toISOString() : "")}
        />
      </div>

      <div className="flex items-center justify-between gap-ds-md">
        <div className="flex flex-wrap gap-ds-xs">
          {activeKeys.map((k) => (
            <Badge key={k} variant="secondary" className="font-mono text-xs">
              {k}
            </Badge>
          ))}
        </div>
        <div className="flex items-center gap-ds-sm">
          <Button type="button" variant="ghost" size="sm" onClick={clear} disabled={isPending}>
            {t("clear")}
          </Button>
          <Button type="button" size="sm" onClick={apply} disabled={isPending} aria-busy={isPending}>
            Apply
          </Button>
        </div>
      </div>
    </section>
  );
}

interface FilterFieldProps {
  id: string;
  label: string;
  value: string;
  type?: string;
  onChange: (value: string) => void;
}

function FilterField({ id, label, value, type = "text", onChange }: FilterFieldProps) {
  return (
    <div className="space-y-ds-xs">
      <Label htmlFor={id}>{label}</Label>
      <Input id={id} type={type} value={value} onChange={(e) => onChange(e.target.value)} />
    </div>
  );
}

function isoToLocal(iso: string | undefined): string {
  if (!iso) return "";
  try {
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return "";
    // datetime-local input expects "YYYY-MM-DDTHH:mm"
    const pad = (n: number) => String(n).padStart(2, "0");
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
  } catch {
    return "";
  }
}

export { defaultLast7Days, FILTER_KEYS };

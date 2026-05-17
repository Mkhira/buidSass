/**
 * Spec 025 T048 — admin provider-routing operator surface. Scaffold-only;
 * full UI is Phase 7+ work.
 */
import { getTranslations } from "next-intl/server";
import { PageHeader } from "@/components/shell/page-header";

const ENDPOINTS = [
  { method: "GET", path: "/admin/notifications/provider-routing/{market}/{channel}", labelKey: "get" },
  { method: "PUT", path: "/admin/notifications/provider-routing/{market}/{channel}", labelKey: "set" },
  { method: "POST", path: "/admin/notifications/provider-routing/{market}/{channel}:failover", labelKey: "failover" },
] as const;

export default async function ProviderRoutingPage() {
  const t = await getTranslations("notifications.provider_routing");
  return (
    <div className="space-y-ds-lg">
      <PageHeader title={t("title")} description={t("description")} />
      <div className="rounded-md border border-dashed border-border p-ds-lg text-sm text-muted-foreground">
        <p>{t("scaffold_note")}</p>
        <ul className="mt-ds-sm list-disc space-y-ds-xs ps-ds-md">
          {ENDPOINTS.map((e) => (
            <li key={`${e.method}-${e.path}`}>
              <code>{`${e.method} ${e.path}`}</code>
              {` — ${t(`endpoints.${e.labelKey}`)}`}
            </li>
          ))}
        </ul>
        <p className="mt-ds-sm">{t("failover_note")}</p>
      </div>
    </div>
  );
}

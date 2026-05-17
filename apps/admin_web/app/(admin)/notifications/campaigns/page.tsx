/**
 * Spec 025 T040 — admin campaigns. Scaffold-only delivery; full UI is
 * Phase 7+ work.
 */
import { getTranslations } from "next-intl/server";
import { PageHeader } from "@/components/shell/page-header";

const ENDPOINTS = [
  { method: "POST", path: "/admin/notifications/campaigns", labelKey: "create_draft" },
  { method: "POST", path: "/admin/notifications/campaigns/{id}:schedule", labelKey: "schedule" },
  { method: "POST", path: "/admin/notifications/campaigns/{id}:<pause|resume|cancel>", labelKey: "pause_resume_cancel" },
  { method: "GET", path: "/admin/notifications/campaigns/{id}/report", labelKey: "report" },
] as const;

export default async function CampaignsPage() {
  const t = await getTranslations("notifications.campaigns");
  return (
    <div className="space-y-ds-lg">
      <PageHeader title={t("title")} description={t("description")} />
      <div className="rounded-md border border-dashed border-border p-ds-lg text-sm text-muted-foreground">
        <p>{t("scaffold_note")}</p>
        <ul className="mt-ds-sm list-disc space-y-ds-xs pl-ds-md">
          {ENDPOINTS.map((e) => (
            <li key={`${e.method}-${e.path}`}>
              <code>{`${e.method} ${e.path}`}</code>
              {` — ${t(`endpoints.${e.labelKey}`)}`}
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
}

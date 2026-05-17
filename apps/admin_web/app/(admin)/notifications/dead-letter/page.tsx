/**
 * Spec 025 T048 — admin dead-letter operator surface. Scaffold-only;
 * full UI is Phase 7+ work.
 */
import { getTranslations } from "next-intl/server";
import { PageHeader } from "@/components/shell/page-header";

const ENDPOINTS = [
  { method: "GET", path: "/admin/notifications/dead-letter", labelKey: "list" },
  { method: "POST", path: "/admin/notifications/dead-letter/{id}:retry", labelKey: "retry" },
  { method: "POST", path: "/admin/notifications/dead-letter/{id}:discard", labelKey: "discard" },
] as const;

export default async function DeadLetterPage() {
  const t = await getTranslations("notifications.dead_letter");
  return (
    <div className="space-y-ds-lg">
      <PageHeader title={t("title")} description={t("description")} />
      <div className="rounded-md border border-dashed border-border p-ds-lg text-sm text-muted-foreground">
        <p>{t("scaffold_note")}</p>
        <ul className="mt-ds-sm list-disc space-y-ds-xs ps-ds-md">
          {ENDPOINTS.map((e) => (
            <li key={`${e.method}-${e.path}`}>
              <code dir="ltr" className="font-mono">{`${e.method} ${e.path}`}</code>
              {` — ${t(`endpoints.${e.labelKey}`)}`}
            </li>
          ))}
        </ul>
        <p className="mt-ds-sm">{t("retention_note")}</p>
      </div>
    </div>
  );
}

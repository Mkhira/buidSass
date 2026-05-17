/**
 * Spec 025 T019 — admin templates list + editor + review board.
 *
 * Scaffold-only delivery. Full UI is Phase 7+ work. API path literals are
 * URL routes, not user-facing copy — they live in a typed constant outside
 * the JSX tree so the i18n linter only inspects translated descriptive text.
 */
import { getTranslations } from "next-intl/server";
import { PageHeader } from "@/components/shell/page-header";

const ENDPOINTS = [
  { method: "POST", path: "/admin/notifications/templates", labelKey: "create_draft" },
  { method: "POST", path: "/admin/notifications/templates/{id}:submit", labelKey: "submit" },
  { method: "POST", path: "/admin/notifications/templates/{id}:approve", labelKey: "approve" },
  { method: "POST", path: "/admin/notifications/templates/{id}:reject", labelKey: "reject" },
  { method: "POST", path: "/admin/notifications/templates/{id}:archive", labelKey: "archive" },
] as const;

export default async function TemplatesPage() {
  const t = await getTranslations("notifications.templates");
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
      </div>
    </div>
  );
}

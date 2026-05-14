/**
 * Spec 026 (T045) — exception queue scaffold (dead-letter labels + disputes).
 * Backend: `GET /v1/admin/shipping/dead-letter-labels`,
 * `POST .../{id}:retry|:discard`, `POST .../shipments/{id}:dispute|:create-re-delivery`.
 */
import { getTranslations } from "next-intl/server";
import { PageHeader } from "@/components/shell/page-header";

export default async function ExceptionQueuePage() {
  const t = await getTranslations("shipping.exception_queue");
  return (
    <div className="space-y-ds-lg">
      <PageHeader title={t("title")} description={t("description")} />
      <div className="rounded-md border border-border bg-card p-ds-lg text-sm text-muted-foreground">
        {t("empty_state")}
      </div>
    </div>
  );
}

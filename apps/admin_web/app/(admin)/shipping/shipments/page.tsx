/**
 * Spec 026 (T045) — shipments list + detail scaffold.
 * Backend exposes `GET /v1/admin/shipping/shipments` with state +
 * provider filters; this scaffold renders the empty-state framing.
 */
import { getTranslations } from "next-intl/server";
import { PageHeader } from "@/components/shell/page-header";

export default async function ShipmentsPage() {
  const t = await getTranslations("shipping.shipments");
  return (
    <div className="space-y-ds-lg">
      <PageHeader title={t("title")} description={t("description")} />
      <div className="rounded-md border border-border bg-card p-ds-lg text-sm text-muted-foreground">
        {t("empty_state")}
      </div>
    </div>
  );
}

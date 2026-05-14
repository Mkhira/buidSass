/**
 * Spec 026 (T014) — shipping methods list + editor + review-board scaffold.
 *
 * V1 scaffold: surfaces the empty-state framing for method authoring.
 * The backend already exposes `POST /v1/admin/shipping/methods`,
 * `POST /v1/admin/shipping/methods/{id}/fee-tables` and the lifecycle
 * verbs (submit/approve/reject/archive). Wiring the data layer + table
 * is deferred to the post-launch admin-UI polish pass.
 */
import { getTranslations } from "next-intl/server";
import { PageHeader } from "@/components/shell/page-header";

export default async function ShippingMethodsPage() {
  const t = await getTranslations("shipping.methods");
  return (
    <div className="space-y-ds-lg">
      <PageHeader title={t("title")} description={t("description")} />
      <div className="rounded-md border border-border bg-card p-ds-lg text-sm text-muted-foreground">
        {t("empty_state")}
      </div>
    </div>
  );
}

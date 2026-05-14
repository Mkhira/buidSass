/**
 * Spec 026 (T015) — shipping zone editor scaffold.
 * Backend: `POST /v1/admin/shipping/zones` (CreateZone slice). Detailed
 * map / postal-code editor deferred to admin-UI polish.
 */
import { getTranslations } from "next-intl/server";
import { PageHeader } from "@/components/shell/page-header";

export default async function ShippingZonesPage() {
  const t = await getTranslations("shipping.zones");
  return (
    <div className="space-y-ds-lg">
      <PageHeader title={t("title")} description={t("description")} />
      <div className="rounded-md border border-border bg-card p-ds-lg text-sm text-muted-foreground">
        {t("empty_state")}
      </div>
    </div>
  );
}

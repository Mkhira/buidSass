/**
 * Spec 026 (T045) — provider routing + manual-failover scaffold.
 * Backend: `GET/PUT /v1/admin/shipping/provider-routing` +
 * `POST /v1/admin/shipping/provider-routing/{market}/{method}:failover`.
 */
import { getTranslations } from "next-intl/server";
import { PageHeader } from "@/components/shell/page-header";

export default async function ProviderRoutingPage() {
  const t = await getTranslations("shipping.provider_routing");
  return (
    <div className="space-y-ds-lg">
      <PageHeader title={t("title")} description={t("description")} />
      <div className="rounded-md border border-border bg-card p-ds-lg text-sm text-muted-foreground">
        {t("empty_state")}
      </div>
    </div>
  );
}

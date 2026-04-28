import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { requirePermission } from "@/lib/auth/guards";
import { hasPermission } from "@/lib/auth/permissions";
import { getSession } from "@/lib/auth/session";
import { PageHeader } from "@/components/shell/page-header";
import { AuditForResourceLink } from "@/components/shell/audit-for-resource-link";
import { ErrorState } from "@/components/shell/error-state";
import { MaskedField } from "@/components/shell/masked-field";
import { customersApi } from "@/lib/api/clients/customers";
import { AccountActionBar } from "@/components/customers/profile/account-action-bar";

export default async function CustomerProfilePage({
  params,
}: {
  params: { customerId: string };
}) {
  await requirePermission(
    ["customers.read"],
    `/customers/${params.customerId}`,
  );
  const session = await getSession();
  const t = await getTranslations("customers.profile");

  let detail;
  let detailError: string | undefined;
  try {
    detail = await customersApi.detail(params.customerId);
  } catch (e) {
    const message = e instanceof Error ? e.message : "unknown";
    if (message.includes("404")) notFound();
    detailError = message;
  }

  if (detailError || !detail) {
    return (
      <div className="space-y-ds-lg">
        <PageHeader title={params.customerId} />
        <ErrorState reasonCode={detailError ?? "unknown"} />
      </div>
    );
  }

  const canReadPii = hasPermission(session, "customers.pii.read");

  return (
    <div className="space-y-ds-lg">
      <PageHeader
        title={t("title", { name: detail.displayName })}
        actions={
          <AuditForResourceLink
            resourceType="Customer"
            resourceId={detail.id}
            canRead={hasPermission(session, "audit.read")}
          />
        }
      />
      <section className="grid gap-ds-md md:grid-cols-2">
        <div className="rounded-md border border-border bg-card p-ds-md">
          <p className="text-xs uppercase text-muted-foreground">
            {t("email")}
          </p>
          <p>
            <MaskedField kind="email" value={detail.email} canRead={canReadPii} />
          </p>
        </div>
        <div className="rounded-md border border-border bg-card p-ds-md">
          <p className="text-xs uppercase text-muted-foreground">
            {t("phone")}
          </p>
          <p>
            <MaskedField kind="phone" value={detail.phone} canRead={canReadPii} />
          </p>
        </div>
      </section>
      <AccountActionBar
        customerId={detail.id}
        rowVersion={detail.rowVersion}
        currentSessionAdminId={session?.adminId ?? ""}
        permissions={session?.permissions ?? []}
        accountState={detail.accountState}
      />
      <section>
        <h2 className="text-lg font-semibold">{t("addresses")}</h2>
        <p className="text-sm text-muted-foreground">
          {detail.addressesCount} {t("addresses")}
        </p>
      </section>
      <section>
        <h2 className="text-lg font-semibold">{t("orders")}</h2>
        <p className="text-sm">{detail.ordersSummary.count}</p>
      </section>
    </div>
  );
}

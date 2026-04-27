/**
 * T029 — order detail (Server Component).
 */
import { getLocale, getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { requirePermission } from "@/lib/auth/guards";
import { hasPermission } from "@/lib/auth/permissions";
import { getSession } from "@/lib/auth/session";
import { PageHeader } from "@/components/shell/page-header";
import { AuditForResourceLink } from "@/components/shell/audit-for-resource-link";
import { ErrorState } from "@/components/shell/error-state";
import { ordersApi } from "@/lib/api/clients/orders";
import { FourStatePillRow } from "@/components/orders/list/four-state-pill-row";
import { TransitionActionBar } from "@/components/orders/detail/transition-action-bar";
import { Timeline } from "@/components/orders/detail/timeline";

export default async function OrderDetailPage({
  params,
}: {
  params: { orderId: string };
}) {
  await requirePermission(["orders.read"], `/orders/${params.orderId}`);
  const session = await getSession();
  const t = await getTranslations("orders.detail");
  const locale = (await getLocale()) === "ar" ? "ar" : "en";

  let detail;
  let detailError: string | undefined;
  let timeline: Awaited<ReturnType<typeof ordersApi.timeline>> = {
    rows: [],
    nextCursor: null,
  };
  try {
    detail = await ordersApi.detail(params.orderId);
  } catch (e) {
    const message = e instanceof Error ? e.message : "unknown";
    if (message.includes("404")) notFound();
    detailError = message;
  }

  if (detail) {
    try {
      timeline = await ordersApi.timeline(params.orderId);
    } catch {
      // Detail still renders even when timeline fails; the timeline
      // section surfaces an empty state.
    }
  }

  if (detailError || !detail) {
    return (
      <div className="space-y-ds-lg">
        <PageHeader title={t("title", { number: params.orderId })} />
        <ErrorState reasonCode={detailError ?? "unknown"} />
      </div>
    );
  }

  const orderClosed =
    detail.orderState === "cancelled" || detail.orderState === "completed";

  return (
    <div className="space-y-ds-lg">
      <PageHeader
        title={t("title", { number: detail.number })}
        actions={
          <AuditForResourceLink
            resourceType="Order"
            resourceId={detail.id}
            canRead={hasPermission(session, "audit.read")}
          />
        }
      />
      <FourStatePillRow
        orderState={detail.orderState}
        paymentState={detail.paymentState}
        fulfillmentState={detail.fulfillmentState}
        refundState={detail.refundState}
      />
      <TransitionActionBar
        orderId={detail.id}
        rowVersion={detail.rowVersion}
        permissions={session?.permissions ?? []}
        states={{
          order: detail.orderState,
          payment: detail.paymentState,
          fulfillment: detail.fulfillmentState,
          refund: detail.refundState,
        }}
        orderClosed={orderClosed}
      />
      <section>
        <h2 className="text-lg font-semibold">{t("customer")}</h2>
        <p>{detail.customer.displayName}</p>
        {detail.customer.email ? (
          <p className="text-sm text-muted-foreground">{detail.customer.email}</p>
        ) : null}
      </section>
      <section>
        <h2 className="text-lg font-semibold">{t("lines")}</h2>
        <ul className="space-y-ds-xs">
          {detail.lineItems.map((line) => (
            <li key={line.id} className="flex justify-between text-sm">
              <span>
                {line.name[locale] || line.name.en} · ×{line.qty}
              </span>
              <span>
                {(line.lineSubtotalMinor / 100).toLocaleString()}{" "}
                {detail.totals.currency}
              </span>
            </li>
          ))}
        </ul>
      </section>
      <section>
        <h2 className="text-lg font-semibold">{t("timeline")}</h2>
        <Timeline entries={timeline.rows} />
      </section>
    </div>
  );
}

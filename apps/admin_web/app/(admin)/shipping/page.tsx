/**
 * Spec 026 — admin shipping overview. Index page for the Shipping module
 * surfacing the five primary admin sub-areas (methods, zones, shipments,
 * provider routing, exception queue).
 */
import { getTranslations } from "next-intl/server";
import Link from "next/link";
import { PageHeader } from "@/components/shell/page-header";

interface CardProps {
  href: string;
  title: string;
  body: string;
}

function OverviewCard({ href, title, body }: CardProps) {
  return (
    <Link
      href={href}
      className="rounded-md border border-border bg-card p-ds-md text-card-foreground transition hover:bg-muted"
    >
      <h2 className="text-base font-semibold">{title}</h2>
      <p className="mt-ds-xs text-sm text-muted-foreground">{body}</p>
    </Link>
  );
}

export default async function ShippingOverviewPage() {
  const t = await getTranslations("shipping.overview");
  return (
    <div className="space-y-ds-lg">
      <PageHeader title={t("title")} description={t("description")} />
      <div className="grid gap-ds-md md:grid-cols-2 lg:grid-cols-3">
        <OverviewCard href="/shipping/methods" title={t("card.methods")} body={t("card.methods_body")} />
        <OverviewCard href="/shipping/zones" title={t("card.zones")} body={t("card.zones_body")} />
        <OverviewCard href="/shipping/shipments" title={t("card.shipments")} body={t("card.shipments_body")} />
        <OverviewCard
          href="/shipping/provider-routing"
          title={t("card.provider_routing")}
          body={t("card.provider_routing_body")}
        />
        <OverviewCard
          href="/shipping/exception-queue"
          title={t("card.exception_queue")}
          body={t("card.exception_queue_body")}
        />
      </div>
    </div>
  );
}

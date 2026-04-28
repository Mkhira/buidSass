/**
 * T019 — inventory overview page.
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

export default async function InventoryOverviewPage() {
  const t = await getTranslations("inventory.overview");
  return (
    <div className="space-y-ds-lg">
      <PageHeader title={t("title")} description={t("description")} />
      <div className="grid gap-ds-md md:grid-cols-2 lg:grid-cols-3">
        <OverviewCard
          href="/inventory/stock"
          title={t("card.stock")}
          body={t("card.stock_body")}
        />
        <OverviewCard
          href="/inventory/adjust"
          title={t("card.adjust")}
          body={t("card.adjust_body")}
        />
        <OverviewCard
          href="/inventory/low-stock"
          title={t("card.low_stock")}
          body={t("card.low_stock_body")}
        />
        <OverviewCard
          href="/inventory/batches"
          title={t("card.batches")}
          body={t("card.batches_body")}
        />
        <OverviewCard
          href="/inventory/expiry"
          title={t("card.expiry")}
          body={t("card.expiry_body")}
        />
        <OverviewCard
          href="/inventory/reservations"
          title={t("card.reservations")}
          body={t("card.reservations_body")}
        />
        <OverviewCard
          href="/inventory/ledger"
          title={t("card.ledger")}
          body={t("card.ledger_body")}
        />
      </div>
    </div>
  );
}

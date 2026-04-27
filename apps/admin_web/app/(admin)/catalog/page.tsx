/**
 * T017 — catalog overview page. Cards link to the four sub-modules.
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

export default async function CatalogOverviewPage() {
  const t = await getTranslations("catalog.overview");
  return (
    <div className="space-y-ds-lg">
      <PageHeader title={t("title")} description={t("description")} />
      <div className="grid gap-ds-md md:grid-cols-2 lg:grid-cols-3">
        <OverviewCard
          href="/catalog/products"
          title={t("card.products")}
          body={t("card.products_body")}
        />
        <OverviewCard
          href="/catalog/categories"
          title={t("card.categories")}
          body={t("card.categories_body")}
        />
        <OverviewCard
          href="/catalog/brands"
          title={t("card.brands")}
          body={t("card.brands_body")}
        />
        <OverviewCard
          href="/catalog/manufacturers"
          title={t("card.manufacturers")}
          body={t("card.manufacturers_body")}
        />
        <OverviewCard
          href="/catalog/bulk-import"
          title={t("card.bulk_import")}
          body={t("card.bulk_import_body")}
        />
      </div>
    </div>
  );
}

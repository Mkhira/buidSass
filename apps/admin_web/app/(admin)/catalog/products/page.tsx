/**
 * T018 — products list (Server Component).
 *
 * Initial render fetches the first page server-side; the
 * <ProductListTable> Client Component owns subsequent interactions.
 */
import { getTranslations, getLocale } from "next-intl/server";
import Link from "next/link";
import { requirePermission } from "@/lib/auth/guards";
import { catalogApi, type ProductState } from "@/lib/api/clients/catalog";
import { buttonVariants } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import { PageHeader } from "@/components/shell/page-header";
import { ErrorState } from "@/components/shell/error-state";
import { ProductListTable } from "@/components/catalog/product/product-list-table";

interface SearchParams {
  q?: string;
  state?: string;
  cursor?: string;
}

export default async function ProductsListPage({
  searchParams,
}: {
  searchParams: SearchParams;
}) {
  await requirePermission(["catalog.product.read"], "/catalog/products");
  const t = await getTranslations("catalog.product");
  const locale = (await getLocale()) === "ar" ? "ar" : "en";

  const validStates: readonly ProductState[] = ["draft", "scheduled", "published"];
  const stateParam =
    searchParams.state && (validStates as readonly string[]).includes(searchParams.state)
      ? (searchParams.state as ProductState)
      : undefined;
  const filter = {
    search: searchParams.q,
    state: stateParam,
    cursor: searchParams.cursor,
  };

  let initialData;
  let errorReason: string | undefined;
  try {
    initialData = await catalogApi.products.list(filter);
  } catch (e) {
    errorReason = e instanceof Error ? e.message : "unknown";
  }

  return (
    <div className="space-y-ds-lg">
      <PageHeader
        title={t("title")}
        actions={
          <Link
            href="/catalog/products/new"
            className={cn(buttonVariants({ variant: "default" }))}
          >
            {t("create_new")}
          </Link>
        }
      />
      {errorReason ? (
        <ErrorState reasonCode={errorReason} />
      ) : (
        <ProductListTable
          initialData={initialData?.rows ?? []}
          initialPagination={{
            hasMore: Boolean(initialData?.nextCursor),
            nextCursor: initialData?.nextCursor ?? null,
          }}
          initialQuery={searchParams.q ?? ""}
          locale={locale}
        />
      )}
    </div>
  );
}

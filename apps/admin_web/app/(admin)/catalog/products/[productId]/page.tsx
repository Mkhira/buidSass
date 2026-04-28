/**
 * T023 — edit product page.
 */
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { requirePermission } from "@/lib/auth/guards";
import { PageHeader } from "@/components/shell/page-header";
import { AuditForResourceLink } from "@/components/shell/audit-for-resource-link";
import { ErrorState } from "@/components/shell/error-state";
import { ProductEditorForm } from "@/components/catalog/product/product-editor-form";
import { catalogApi } from "@/lib/api/clients/catalog";
import { hasPermission } from "@/lib/auth/permissions";
import { getSession } from "@/lib/auth/session";

export default async function EditProductPage({
  params,
}: {
  params: { productId: string };
}) {
  await requirePermission(["catalog.product.read"], `/catalog/products/${params.productId}`);
  const session = await getSession();
  const t = await getTranslations("catalog.product");

  let product;
  let error: string | undefined;
  try {
    product = await catalogApi.products.byId(params.productId);
  } catch (e) {
    const message = e instanceof Error ? e.message : "unknown";
    if (message.includes("404")) notFound();
    error = message;
  }

  return (
    <div className="space-y-ds-lg">
      <PageHeader
        title={t("edit_title")}
        actions={
          <AuditForResourceLink
            resourceType="Product"
            resourceId={params.productId}
            canRead={hasPermission(session, "audit.read")}
          />
        }
      />
      {error ? <ErrorState reasonCode={error} /> : null}
      {product ? <ProductEditorForm initial={product} /> : null}
    </div>
  );
}

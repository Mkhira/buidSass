/**
 * T023 — new product page.
 */
import { getTranslations } from "next-intl/server";
import { requirePermission } from "@/lib/auth/guards";
import { PageHeader } from "@/components/shell/page-header";
import { ProductEditorForm } from "@/components/catalog/product/product-editor-form";

export default async function NewProductPage() {
  await requirePermission(["catalog.product.write"], "/catalog/products/new");
  const t = await getTranslations("catalog.product");
  return (
    <div className="space-y-ds-lg">
      <PageHeader title={t("new_title")} />
      <ProductEditorForm initial={null} />
    </div>
  );
}

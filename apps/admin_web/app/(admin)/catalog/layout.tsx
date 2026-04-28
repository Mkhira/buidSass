/**
 * T016 — catalog sub-shell layout.
 * The (admin) layout already mounts AppShell + session/permission gate.
 * This sub-layout adds the catalog group as the active sidebar context.
 */
import { requireAnyPermission } from "@/lib/auth/guards";
import type { ReactNode } from "react";

export default async function CatalogLayout({ children }: { children: ReactNode }) {
  await requireAnyPermission(
    [
      "catalog.read",
      "catalog.product.read",
      "catalog.category.read",
      "catalog.brand.read",
      "catalog.manufacturer.read",
      "catalog.product.bulk_import",
    ],
    "/catalog",
  );
  return <>{children}</>;
}

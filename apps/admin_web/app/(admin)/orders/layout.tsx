/**
 * T020 — orders sub-shell layout.
 */
import { requireAnyPermission } from "@/lib/auth/guards";
import type { ReactNode } from "react";

export default async function OrdersLayout({ children }: { children: ReactNode }) {
  await requireAnyPermission(
    ["orders.read", "orders.export", "orders.refund"],
    "/orders",
  );
  return <>{children}</>;
}

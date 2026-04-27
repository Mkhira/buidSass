/**
 * T018 — inventory sub-shell layout.
 */
import { requireAnyPermission } from "@/lib/auth/guards";
import type { ReactNode } from "react";

export default async function InventoryLayout({ children }: { children: ReactNode }) {
  await requireAnyPermission(
    [
      "inventory.read",
      "inventory.adjust",
      "inventory.threshold.read",
      "inventory.batch.read",
      "inventory.reservation.read",
    ],
    "/inventory",
  );
  return <>{children}</>;
}

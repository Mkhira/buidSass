/**
 * Spec 026 (T014/T015/T045) — admin shipping sub-shell layout.
 * The route group covers methods, zones, shipments, provider-routing,
 * and the exception queue. Each leaf page enforces its own permission
 * guard; the layout exists to keep the routing tree tidy and to ease
 * future addition of breadcrumbs / nav state.
 */
import { requireAnyPermission } from "@/lib/auth/guards";
import type { ReactNode } from "react";

export default async function ShippingLayout({ children }: { children: ReactNode }) {
  await requireAnyPermission(
    [
      "shipping.method.read",
      "shipping.method.author",
      "shipping.zone.read",
      "shipping.shipment.read",
      "shipping.shipment.write",
      "shipping.provider_routing.read",
      "shipping.provider_routing.write",
    ],
    "/shipping",
  );
  return <>{children}</>;
}

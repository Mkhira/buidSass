import { requireAnyPermission } from "@/lib/auth/guards";
import type { ReactNode } from "react";

export default async function CustomersLayout({
  children,
}: {
  children: ReactNode;
}) {
  await requireAnyPermission(
    ["customers.read", "customers.account_action"],
    "/customers",
  );
  return <>{children}</>;
}

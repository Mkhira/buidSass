/**
 * T040e: AuditForResourceLink (FR-028f).
 *
 * Header-area "View audit log" affordance that deep-links to the audit
 * reader pre-filtered by `?resourceType=…&resourceId=…`. Hidden when
 * the actor lacks `audit.read`.
 *
 * The resourceType strings come from the registry in
 * `contracts/audit-redaction.md` (Resource-type registry section). The
 * union is checked at compile time so 016/017/018/019 can't drift.
 */
import Link from "next/link";
import { useTranslations } from "next-intl";
import { ScrollText } from "lucide-react";
import { buttonVariants } from "@/components/ui/button";
import { cn } from "@/lib/utils";

export type AuditResourceType =
  | "Product"
  | "Category"
  | "Brand"
  | "Manufacturer"
  | "Sku"
  | "Warehouse"
  | "Batch"
  | "Reservation"
  | "Order"
  | "Refund"
  | "Invoice"
  | "Customer"
  | "AdminAccount";

export interface AuditForResourceLinkProps {
  resourceType: AuditResourceType;
  resourceId: string;
  /** Whether the actor holds `audit.read`. Hide entirely when false. */
  canRead: boolean;
  /** Visual variant; defaults to "ghost" so the link sits unobtrusively in page headers. */
  variant?: "ghost" | "secondary" | "default";
}

export function AuditForResourceLink({
  resourceType,
  resourceId,
  canRead,
  variant = "ghost",
}: AuditForResourceLinkProps) {
  const t = useTranslations("shell");
  if (!canRead) return null;
  const href = `/audit?resourceType=${encodeURIComponent(resourceType)}&resourceId=${encodeURIComponent(resourceId)}`;
  return (
    <Link href={href} className={cn(buttonVariants({ variant, size: "sm" }))}>
      <ScrollText aria-hidden="true" className="me-ds-xs size-4" />
      {t("audit_for_resource_link")}
    </Link>
  );
}

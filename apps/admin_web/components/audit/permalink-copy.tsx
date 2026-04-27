/**
 * T068: PermalinkCopy — writes the audit-entry permalink to the
 * clipboard with a sonner toast confirmation.
 *
 * Permalink format per spec 015 FR-021:
 *   https://admin.<env>.dental-commerce.com/audit/<entryId>?permalink=1&locale=<en|ar>
 *
 * The host part is the deployment env's admin host; in dev it falls
 * back to `window.location.host`.
 */
"use client";

import { useTranslations, useLocale } from "next-intl";
import { Link2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { toast } from "sonner";
import { emitTelemetry } from "@/lib/observability/telemetry";

interface PermalinkCopyProps {
  entryId: string;
}

export function PermalinkCopy({ entryId }: PermalinkCopyProps) {
  const t = useTranslations("audit.detail");
  const locale = useLocale();

  async function copy() {
    if (typeof window === "undefined") return;
    const url = new URL(
      `/audit/${encodeURIComponent(entryId)}?permalink=1&locale=${locale}`,
      window.location.origin,
    ).toString();
    try {
      await navigator.clipboard.writeText(url);
      emitTelemetry({ name: "admin.audit.permalink.copied" });
      toast.success(t("permalink_copied"));
    } catch {
      toast.error(t("permalink_copied"));
    }
  }

  return (
    <Button type="button" variant="outline" size="sm" onClick={copy}>
      <Link2 aria-hidden="true" className="me-ds-xs size-4" />
      {t("copy_permalink")}
    </Button>
  );
}

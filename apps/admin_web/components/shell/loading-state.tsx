/**
 * T040: LoadingState — page-level loading stub used while a Server
 * Component fetches.
 */
import { useTranslations } from "next-intl";
import { Skeleton } from "@/components/ui/skeleton";

export function LoadingState({ rows = 4 }: { rows?: number }) {
  const t = useTranslations("shell");
  return (
    <div role="status" aria-live="polite" aria-busy="true" className="space-y-ds-md">
      <span className="sr-only">{t("loading")}</span>
      {Array.from({ length: rows }).map((_, i) => (
        <Skeleton key={i} className="h-8 w-full" />
      ))}
    </div>
  );
}

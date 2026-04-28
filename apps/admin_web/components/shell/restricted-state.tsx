/**
 * T040: RestrictedState (Constitution Principle 27).
 *
 * **Distinct from `<ForbiddenState>`** (T040a): RestrictedState is for
 * *content* that is restricted (e.g., a customer-app surface previewed
 * inside the admin showing a restricted-product view). ForbiddenState is
 * for *route* permission denial.
 */
import { useTranslations } from "next-intl";
import { Lock } from "lucide-react";

interface RestrictedStateProps {
  reason?: string;
}

export function RestrictedState({ reason }: RestrictedStateProps) {
  const t = useTranslations("shell");
  return (
    <div
      role="note"
      className="flex items-start gap-ds-sm rounded-md border border-border bg-muted/30 p-ds-md"
    >
      <Lock aria-hidden="true" className="mt-0.5 size-4 text-muted-foreground" />
      <div>
        <p className="text-sm font-medium text-foreground">{t("restricted")}</p>
        {reason ? <p className="text-sm text-muted-foreground">{reason}</p> : null}
      </div>
    </div>
  );
}

/**
 * T040a: ForbiddenState (FR-022 / FR-025).
 *
 * The 403 / `/__forbidden` route screen. Distinct from `RestrictedState`
 * (T040): RestrictedState is for content; ForbiddenState is for routes
 * the actor's permission set doesn't permit.
 */
import Link from "next/link";
import { useTranslations } from "next-intl";
import { ShieldAlert } from "lucide-react";
import { buttonVariants } from "@/components/ui/button";

export function ForbiddenState() {
  const t = useTranslations("shell.forbidden");
  return (
    <main
      role="main"
      className="flex min-h-[60vh] flex-col items-center justify-center gap-ds-md p-ds-xl text-center"
    >
      <ShieldAlert aria-hidden="true" className="size-12 text-muted-foreground" />
      <h1 className="text-2xl font-semibold tracking-tight">{t("title")}</h1>
      <p className="max-w-md text-sm text-muted-foreground">{t("body")}</p>
      <Link href="/" className={buttonVariants({ variant: "default" })}>
        {t("primary_cta")}
      </Link>
      <p className="max-w-md text-xs text-muted-foreground">{t("secondary_hint")}</p>
    </main>
  );
}

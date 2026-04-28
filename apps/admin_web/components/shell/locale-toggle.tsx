/**
 * T018: Locale toggle (Client Component) flipping the `admin_locale` cookie
 * and triggering a soft refresh so the active layout re-renders with the
 * new locale + dir attribute.
 *
 * Per FR-028e + locale-aware-endpoints.md, react-query keys for i18n-bearing
 * endpoints include the active locale; switching invalidates them naturally.
 */
"use client";

import { useTransition } from "react";
import { useLocale } from "next-intl";
import { useRouter } from "next/navigation";
import { Button } from "@/components/ui/button";
import { LOCALE_COOKIE, type Locale } from "@/lib/i18n/config";

const LABELS: Record<Locale, string> = {
  en: "EN",
  ar: "AR",
};

export function LocaleToggle() {
  const current = useLocale() as Locale;
  const router = useRouter();
  const [isPending, startTransition] = useTransition();

  function setLocale(next: Locale) {
    if (next === current) return;
    document.cookie = `${LOCALE_COOKIE}=${next}; path=/; max-age=${60 * 60 * 24 * 365}; samesite=strict`;
    startTransition(() => {
      router.refresh();
    });
  }

  return (
    <div role="group" aria-label="Language" className="inline-flex items-center gap-ds-xs">
      {(["en", "ar"] as const).map((locale) => (
        <Button
          key={locale}
          type="button"
          size="sm"
          variant={locale === current ? "default" : "ghost"}
          onClick={() => setLocale(locale)}
          disabled={isPending}
          aria-pressed={locale === current}
        >
          {LABELS[locale]}
        </Button>
      ))}
    </div>
  );
}

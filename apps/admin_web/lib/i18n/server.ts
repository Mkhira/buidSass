/**
 * T017: server-side i18n entry point.
 *
 * `getRequestConfig` is consumed by next-intl's middleware integration.
 * For Server Components, call `getLocale()` / `getTranslations()` from
 * `next-intl/server`.
 */
import { getRequestConfig } from "next-intl/server";
import { cookies, headers } from "next/headers";
import { DEFAULT_LOCALE, LOCALE_COOKIE, SUPPORTED_LOCALES, isLocale, type Locale } from "./config";

async function loadMessages(locale: string) {
  // Locale is constrained to the `Locale` enum upstream, so this dynamic
  // import is safe; the no-unsanitized rule false-positives on it.
  // eslint-disable-next-line no-unsanitized/method
  const mod = await import(`../../messages/${locale}.json`);
  return mod.default;
}

export default getRequestConfig(async () => {
  const locale = resolveLocale();
  const messages = await loadMessages(locale);
  return {
    locale,
    messages,
    timeZone: "Asia/Riyadh", // ADR-010 default; per-warehouse / per-admin overrides land later
  };
});

export function resolveLocale(): Locale {
  // 1. Cookie wins
  const cookieValue = cookies().get(LOCALE_COOKIE)?.value;
  if (isLocale(cookieValue)) return cookieValue;

  // 2. Accept-Language fallback
  const acceptLanguage = headers().get("accept-language") ?? "";
  for (const part of acceptLanguage.split(",")) {
    const tag = part.split(";")[0].trim().toLowerCase();
    const primary = tag.split("-")[0] as Locale;
    if (SUPPORTED_LOCALES.includes(primary)) return primary;
  }

  return DEFAULT_LOCALE;
}

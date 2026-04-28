/**
 * T017: i18n config — locales, cookie name, default detection.
 *
 * Per research §R4: URL paths are locale-neutral; locale persists in a cookie.
 * Detection order: cookie → `Accept-Language` header → `en`.
 */

export type Locale = "en" | "ar";

export const SUPPORTED_LOCALES: Locale[] = ["en", "ar"];
export const DEFAULT_LOCALE: Locale = "en";
export const LOCALE_COOKIE = "admin_locale";

export const RTL_LOCALES: Locale[] = ["ar"];

export function isLocale(value: unknown): value is Locale {
  return typeof value === "string" && (SUPPORTED_LOCALES as string[]).includes(value);
}

export function dirFor(locale: Locale): "ltr" | "rtl" {
  return RTL_LOCALES.includes(locale) ? "rtl" : "ltr";
}

/**
 * Maps a locale + market to the BCP-47 tag the backend expects on
 * `Accept-Language`. Market is platform-resolved per spec 015 FR-015.
 */
export function bcp47For(locale: Locale, market: "ksa" | "eg" | "platform"): string {
  const region = market === "eg" ? "EG" : "SA"; // platform-scope defaults to KSA per ADR-010
  return `${locale}-${region}`;
}

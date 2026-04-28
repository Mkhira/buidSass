/**
 * T078: Per-market currency + numeral + date formatters.
 *
 * Default locale tags follow the market scope (KSA → ar-SA / en-SA,
 * EG → ar-EG / en-EG, platform → en-SA per ADR-010 KSA residency).
 *
 * Western Arabic numerals are the default per research §R4 — Eastern
 * Arabic numerals (٠١٢…) deferred to a separate copy decision.
 */
import type { Locale } from "./config";

export type MarketScope = "ksa" | "eg" | "platform";

const CURRENCY_BY_MARKET: Record<MarketScope, string> = {
  ksa: "SAR",
  eg: "EGP",
  platform: "SAR",
};

export function bcp47For(locale: Locale, market: MarketScope): string {
  const region = market === "eg" ? "EG" : "SA";
  return `${locale}-${region}`;
}

export function formatCurrency(
  amountMinor: number,
  market: MarketScope,
  locale: Locale,
  options?: Partial<Intl.NumberFormatOptions>,
): string {
  const tag = bcp47For(locale, market);
  const currency = CURRENCY_BY_MARKET[market];
  return new Intl.NumberFormat(tag, {
    style: "currency",
    currency,
    numberingSystem: "latn", // western-arabic per R4
    ...options,
  }).format(amountMinor / 100);
}

export function formatNumber(value: number, locale: Locale, market: MarketScope = "ksa"): string {
  return new Intl.NumberFormat(bcp47For(locale, market), { numberingSystem: "latn" }).format(value);
}

export function formatDate(
  iso: string | Date,
  locale: Locale,
  market: MarketScope = "ksa",
  options: Intl.DateTimeFormatOptions = { dateStyle: "medium" },
): string {
  const d = typeof iso === "string" ? new Date(iso) : iso;
  return new Intl.DateTimeFormat(bcp47For(locale, market), {
    numberingSystem: "latn",
    ...options,
  }).format(d);
}

export function formatDateTime(
  iso: string | Date,
  locale: Locale,
  market: MarketScope = "ksa",
): string {
  return formatDate(iso, locale, market, { dateStyle: "medium", timeStyle: "short" });
}

export function formatRelative(iso: string | Date, locale: Locale): string {
  const d = typeof iso === "string" ? new Date(iso) : iso;
  const seconds = Math.round((d.getTime() - Date.now()) / 1000);
  const rtf = new Intl.RelativeTimeFormat(locale, { numeric: "auto" });
  const abs = Math.abs(seconds);
  if (abs < 60) return rtf.format(seconds, "second");
  if (abs < 3600) return rtf.format(Math.round(seconds / 60), "minute");
  if (abs < 86400) return rtf.format(Math.round(seconds / 3600), "hour");
  return rtf.format(Math.round(seconds / 86400), "day");
}

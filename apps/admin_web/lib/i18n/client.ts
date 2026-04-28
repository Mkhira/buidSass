/**
 * T017: client-side i18n entry point.
 *
 * Re-exports the next-intl Client Component hooks so feature code imports
 * from one path. Server Components import from `next-intl/server` directly.
 */
"use client";

export { useTranslations, useLocale, useFormatter, useNow, useTimeZone } from "next-intl";
export { NextIntlClientProvider } from "next-intl";

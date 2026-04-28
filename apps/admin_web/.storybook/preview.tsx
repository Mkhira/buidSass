/**
 * T046: Storybook preview — locale + theme toolbar.
 *
 * Wraps every story in <NextIntlClientProvider> with the matching
 * messages bundle, sets `dir` on the html, applies the theme class.
 */
import type { Preview } from "@storybook/react";
import React from "react";
import { NextIntlClientProvider } from "next-intl";
import en from "../messages/en.json";
import ar from "../messages/ar.json";
import "../app/globals.css";

const MESSAGES = { en, ar } as const;

const preview: Preview = {
  globalTypes: {
    locale: {
      name: "Locale",
      description: "Active locale",
      defaultValue: "en",
      toolbar: {
        icon: "globe",
        items: [
          { value: "en", title: "English (LTR)" },
          { value: "ar", title: "Arabic (RTL)" },
        ],
      },
    },
    theme: {
      name: "Theme",
      description: "Light / dark theme",
      defaultValue: "light",
      toolbar: {
        icon: "circlehollow",
        items: [
          { value: "light", title: "Light" },
          { value: "dark", title: "Dark" },
        ],
      },
    },
  },
  parameters: {
    controls: { expanded: true },
    a11y: { config: { rules: [{ id: "color-contrast", enabled: true }] } },
  },
  decorators: [
    (Story, context) => {
      const locale = (context.globals.locale as "en" | "ar") ?? "en";
      const theme = (context.globals.theme as "light" | "dark") ?? "light";
      const dir = locale === "ar" ? "rtl" : "ltr";
      const messages = MESSAGES[locale];
      if (typeof document !== "undefined") {
        document.documentElement.lang = locale;
        document.documentElement.dir = dir;
        document.documentElement.classList.toggle("dark", theme === "dark");
      }
      return (
        <NextIntlClientProvider locale={locale} messages={messages} timeZone="Asia/Riyadh">
          <div className="min-h-screen bg-background p-8 text-foreground">
            <Story />
          </div>
        </NextIntlClientProvider>
      );
    },
  ],
};

export default preview;

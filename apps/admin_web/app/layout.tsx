import type { Metadata } from "next";
import { Inter } from "next/font/google";
import { getMessages, getLocale } from "next-intl/server";
import "./globals.css";
import { cn } from "@/lib/utils";
import { dirFor, type Locale } from "@/lib/i18n/config";
import { AppProviders } from "@/components/providers/app-providers";

const inter = Inter({ subsets: ["latin"], variable: "--font-sans" });

export const metadata: Metadata = {
  title: "Dental Commerce Admin",
  description: "Admin shell for the Dental Commerce Platform — spec 015.",
};

export default async function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  const locale = (await getLocale()) as Locale;
  const messages = await getMessages();
  const dir = dirFor(locale);

  return (
    <html lang={locale} dir={dir} className={cn("font-sans", inter.variable)}>
      <body className="antialiased">
        <AppProviders locale={locale} messages={messages} timeZone="Asia/Riyadh">
          {children}
        </AppProviders>
      </body>
    </html>
  );
}

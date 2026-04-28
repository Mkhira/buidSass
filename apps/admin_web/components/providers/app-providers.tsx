/**
 * Client Component wrapping next-intl + react-query + sonner toaster.
 * Imported by `app/layout.tsx` (Server) so locale + cache + toast host
 * are available everywhere downstream.
 */
"use client";

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { NextIntlClientProvider } from "next-intl";
import { Toaster } from "@/components/ui/sonner";
import { useState } from "react";
import type { ReactNode } from "react";

import type { AbstractIntlMessages } from "next-intl";

interface AppProvidersProps {
  children: ReactNode;
  locale: string;
  messages: AbstractIntlMessages;
  timeZone: string;
}

export function AppProviders({ children, locale, messages, timeZone }: AppProvidersProps) {
  const [queryClient] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: {
            staleTime: 30_000,
            refetchOnWindowFocus: false,
            retry: (failureCount, error) => {
              // Don't retry 4xx — we want fast feedback on permission /
              // validation errors. 5xx is retried up to 2 times.
              if (error instanceof Error && /\b4\d\d\b/.test(error.message)) return false;
              return failureCount < 2;
            },
          },
        },
      }),
  );

  return (
    <NextIntlClientProvider locale={locale} messages={messages} timeZone={timeZone}>
      <QueryClientProvider client={queryClient}>
        {children}
        <Toaster richColors closeButton />
      </QueryClientProvider>
    </NextIntlClientProvider>
  );
}

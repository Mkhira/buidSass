/**
 * T035: AppShell — composes SidebarNav + TopBar + main content slot.
 *
 * Mounted by `app/(admin)/layout.tsx`. The skip-to-content link
 * satisfies FR-005's keyboard-nav requirement.
 */
import { useTranslations } from "next-intl";
import type { ReactNode } from "react";
import { SidebarNav } from "./sidebar-nav";
import { TopBar } from "./top-bar";
import type { AdminSessionPayload } from "@/lib/auth/session";

interface AppShellProps {
  session: AdminSessionPayload;
  children: ReactNode;
}

export function AppShell({ session, children }: AppShellProps) {
  const t = useTranslations("shell");
  return (
    <div className="flex min-h-screen flex-col">
      <a
        href="#main-content"
        className="sr-only focus:not-sr-only focus:absolute focus:start-2 focus:top-2 focus:z-50 focus:rounded focus:bg-primary focus:px-ds-sm focus:py-ds-xs focus:text-primary-foreground"
      >
        {t("skip_to_content")}
      </a>
      <TopBar session={session} />
      <div className="flex flex-1">
        <SidebarNav />
        <div className="flex flex-1 flex-col">
          <main id="main-content" role="main" tabIndex={-1} className="flex-1 p-ds-lg">
            {children}
          </main>
        </div>
      </div>
    </div>
  );
}

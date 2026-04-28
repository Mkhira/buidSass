/**
 * T014: (auth) layout — minimal centered card for unauthenticated screens.
 *
 * Login / MFA / reset land here. The shell (sidebar / topbar) is NOT
 * mounted; CSP + locale headers come from middleware.ts (T026 + T032a).
 */
import type { ReactNode } from "react";

export default function AuthLayout({ children }: { children: ReactNode }) {
  return (
    <main
      className="flex min-h-screen items-center justify-center bg-background p-ds-md"
      role="main"
    >
      <div className="w-full max-w-md rounded-lg border border-border bg-card p-ds-lg shadow-sm">
        {children}
      </div>
    </main>
  );
}

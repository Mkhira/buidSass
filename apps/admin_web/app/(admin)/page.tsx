/**
 * T056: Admin landing — "today's tasks" placeholder.
 *
 * Real cards land in 1D specs (returns awaiting approval, low-stock,
 * pending verifications). For now this renders a welcome scoped to
 * the active session's display name plus two placeholder cards.
 */
import { getTranslations } from "next-intl/server";
import { getSession } from "@/lib/auth/session";
import { PageHeader } from "@/components/shell/page-header";

function Card_({ children, className }: { children: React.ReactNode; className?: string }) {
  return (
    <div className={`rounded-md border border-border bg-card p-ds-md text-card-foreground ${className ?? ""}`}>
      {children}
    </div>
  );
}

export default async function AdminLandingPage() {
  const session = await getSession();
  const tShell = await getTranslations("shell");
  const tLanding = await getTranslations("landing");

  return (
    <div className="space-y-ds-lg">
      <PageHeader
        title={tShell("app_name")}
        description={session ? tLanding("signed_in_as", { name: session.displayName }) : ""}
      />

      <div className="grid gap-ds-md md:grid-cols-2 lg:grid-cols-3">
        <Card_>
          <h2 className="text-sm font-medium text-muted-foreground">{tLanding("todays_tasks_title")}</h2>
          <p className="mt-ds-xs text-sm">{tLanding("todays_tasks_body")}</p>
        </Card_>
        <Card_>
          <h2 className="text-sm font-medium text-muted-foreground">{tLanding("recent_audit_title")}</h2>
          <p className="mt-ds-xs text-sm">{tLanding("recent_audit_body", { path: "/audit" })}</p>
        </Card_>
      </div>
    </div>
  );
}

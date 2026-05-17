/**
 * Spec 025 — admin notifications overview. Index page for the Notifications
 * module surfacing the four primary admin sub-areas (templates, campaigns,
 * dead-letter, provider-routing).
 *
 * Scaffold-only delivery: full UI (table interactions, editor, review board,
 * routing flip-control) lands in the Phase 7+ UI batch. Strings are inlined
 * here pending the messages/{ar,en}.json translation-key pass.
 */
import Link from "next/link";
import { PageHeader } from "@/components/shell/page-header";

interface CardProps {
  href: string;
  title: string;
  body: string;
}

function OverviewCard({ href, title, body }: CardProps) {
  return (
    <Link
      href={href}
      className="rounded-md border border-border bg-card p-ds-md text-card-foreground transition hover:bg-muted"
    >
      <h2 className="text-base font-semibold">{title}</h2>
      <p className="mt-ds-xs text-sm text-muted-foreground">{body}</p>
    </Link>
  );
}

export default function NotificationsOverviewPage() {
  return (
    <div className="space-y-ds-lg">
      <PageHeader
        title="Notifications"
        description="Templates, campaigns, dead-letter ops, and provider routing for transactional and marketing messages."
      />
      <div className="grid gap-ds-md md:grid-cols-2 lg:grid-cols-3">
        <OverviewCard
          href="/notifications/templates"
          title="Templates"
          body="Author, review, and publish AR + EN template versions per event kind."
        />
        <OverviewCard
          href="/notifications/campaigns"
          title="Campaigns"
          body="Schedule, pause, resume, cancel campaigns and read per-state delivery reports."
        />
        <OverviewCard
          href="/notifications/dead-letter"
          title="Dead-letter queue"
          body="Operator review of failed deliveries: retry, discard, archive."
        />
        <OverviewCard
          href="/notifications/provider-routing"
          title="Provider routing"
          body="Per-market × channel primary/backup provider selection and failover."
        />
      </div>
    </div>
  );
}

/**
 * Spec 025 — admin notifications overview. Index page for the Notifications
 * module surfacing the four primary admin sub-areas (templates, campaigns,
 * dead-letter, provider-routing).
 *
 * Scaffold-only delivery: full UI (table interactions, editor, review board,
 * routing flip-control) lands in the Phase 7+ UI batch.
 */
import { getTranslations } from "next-intl/server";
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

export default async function NotificationsOverviewPage() {
  const t = await getTranslations("notifications.overview");
  return (
    <div className="space-y-ds-lg">
      <PageHeader title={t("title")} description={t("description")} />
      <div className="grid gap-ds-md md:grid-cols-2 lg:grid-cols-3">
        <OverviewCard
          href="/notifications/templates"
          title={t("card.templates")}
          body={t("card.templates_body")}
        />
        <OverviewCard
          href="/notifications/campaigns"
          title={t("card.campaigns")}
          body={t("card.campaigns_body")}
        />
        <OverviewCard
          href="/notifications/dead-letter"
          title={t("card.dead_letter")}
          body={t("card.dead_letter_body")}
        />
        <OverviewCard
          href="/notifications/provider-routing"
          title={t("card.provider_routing")}
          body={t("card.provider_routing_body")}
        />
      </div>
    </div>
  );
}

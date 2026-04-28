/**
 * T057: /me — read-only profile + locale toggle target.
 *
 * Surfaces the active session's identity, role scope, MFA enrolment, and
 * a link to /me/preferences. Editing identity fields is owned by spec 004
 * via a future admin-self-service surface; this page is read-only in v1.
 */
import { getTranslations } from "next-intl/server";
import Link from "next/link";
import { getSession } from "@/lib/auth/session";
import { PageHeader } from "@/components/shell/page-header";
import { MaskedField } from "@/components/shell/masked-field";
import { Badge } from "@/components/ui/badge";
import { buttonVariants } from "@/components/ui/button";

export default async function MePage() {
  const session = await getSession();
  const tNav = await getTranslations("nav.entry");
  const tMe = await getTranslations("me");
  if (!session) return null; // (admin) layout would have redirected; defensive

  return (
    <div className="space-y-ds-lg">
      <PageHeader
        title={tNav("me")}
        actions={
          <Link href="/me/preferences" className={buttonVariants({ variant: "outline", size: "sm" })}>
            {tNav("preferences")}
          </Link>
        }
      />

      <dl className="grid max-w-2xl grid-cols-[max-content,1fr] gap-x-ds-md gap-y-ds-sm text-sm">
        <dt className="text-muted-foreground">{tMe("display_name")}</dt>
        <dd>{session.displayName}</dd>

        <dt className="text-muted-foreground">{tMe("email")}</dt>
        <dd>
          <MaskedField kind="email" value={session.email} canRead={true} />
        </dd>

        <dt className="text-muted-foreground">{tMe("market_scope")}</dt>
        <dd>
          <Badge variant="secondary">{session.roleScope}</Badge>
        </dd>

        <dt className="text-muted-foreground">{tMe("roles")}</dt>
        <dd>
          <div className="flex flex-wrap gap-ds-xs">
            {session.roles.map((r) => (
              <Badge key={r} variant="outline" className="font-mono text-xs">
                {r}
              </Badge>
            ))}
          </div>
        </dd>

        <dt className="text-muted-foreground">{tMe("mfa_enrolled")}</dt>
        <dd>{session.mfaEnrolled ? tMe("yes") : tMe("no")}</dd>
      </dl>
    </div>
  );
}

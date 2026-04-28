/**
 * T037: TopBar — identity + market badge + locale toggle + theme toggle
 * + bell mount + global-search opener + logout.
 *
 * Server Component; mounts the LocaleToggle and BellMenu Client
 * Components inline.
 */
import { useTranslations } from "next-intl";
import { LogOut, Search } from "lucide-react";
import { LocaleToggle } from "./locale-toggle";
import { BellMenu } from "./bell-menu";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Separator } from "@/components/ui/separator";
import { LogoutButton } from "./logout-button";
import type { AdminSessionPayload } from "@/lib/auth/session";

interface TopBarProps {
  session: AdminSessionPayload;
}

const MARKET_LABELS: Record<AdminSessionPayload["roleScope"], string> = {
  platform: "Platform",
  ksa: "KSA",
  eg: "EG",
};

export function TopBar({ session }: TopBarProps) {
  const t = useTranslations("shell.topbar");

  return (
    <header
      className="flex h-14 items-center gap-ds-md border-b border-border bg-card px-ds-md"
      role="banner"
    >
      <div className="flex flex-1 items-center gap-ds-md">
        {/* Global search opener — full search lands later. */}
        <Button variant="outline" size="sm" className="w-64 justify-start text-muted-foreground">
          <Search aria-hidden="true" className="me-ds-xs size-4" />
          <span className="text-sm">{t("search_placeholder")}</span>
        </Button>
      </div>

      <div className="flex items-center gap-ds-md">
        <Badge variant="secondary" aria-label={MARKET_LABELS[session.roleScope]}>
          {MARKET_LABELS[session.roleScope]}
        </Badge>
        <Separator orientation="vertical" className="h-6" />
        <LocaleToggle />
        <Separator orientation="vertical" className="h-6" />
        <BellMenu />
        <Separator orientation="vertical" className="h-6" />
        <div className="flex items-center gap-ds-sm">
          <span className="text-sm font-medium">{session.displayName}</span>
        </div>
        <LogoutButton />
      </div>
    </header>
  );
}

/**
 * Logout button — Client Component because it owns the POST + redirect.
 */
"use client";

import { useTransition } from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { LogOut } from "lucide-react";
import { Button } from "@/components/ui/button";

export function LogoutButton() {
  const t = useTranslations("shell.topbar");
  const router = useRouter();
  const [isPending, startTransition] = useTransition();

  function handleLogout() {
    startTransition(async () => {
      try {
        await fetch("/api/auth/logout", { method: "POST", credentials: "same-origin" });
      } finally {
        router.replace("/login");
        router.refresh();
      }
    });
  }

  return (
    <Button
      type="button"
      variant="ghost"
      size="sm"
      onClick={handleLogout}
      disabled={isPending}
      aria-label={t("logout")}
    >
      <LogOut aria-hidden="true" className="me-ds-xs size-4" />
      <span className="hidden sm:inline">{t("logout")}</span>
    </Button>
  );
}

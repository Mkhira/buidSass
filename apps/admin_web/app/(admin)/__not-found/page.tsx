/**
 * /__not-found — unknown route inside the (admin) tree. Per FR-028a.
 */
import Link from "next/link";
import { useTranslations } from "next-intl";
import { buttonVariants } from "@/components/ui/button";
import { FileQuestion } from "lucide-react";

export default function AdminNotFoundPage() {
  const t = useTranslations("shell.not_found");
  return (
    <main role="main" className="flex min-h-[60vh] flex-col items-center justify-center gap-ds-md p-ds-xl text-center">
      <FileQuestion aria-hidden="true" className="size-12 text-muted-foreground" />
      <h1 className="text-2xl font-semibold tracking-tight">{t("title")}</h1>
      <p className="max-w-md text-sm text-muted-foreground">{t("body")}</p>
      <Link href="/" className={buttonVariants({ variant: "default" })}>
        {t("cta")}
      </Link>
    </main>
  );
}

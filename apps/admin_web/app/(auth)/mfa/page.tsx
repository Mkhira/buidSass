/**
 * T050: /mfa page (Server Component) — shells the MfaForm.
 *
 * If the admin has a full session, redirect to landing.
 */
import { redirect } from "next/navigation";
import { getSession } from "@/lib/auth/session";
import { MfaForm } from "./mfa-form";

interface MfaPageProps {
  searchParams: { continueTo?: string };
}

export default async function MfaPage({ searchParams }: MfaPageProps) {
  const session = await getSession();
  if (session) {
    redirect(searchParams.continueTo ?? "/");
  }
  return <MfaForm />;
}

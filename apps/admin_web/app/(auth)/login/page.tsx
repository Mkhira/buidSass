/**
 * T048: /login (Server Component) — shells the LoginForm.
 *
 * If the admin already has a session, redirect straight to landing or
 * `?continueTo=…`.
 */
import { redirect } from "next/navigation";
import { getSession } from "@/lib/auth/session";
import { LoginForm } from "./login-form";

interface LoginPageProps {
  searchParams: { continueTo?: string };
}

export default async function LoginPage({ searchParams }: LoginPageProps) {
  const session = await getSession();
  if (session) {
    redirect(searchParams.continueTo ?? "/");
  }
  return <LoginForm />;
}

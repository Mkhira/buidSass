/**
 * T040: ToastHost — re-exports the sonner-backed Toaster mounted in
 * `app-providers.tsx` so feature code has a single import path.
 *
 * Use the imperative API:
 *   import { toast } from "sonner";
 *   toast.success("Saved");
 */
"use client";

export { Toaster as ToastHost } from "@/components/ui/sonner";
export { toast } from "sonner";

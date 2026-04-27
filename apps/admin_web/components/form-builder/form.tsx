/**
 * T043: FormBuilder primitive (FR-024).
 *
 * Thin wrapper over `react-hook-form` + `zod` + `@hookform/resolvers/zod`.
 * Provides:
 *  - typed fields
 *  - server-side validation surfacing (errors keyed by field name)
 *  - dirty-state warning on navigation (the consuming component wires
 *    a `<DirtyStateGuard>` per spec 016 FR-005 — primitive lives here)
 *  - optimistic disable on submit
 *
 * Feature components compose:
 *
 *   const form = useFormBuilder({ schema, defaultValues, onSubmit });
 *   <Form {...form}>
 *     <FormField name="..." />
 *   </Form>
 */
"use client";

import {
  useForm,
  type UseFormReturn,
  type DefaultValues,
  type SubmitHandler,
} from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { useEffect } from "react";
import type { ReactNode } from "react";
import type { z } from "zod";

export interface UseFormBuilderProps<TSchema extends z.ZodType> {
  schema: TSchema;
  defaultValues: DefaultValues<z.infer<TSchema>>;
  onSubmit: SubmitHandler<z.infer<TSchema>>;
}

export function useFormBuilder<TSchema extends z.ZodType>(
  props: UseFormBuilderProps<TSchema>,
): UseFormReturn<z.infer<TSchema>> & {
  submit: () => void;
  isSubmitting: boolean;
  isDirty: boolean;
} {
  const form = useForm<z.infer<TSchema>>({
    resolver: zodResolver(props.schema as z.ZodTypeAny),
    defaultValues: props.defaultValues,
    mode: "onBlur",
  });
  return Object.assign(form, {
    submit: form.handleSubmit(props.onSubmit),
    get isSubmitting() {
      return form.formState.isSubmitting;
    },
    get isDirty() {
      return form.formState.isDirty;
    },
  });
}

/**
 * DirtyStateGuard — beforeunload listener that prompts when the form is
 * dirty. The discard / save / cancel dialog (per spec 016 Q1) wraps
 * this with the `<ConfirmationDialog>` primitive.
 */
interface DirtyStateGuardProps {
  isDirty: boolean;
  /** Optional bypass for an explicit save/discard path. */
  bypass?: boolean;
}

export function DirtyStateGuard({ isDirty, bypass }: DirtyStateGuardProps) {
  useEffect(() => {
    if (!isDirty || bypass) return;
    function handler(e: BeforeUnloadEvent) {
      e.preventDefault();
      e.returnValue = "";
    }
    window.addEventListener("beforeunload", handler);
    return () => window.removeEventListener("beforeunload", handler);
  }, [isDirty, bypass]);
  return null;
}

/**
 * Map server-side ProblemDetails errors back onto the form. Given the
 * spec 003 envelope's `errors: Record<string, string[]>`, calls
 * `form.setError(field, …)` per entry. Unknown fields go onto the
 * top-level error slot.
 */
export function applyServerErrors<TSchema extends z.ZodType>(
  form: UseFormReturn<z.infer<TSchema>>,
  errors: Record<string, string[]>,
  fallback?: { onUnknown?: (message: string) => void },
): void {
  for (const [field, messages] of Object.entries(errors)) {
    const message = messages.join("; ");
    if (field in form.control._fields) {
      form.setError(field as never, { type: "server", message });
    } else {
      fallback?.onUnknown?.(message);
    }
  }
}

export interface FormShellProps {
  /** Optional id; useful when the submit button lives outside the form. */
  id?: string;
  /** The form element body. */
  children: ReactNode;
  onSubmit: () => void;
}

export function FormShell({ id, children, onSubmit }: FormShellProps) {
  return (
    <form
      id={id}
      onSubmit={(e) => {
        e.preventDefault();
        onSubmit();
      }}
      className="space-y-ds-md"
    >
      {children}
    </form>
  );
}

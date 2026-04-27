/**
 * T043: FormField — generic typed field with ARIA wiring.
 *
 * Renders the label + input + error message in a single block; pulls
 * the error from `react-hook-form`'s state.
 */
"use client";

import { Label } from "@/components/ui/label";
import { Input } from "@/components/ui/input";
import type { ReactNode } from "react";
import {
  type Control,
  type FieldPath,
  type FieldValues,
  Controller,
} from "react-hook-form";
import { cn } from "@/lib/utils";

export interface FormFieldProps<TValues extends FieldValues> {
  control: Control<TValues>;
  name: FieldPath<TValues>;
  label: string;
  type?: "text" | "email" | "password" | "tel" | "number";
  placeholder?: string;
  description?: string;
  required?: boolean;
  autoComplete?: string;
  /** Render-prop override for non-input controls (textarea, select, etc.). */
  render?: (props: {
    value: unknown;
    onChange: (value: unknown) => void;
    onBlur: () => void;
    name: string;
    invalid: boolean;
  }) => ReactNode;
}

export function FormField<TValues extends FieldValues>({
  control,
  name,
  label,
  type = "text",
  placeholder,
  description,
  required,
  autoComplete,
  render,
}: FormFieldProps<TValues>) {
  return (
    <Controller
      control={control}
      name={name}
      render={({ field, fieldState }) => {
        const id = `field-${String(name).replace(/\./g, "-")}`;
        const errorId = `${id}-error`;
        const descId = description ? `${id}-desc` : undefined;
        const hasError = Boolean(fieldState.error);
        return (
          <div className="space-y-ds-xs">
            <Label htmlFor={id} className={cn(hasError && "text-destructive")}>
              {label}
              {required ? <span aria-hidden="true"> *</span> : null}
            </Label>
            {render ? (
              render({
                value: field.value,
                onChange: field.onChange,
                onBlur: field.onBlur,
                name: field.name,
                invalid: hasError,
              })
            ) : (
              <Input
                id={id}
                type={type}
                placeholder={placeholder}
                autoComplete={autoComplete}
                aria-required={required}
                aria-invalid={hasError}
                aria-describedby={
                  [descId, hasError ? errorId : undefined].filter(Boolean).join(" ") || undefined
                }
                {...field}
                value={field.value ?? ""}
              />
            )}
            {description ? (
              <p id={descId} className="text-xs text-muted-foreground">
                {description}
              </p>
            ) : null}
            {hasError ? (
              <p id={errorId} role="alert" className="text-xs text-destructive">
                {fieldState.error?.message}
              </p>
            ) : null}
          </div>
        );
      }}
    />
  );
}

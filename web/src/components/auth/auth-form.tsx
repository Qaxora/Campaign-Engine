"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Alert, Field, Input } from "@/components/ui/form";
import { problemMessage } from "@/lib/problem";
import { safeRedirectPath } from "@/lib/session";
import { fieldErrors, loginSchema, registerSchema, type FieldErrors } from "@/lib/validation";

type Mode = "login" | "register";

const fields = {
  login: [
    { name: "email", label: "Work email", type: "email", autoComplete: "email" },
    { name: "password", label: "Password", type: "password", autoComplete: "current-password" },
  ],
  register: [
    { name: "name", label: "Your name", type: "text", autoComplete: "name" },
    { name: "organizationName", label: "Company", type: "text", autoComplete: "organization" },
    { name: "email", label: "Work email", type: "email", autoComplete: "email" },
    { name: "password", label: "Password", type: "password", autoComplete: "new-password", hint: "At least 8 characters." },
  ],
} as const;

export function AuthForm({ mode, next }: { mode: Mode; next?: string }) {
  const router = useRouter();
  const [errors, setErrors] = useState<FieldErrors<Record<string, string>>>({});
  const [formError, setFormError] = useState<string>();
  const [pending, setPending] = useState(false);

  async function onSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const values = Object.fromEntries(new FormData(event.currentTarget)) as Record<string, string>;
    const parsed = (mode === "login" ? loginSchema : registerSchema).safeParse(values);
    if (!parsed.success) {
      setErrors(fieldErrors(parsed.error) as FieldErrors<Record<string, string>>);
      return;
    }

    setErrors({});
    setFormError(undefined);
    setPending(true);
    try {
      const response = await fetch(`/api/session/${mode}`, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify(parsed.data),
      });
      if (!response.ok) {
        setFormError(problemMessage(await response.json().catch(() => null), response.status));
        return;
      }

      router.replace(safeRedirectPath(next));
      router.refresh();
    } catch {
      setFormError("Could not reach the server. Check your connection and try again.");
    } finally {
      setPending(false);
    }
  }

  return (
    <form onSubmit={onSubmit} noValidate className="flex flex-col gap-4">
      {formError ? <Alert>{formError}</Alert> : null}
      {fields[mode].map((field) => (
        <Field key={field.name} id={field.name} label={field.label} error={errors[field.name]} hint={"hint" in field ? field.hint : undefined}>
          <Input
            id={field.name}
            name={field.name}
            type={field.type}
            autoComplete={field.autoComplete}
            aria-invalid={errors[field.name] ? true : undefined}
            aria-describedby={errors[field.name] ? `${field.name}-error` : undefined}
            required
          />
        </Field>
      ))}
      <Button type="submit" disabled={pending} className="mt-2">
        {pending ? "Please wait…" : mode === "login" ? "Sign in" : "Create account"}
      </Button>
      <p className="text-center text-sm text-muted">
        {mode === "login" ? (
          <>
            New to Qaxora Campaign?{" "}
            <Link href="/register" className="font-medium text-brand hover:underline">
              Create an account
            </Link>
          </>
        ) : (
          <>
            Already have an account?{" "}
            <Link href="/login" className="font-medium text-brand hover:underline">
              Sign in
            </Link>
          </>
        )}
      </p>
    </form>
  );
}

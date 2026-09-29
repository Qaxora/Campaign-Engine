"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { Button, buttonClass } from "@/components/ui/button";
import { Alert, Field, Input } from "@/components/ui/form";
import { api } from "@/lib/api/client";
import { problemMessage } from "@/lib/problem";
import { organizationSchema } from "@/lib/validation";

export function CreateOrganizationForm({ canCancel }: { canCancel: boolean }) {
  const router = useRouter();
  const [error, setError] = useState<string>();
  const [pending, setPending] = useState(false);

  async function onSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const parsed = organizationSchema.safeParse(Object.fromEntries(new FormData(event.currentTarget)));
    if (!parsed.success) {
      setError(parsed.error.issues[0]?.message);
      return;
    }

    setPending(true);
    const { error: problem, response } = await api.POST("/api/v1/auth/organizations", { body: parsed.data });
    setPending(false);
    if (problem || !response.ok) {
      setError(problemMessage(problem, response.status));
      return;
    }

    router.replace("/");
    router.refresh();
  }

  return (
    <form onSubmit={onSubmit} noValidate className="flex flex-col gap-4">
      {error ? <Alert>{error}</Alert> : null}
      <Field id="name" label="Organization name">
        <Input id="name" name="name" autoComplete="organization" required />
      </Field>
      <div className="flex gap-2">
        <Button type="submit" disabled={pending} className="flex-1">
          {pending ? "Creating…" : "Create organization"}
        </Button>
        {canCancel ? (
          <Link href="/" className={buttonClass("secondary")}>
            Cancel
          </Link>
        ) : null}
      </div>
    </form>
  );
}

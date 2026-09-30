"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Alert, Field, Input } from "@/components/ui/form";
import { api } from "@/lib/api/client";
import { parseSkus } from "@/lib/catalog";
import { problemMessage } from "@/lib/problem";

export function CreateListForm({ kind }: { kind: "standard" | "globalExclusion" }) {
  const router = useRouter();
  const [error, setError] = useState<string>();
  const [pending, setPending] = useState(false);

  async function onSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    setPending(true);
    setError(undefined);
    const { data, error: problem, response } = await api.POST("/api/v1/product-lists", {
      body: {
        code: String(form.get("code") ?? "").trim().toUpperCase(),
        name: String(form.get("name") ?? "").trim(),
        description: String(form.get("description") ?? "").trim() || null,
        kind,
        skus: parseSkus(String(form.get("skus") ?? "")),
      },
    });
    setPending(false);
    if (!data) {
      setError(problemMessage(problem, response.status));
      return;
    }

    router.push(`/catalog/lists/${encodeURIComponent(data.code)}`);
  }

  return (
    <form onSubmit={onSubmit} className="grid gap-4 px-5 py-4 sm:grid-cols-2">
      {error ? (
        <div className="sm:col-span-2">
          <Alert>{error}</Alert>
        </div>
      ) : null}
      <Field id="list-code" label="Code" hint="Campaigns refer to the list by this code.">
        <Input id="list-code" name="code" required className="font-mono uppercase" placeholder={kind === "globalExclusion" ? "NEVER-DISCOUNT" : "SUMMER-26-TSHIRTS"} />
      </Field>
      <Field id="list-name" label="Name">
        <Input id="list-name" name="name" required />
      </Field>
      <Field id="list-description" label="Description">
        <Input id="list-description" name="description" />
      </Field>
      <Field id="list-skus" label="SKUs (optional)" hint="Paste one per line, or comma separated. You can also import a CSV later.">
        <textarea id="list-skus" name="skus" rows={3} className="rounded-md border border-border bg-surface px-3 py-2 font-mono text-sm" />
      </Field>
      <div className="sm:col-span-2">
        <Button type="submit" disabled={pending}>
          {pending ? "Creating…" : kind === "globalExclusion" ? "Create exclusion list" : "Create product list"}
        </Button>
      </div>
    </form>
  );
}

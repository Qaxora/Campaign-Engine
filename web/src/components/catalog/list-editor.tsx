"use client";

import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardHeader } from "@/components/ui/card";
import { Dialog } from "@/components/ui/dialog";
import { Alert, Field, Input } from "@/components/ui/form";
import { api } from "@/lib/api/client";
import type { components } from "@/lib/api/schema";
import { parseSkus } from "@/lib/catalog";
import { formatInteger } from "@/lib/format";
import { problemMessage } from "@/lib/problem";
import { CsvImport } from "./csv-import";

type ProductList = components["schemas"]["ProductList"];

/** Edits a product list through the API; every change is validated and audited there. */
export function ListEditor({ list, canManage }: { list: ProductList; canManage: boolean }) {
  const router = useRouter();
  const skus = useMemo(() => [...(list.skus ?? [])].sort((a, b) => a.localeCompare(b)), [list.skus]);
  const [filter, setFilter] = useState("");
  const [selected, setSelected] = useState<string[]>([]);
  const [adding, setAdding] = useState("");
  const [error, setError] = useState<string>();
  const [notice, setNotice] = useState<string>();
  const [confirmDelete, setConfirmDelete] = useState(false);
  const code = list.code;
  const path = { params: { path: { code } } };

  const visible = filter ? skus.filter((s) => s.toLowerCase().includes(filter.toLowerCase())) : skus;
  const shown = visible.slice(0, 500);

  async function call<T>(action: () => Promise<{ data?: T; error?: unknown; response: Response }>, success: string) {
    setError(undefined);
    setNotice(undefined);
    const { error: problem, response } = await action();
    if (!response.ok) {
      setError(problemMessage(problem, response.status));
      return false;
    }

    setNotice(success);
    router.refresh();
    return true;
  }

  async function saveDetails(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    await call(
      () =>
        api.PUT("/api/v1/product-lists/{code}", {
          ...path,
          body: {
            name: String(form.get("name") ?? "").trim(),
            description: String(form.get("description") ?? "").trim() || null,
            kind: form.get("kind") === "globalExclusion" ? "globalExclusion" : "standard",
          },
        }),
      "Details saved.",
    );
  }

  async function add() {
    const values = parseSkus(adding);
    if (values.length === 0) return;
    if (await call(() => api.POST("/api/v1/product-lists/{code}/skus", { ...path, body: { skus: values } }), `${values.length} SKU(s) added.`)) setAdding("");
  }

  async function remove() {
    if (await call(() => api.POST("/api/v1/product-lists/{code}/skus/remove", { ...path, body: { skus: selected } }), `${selected.length} SKU(s) removed.`)) setSelected([]);
  }

  async function destroy() {
    setConfirmDelete(false);
    const { error: problem, response } = await api.DELETE("/api/v1/product-lists/{code}", path);
    if (!response.ok) {
      setError(problemMessage(problem, response.status));
      return;
    }

    router.push(list.kind === "globalExclusion" ? "/catalog/exclusions" : "/catalog/lists");
    router.refresh();
  }

  return (
    <div className="grid gap-6 xl:grid-cols-[1fr_22rem]">
      <Card>
        <CardHeader title={`SKUs (${formatInteger(skus.length)})`} description="Carts send these SKUs on their lines; matching is case-insensitive." />
        <div className="space-y-3 px-5 py-4">
          {error ? <Alert>{error}</Alert> : null}
          {notice ? <Alert tone="success">{notice}</Alert> : null}
          <div className="flex flex-wrap items-center gap-2">
            <Input aria-label="Filter SKUs" placeholder="Filter SKUs" value={filter} onChange={(e) => setFilter(e.target.value)} className="max-w-xs" />
            {canManage && selected.length > 0 ? (
              <Button variant="secondary" size="sm" onClick={remove}>
                <Trash2 className="size-4" aria-hidden /> Remove {selected.length}
              </Button>
            ) : null}
          </div>
          {skus.length === 0 ? (
            <p className="py-6 text-center text-sm text-muted">The list is empty. Add SKUs or import a CSV.</p>
          ) : (
            <ul className="grid max-h-[28rem] gap-x-4 overflow-y-auto sm:grid-cols-2 lg:grid-cols-3">
              {shown.map((sku) => (
                <li key={sku} className="flex items-center gap-2 border-b border-border py-1.5 font-mono text-sm">
                  {canManage ? (
                    <input
                      type="checkbox"
                      aria-label={`Select ${sku}`}
                      checked={selected.includes(sku)}
                      onChange={(e) => setSelected(e.target.checked ? [...selected, sku] : selected.filter((s) => s !== sku))}
                    />
                  ) : null}
                  {sku}
                </li>
              ))}
            </ul>
          )}
          {visible.length > shown.length ? <p className="text-xs text-muted">Showing the first {shown.length} of {formatInteger(visible.length)} — filter to narrow down.</p> : null}
        </div>
      </Card>

      {canManage ? (
        <div className="space-y-6">
          <Card>
            <CardHeader title="Add SKUs" />
            <div className="space-y-3 px-5 py-4">
              <textarea
                aria-label="SKUs to add"
                value={adding}
                onChange={(e) => setAdding(e.target.value)}
                rows={4}
                placeholder="One per line, or comma separated"
                className="w-full rounded-md border border-border bg-surface px-3 py-2 font-mono text-sm"
              />
              <Button size="sm" onClick={add} disabled={parseSkus(adding).length === 0}>
                <Plus className="size-4" aria-hidden /> Add {parseSkus(adding).length || ""}
              </Button>
              <div className="border-t border-border pt-3">
                <p className="mb-2 text-sm text-muted">Or import a file (first column, optional `sku` header):</p>
                <CsvImport
                  endpoint={`/api/v1/product-lists/${encodeURIComponent(code)}/skus/import`}
                  modes={[
                    { value: "append", label: "Add to the list" },
                    { value: "replace", label: "Replace the list" },
                  ]}
                  result="skus"
                />
              </div>
            </div>
          </Card>

          <Card>
            <CardHeader title="Details" />
            <form onSubmit={saveDetails} className="space-y-3 px-5 py-4">
              <Field id="name" label="Name">
                <Input id="name" name="name" defaultValue={list.name} required />
              </Field>
              <Field id="description" label="Description">
                <Input id="description" name="description" defaultValue={list.description ?? ""} />
              </Field>
              <Field id="kind" label="Kind" hint="Global exclusions are never discounted by any campaign.">
                <select id="kind" name="kind" defaultValue={list.kind ?? "standard"} className="h-10 rounded-md border border-border bg-surface px-2 text-sm">
                  <option value="standard">Standard (campaigns target or exclude it)</option>
                  <option value="globalExclusion">Global exclusion (never discounted)</option>
                </select>
              </Field>
              <div className="flex justify-between gap-2">
                <Button type="submit" size="sm" variant="secondary">
                  Save details
                </Button>
                <Button type="button" size="sm" variant="ghost" onClick={() => setConfirmDelete(true)}>
                  <Trash2 className="size-4" aria-hidden /> Delete list
                </Button>
              </div>
            </form>
          </Card>
        </div>
      ) : null}

      <Dialog
        open={confirmDelete}
        title={`Delete ${code}?`}
        onClose={() => setConfirmDelete(false)}
        footer={
          <>
            <Button variant="secondary" onClick={() => setConfirmDelete(false)}>
              Cancel
            </Button>
            <Button variant="danger" onClick={destroy}>
              Delete
            </Button>
          </>
        }
      >
        <p className="px-5 py-4 text-sm text-muted">The API refuses to delete a list that a campaign still uses.</p>
      </Dialog>
    </div>
  );
}

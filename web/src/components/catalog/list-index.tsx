import Link from "next/link";
import { ListChecks, ShieldBan } from "lucide-react";
import { Card, CardHeader } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import { getSession, serverApi } from "@/lib/api/server";
import { formatInteger, formatRelative } from "@/lib/format";
import { CreateListForm } from "./create-list-form";

/** Product lists of one kind: standard lists or global exclusions. */
export async function ListIndex({ kind }: { kind: "standard" | "globalExclusion" }) {
  const session = (await getSession())!;
  const canManage = session.current!.role !== "member";
  const api = await serverApi();
  const { data, response } = await api.GET("/api/v1/product-lists");
  if (!data) throw new Error(`Product lists are unavailable (HTTP ${response.status}).`);
  const lists = data.filter((l) => l.kind === kind);
  const now = new Date();
  const exclusion = kind === "globalExclusion";

  return (
    <div className="space-y-6">
      {lists.length === 0 ? (
        <EmptyState icon={exclusion ? ShieldBan : ListChecks} title={exclusion ? "No global exclusions yet" : "No product lists yet"}>
          {exclusion
            ? "Add products no campaign may discount: tobacco, gold, gift cards, regulated items."
            : "Lists group SKUs that campaigns target or exclude, e.g. a season's assortment."}
        </EmptyState>
      ) : (
        <Card className="overflow-hidden">
          <table className="w-full text-sm">
            <thead className="border-b border-border bg-surface-muted/60 text-left text-xs text-muted">
              <tr>
                <th className="px-4 py-2.5 font-medium">List</th>
                <th className="px-4 py-2.5 text-right font-medium">SKUs</th>
                <th className="hidden px-4 py-2.5 font-medium sm:table-cell">Description</th>
                <th className="px-4 py-2.5 text-right font-medium">Updated</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {lists.map((l) => (
                <tr key={l.id} className="hover:bg-surface-muted/60">
                  <td className="px-4 py-3">
                    <Link href={`/catalog/lists/${encodeURIComponent(l.code)}`} className="font-medium hover:text-brand hover:underline">
                      {l.name}
                    </Link>
                    <p className="font-mono text-xs text-muted">{l.code}</p>
                  </td>
                  <td className="px-4 py-3 text-right tabular-nums">{formatInteger(l.skuCount)}</td>
                  <td className="hidden px-4 py-3 text-muted sm:table-cell">{l.description ?? "—"}</td>
                  <td className="px-4 py-3 text-right text-muted">{formatRelative(l.updatedAt, now)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </Card>
      )}

      {canManage ? (
        <Card>
          <CardHeader title={exclusion ? "New exclusion list" : "New product list"} />
          <CreateListForm kind={kind} />
        </Card>
      ) : null}
    </div>
  );
}

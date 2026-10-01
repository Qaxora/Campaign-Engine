import type { Metadata } from "next";
import Link from "next/link";
import { Receipt, SearchX } from "lucide-react";
import { SaleStatus } from "@/components/sales/status";
import { buttonClass } from "@/components/ui/button";
import { Card, PageHeader } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import { Input, Label } from "@/components/ui/form";
import { Pagination } from "@/components/ui/pagination";
import { serverApi } from "@/lib/api/server";
import { formatAmount, formatInteger, formatRelative, toNumber } from "@/lib/format";
import { dayRange, hrefWith, pageParam, param } from "@/lib/query";

export const metadata: Metadata = { title: "Transactions" };

const PAGE_SIZE = 25;
const select = "h-10 rounded-md border border-border bg-surface px-2 text-sm";

export default async function TransactionsPage({ searchParams }: PageProps<"/sales/transactions">) {
  const params = await searchParams;
  const f = {
    search: param(params, "search"),
    status: ["confirmed", "reversed"].includes(param(params, "status")) ? param(params, "status") : "",
    channel: param(params, "channel"),
    storeId: param(params, "storeId"),
    customerId: param(params, "customerId"),
    campaign: param(params, "campaign"),
    offline: ["true", "false"].includes(param(params, "offline")) ? param(params, "offline") : "",
    from: param(params, "from"),
    to: param(params, "to"),
  };
  const page = pageParam(params);
  const api = await serverApi();
  const [result, stores] = await Promise.all([
    api.GET("/api/v1/ledger/transactions", {
      params: {
        query: {
          ...dayRange(f.from, f.to),
          search: f.search || undefined,
          status: f.status || undefined,
          channel: f.channel || undefined,
          storeId: f.storeId || undefined,
          customerId: f.customerId || undefined,
          campaign: f.campaign || undefined,
          offline: f.offline === "" ? undefined : f.offline === "true",
          page,
          pageSize: PAGE_SIZE,
        },
      },
    }),
    api.GET("/api/v1/stores"),
  ]);
  if (!result.data) throw new Error(`The ledger is unavailable (HTTP ${result.response.status}).`);

  const total = toNumber(result.data.totalCount);
  const filtered = Object.values(f).some(Boolean);
  const now = new Date();

  return (
    <>
      <PageHeader title="Transactions" description="Every sale recorded through the integration API — online and offline — with the campaigns it used." />

      <form method="get" className="mb-4 grid gap-3 rounded-lg border border-border bg-surface p-4 sm:grid-cols-2 lg:grid-cols-4">
        <Filter id="search" label="Receipt / order no." defaultValue={f.search} />
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="status">Status</Label>
          <select id="status" name="status" defaultValue={f.status} className={select}>
            <option value="">Any</option>
            <option value="confirmed">Confirmed</option>
            <option value="reversed">Reversed</option>
          </select>
        </div>
        <Filter id="channel" label="Channel" defaultValue={f.channel} placeholder="store, web…" />
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="storeId">Store</Label>
          <select id="storeId" name="storeId" defaultValue={f.storeId} className={select}>
            <option value="">Any store</option>
            {(stores.data ?? []).map((s) => (
              <option key={s.code} value={s.code}>
                {s.code} — {s.name}
              </option>
            ))}
          </select>
        </div>
        <Filter id="customerId" label="Customer" defaultValue={f.customerId} />
        <Filter id="campaign" label="Campaign code" defaultValue={f.campaign} />
        <Filter id="from" label="From" type="date" defaultValue={f.from} />
        <Filter id="to" label="To" type="date" defaultValue={f.to} />
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="offline">Source</Label>
          <select id="offline" name="offline" defaultValue={f.offline} className={select}>
            <option value="">Online and offline</option>
            <option value="false">Online (priced by the API)</option>
            <option value="true">Offline imports</option>
          </select>
        </div>
        <div className="flex items-end gap-2 sm:col-span-2 lg:col-span-3">
          <button type="submit" className={buttonClass("secondary", "sm")}>
            Apply filters
          </button>
          {filtered ? (
            <Link href="/sales/transactions" className={buttonClass("ghost", "sm")}>
              Clear
            </Link>
          ) : null}
          <span className="ml-auto text-sm text-muted">{formatInteger(total)} transactions</span>
        </div>
      </form>

      {result.data.items.length === 0 ? (
        filtered ? (
          <EmptyState icon={SearchX} title="No transactions match these filters" />
        ) : (
          <EmptyState icon={Receipt} title="No sales recorded yet">
            Sales appear here when your channels call <code className="font-mono text-xs">POST /api/v1/redemptions</code>.
          </EmptyState>
        )
      ) : (
        <Card className="overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full min-w-[760px] text-sm">
              <thead className="border-b border-border bg-surface-muted/60 text-left text-xs text-muted">
                <tr>
                  <th className="px-4 py-2.5 font-medium">Transaction</th>
                  <th className="px-4 py-2.5 font-medium">Where</th>
                  <th className="px-4 py-2.5 font-medium">Customer</th>
                  <th className="px-4 py-2.5 text-right font-medium">Campaigns</th>
                  <th className="px-4 py-2.5 text-right font-medium">Discount</th>
                  <th className="px-4 py-2.5 font-medium">Status</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {result.data.items.map((t) => (
                  <tr key={t.transactionId} className="hover:bg-surface-muted/60">
                    <td className="px-4 py-3">
                      <Link href={`/sales/transactions/${encodeURIComponent(t.transactionId)}`} className="font-mono font-medium hover:text-brand hover:underline">
                        {t.transactionId}
                      </Link>
                      <p className="text-xs text-muted">{formatRelative(t.createdAt, now)}</p>
                    </td>
                    <td className="px-4 py-3 text-muted">{[t.channel, t.storeId].filter(Boolean).join(" · ") || "—"}</td>
                    <td className="px-4 py-3 text-muted">{t.customerId ?? "Anonymous"}</td>
                    <td className="px-4 py-3 text-right tabular-nums">{formatInteger(t.campaignCount)}</td>
                    <td className="px-4 py-3 text-right tabular-nums">
                      {formatAmount(t.totalDiscount)} <span className="text-xs text-muted">{t.currency}</span>
                    </td>
                    <td className="px-4 py-3">
                      <SaleStatus status={t.status} offline={t.offline} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pagination page={page} pages={Math.ceil(total / PAGE_SIZE)} href={(p) => hrefWith("/sales/transactions", { ...f, page: p })} />
        </Card>
      )}
    </>
  );
}

function Filter({ id, label, defaultValue, placeholder, type }: { id: string; label: string; defaultValue: string; placeholder?: string; type?: string }) {
  return (
    <div className="flex flex-col gap-1.5">
      <Label htmlFor={id}>{label}</Label>
      <Input id={id} name={id} type={type} defaultValue={defaultValue} placeholder={placeholder} />
    </div>
  );
}

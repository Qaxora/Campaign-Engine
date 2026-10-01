import type { Metadata } from "next";
import Link from "next/link";
import { BadgePercent, SearchX } from "lucide-react";
import { SaleStatus } from "@/components/sales/status";
import { buttonClass } from "@/components/ui/button";
import { Card, PageHeader } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import { Input, Label } from "@/components/ui/form";
import { Pagination } from "@/components/ui/pagination";
import { serverApi } from "@/lib/api/server";
import { formatAmount, formatInteger, formatRelative, toNumber } from "@/lib/format";
import { dayRange, hrefWith, pageParam, param } from "@/lib/query";

export const metadata: Metadata = { title: "Redemptions" };

const PAGE_SIZE = 30;

export default async function RedemptionsPage({ searchParams }: PageProps<"/sales/redemptions">) {
  const params = await searchParams;
  const f = {
    campaign: param(params, "campaign"),
    coupon: param(params, "coupon"),
    customerId: param(params, "customerId"),
    status: ["confirmed", "reversed"].includes(param(params, "status")) ? param(params, "status") : "",
    from: param(params, "from"),
    to: param(params, "to"),
  };
  const page = pageParam(params);
  const api = await serverApi();
  const { data, response } = await api.GET("/api/v1/ledger/redemptions", {
    params: {
      query: {
        ...dayRange(f.from, f.to),
        campaign: f.campaign || undefined,
        coupon: f.coupon || undefined,
        customerId: f.customerId || undefined,
        status: f.status || undefined,
        page,
        pageSize: PAGE_SIZE,
      },
    },
  });
  if (!data) throw new Error(`The ledger is unavailable (HTTP ${response.status}).`);
  const total = toNumber(data.totalCount);
  const filtered = Object.values(f).some(Boolean);
  const now = new Date();

  return (
    <>
      <PageHeader title="Redemptions" description="Every campaign discount given: one row per campaign per sale, including coupon codes." />

      <form method="get" className="mb-4 grid gap-3 rounded-lg border border-border bg-surface p-4 sm:grid-cols-3 lg:grid-cols-6">
        {(
          [
            ["campaign", "Campaign code", ""],
            ["coupon", "Coupon code", ""],
            ["customerId", "Customer", ""],
            ["from", "From", "date"],
            ["to", "To", "date"],
          ] as const
        ).map(([id, label, type]) => (
          <div key={id} className="flex flex-col gap-1.5">
            <Label htmlFor={id}>{label}</Label>
            <Input id={id} name={id} type={type || undefined} defaultValue={f[id]} />
          </div>
        ))}
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="status">Status</Label>
          <select id="status" name="status" defaultValue={f.status} className="h-10 rounded-md border border-border bg-surface px-2 text-sm">
            <option value="">Any</option>
            <option value="confirmed">Confirmed</option>
            <option value="reversed">Reversed</option>
          </select>
        </div>
        <div className="flex items-center gap-2 sm:col-span-3 lg:col-span-6">
          <button type="submit" className={buttonClass("secondary", "sm")}>
            Apply filters
          </button>
          {filtered ? (
            <Link href="/sales/redemptions" className={buttonClass("ghost", "sm")}>
              Clear
            </Link>
          ) : null}
          <span className="ml-auto text-sm text-muted">{formatInteger(total)} redemptions</span>
        </div>
      </form>

      {data.items.length === 0 ? (
        filtered ? <EmptyState icon={SearchX} title="No redemptions match these filters" /> : <EmptyState icon={BadgePercent} title="No campaign has been redeemed yet" />
      ) : (
        <Card className="overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full min-w-[760px] text-sm">
              <thead className="border-b border-border bg-surface-muted/60 text-left text-xs text-muted">
                <tr>
                  <th className="px-4 py-2.5 font-medium">Campaign</th>
                  <th className="px-4 py-2.5 font-medium">Sale</th>
                  <th className="px-4 py-2.5 font-medium">Coupon</th>
                  <th className="px-4 py-2.5 font-medium">Customer</th>
                  <th className="px-4 py-2.5 text-right font-medium">Discount</th>
                  <th className="px-4 py-2.5 font-medium">Status</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {data.items.map((r) => (
                  <tr key={`${r.transactionId}-${r.campaignId}`} className="hover:bg-surface-muted/60">
                    <td className="px-4 py-3">
                      <Link href={`/campaigns/${r.campaignId}`} className="font-medium hover:text-brand hover:underline">
                        {r.campaignName ?? r.campaignCode}
                      </Link>
                      <p className="font-mono text-xs text-muted">{r.campaignCode}</p>
                    </td>
                    <td className="px-4 py-3">
                      <Link href={`/sales/transactions/${encodeURIComponent(r.transactionId)}`} className="font-mono text-xs hover:text-brand hover:underline">
                        {r.transactionId}
                      </Link>
                      <p className="text-xs text-muted">
                        {[r.channel, r.storeId].filter(Boolean).join(" · ")} · {formatRelative(r.soldAt, now)}
                      </p>
                    </td>
                    <td className="px-4 py-3 font-mono text-xs">{r.couponCode ?? "—"}</td>
                    <td className="px-4 py-3 text-muted">{r.customerId ?? "Anonymous"}</td>
                    <td className="px-4 py-3 text-right tabular-nums">
                      {formatAmount(r.discount)} <span className="text-xs text-muted">{r.currency}</span>
                    </td>
                    <td className="px-4 py-3">
                      <SaleStatus status={r.status} offline={r.offline} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pagination page={page} pages={Math.ceil(total / PAGE_SIZE)} href={(p) => hrefWith("/sales/redemptions", { ...f, page: p })} />
        </Card>
      )}
    </>
  );
}

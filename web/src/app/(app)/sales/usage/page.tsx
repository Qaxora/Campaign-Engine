import type { Metadata } from "next";
import Link from "next/link";
import { Gauge, UserSearch } from "lucide-react";
import { StatusBadge } from "@/components/campaigns/badges";
import { buttonClass } from "@/components/ui/button";
import { Card, CardHeader, PageHeader } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import { Input, Label } from "@/components/ui/form";
import { serverApi } from "@/lib/api/server";
import { formatAmount, formatInteger, formatPercent, formatRelative, toNumber } from "@/lib/format";
import { param } from "@/lib/query";

export const metadata: Metadata = { title: "Usage" };

/** Limit and budget consumption per campaign (from the ledger) and a lookup of one customer's usage. */
export default async function UsagePage({ searchParams }: PageProps<"/sales/usage">) {
  const params = await searchParams;
  const customerId = param(params, "customer");
  const api = await serverApi();
  const [usage, customer] = await Promise.all([
    api.GET("/api/v1/ledger/usage/campaigns"),
    customerId ? api.GET("/api/v1/ledger/usage/customers/{customerId}", { params: { path: { customerId } } }) : Promise.resolve(undefined),
  ]);
  if (!usage.data) throw new Error(`Usage is unavailable (HTTP ${usage.response.status}).`);
  const rows = usage.data.filter((u) => u.status !== "archived" || toNumber(u.redemptions) > 0);
  const now = new Date();

  return (
    <>
      <PageHeader title="Usage" description="How much of each campaign's limits and budget has been used. Reversed sales give their usage back." />

      <Card className="mb-6">
        <CardHeader title="Campaigns" />
        {rows.length === 0 ? (
          <div className="p-5">
            <EmptyState icon={Gauge} title="No campaigns yet" />
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[760px] text-sm">
              <thead className="border-b border-border text-left text-xs text-muted">
                <tr>
                  <th className="px-5 py-2.5 font-medium">Campaign</th>
                  <th className="px-5 py-2.5 text-right font-medium">Redemptions</th>
                  <th className="px-5 py-2.5 text-right font-medium">Discount</th>
                  <th className="w-64 px-5 py-2.5 font-medium">Budget</th>
                  <th className="px-5 py-2.5 text-right font-medium">Reversed</th>
                  <th className="px-5 py-2.5 text-right font-medium">Last used</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {rows.map((u) => {
                  const budget = u.budget == null ? null : toNumber(u.budget);
                  const used = budget ? Math.min(1, toNumber(u.discountTotal) / budget) : 0;
                  return (
                    <tr key={u.campaignId}>
                      <td className="px-5 py-3">
                        <div className="flex items-center gap-2">
                          <Link href={`/campaigns/${u.campaignId}`} className="font-medium hover:text-brand hover:underline">
                            {u.name}
                          </Link>
                          <StatusBadge status={u.status} />
                        </div>
                        <p className="font-mono text-xs text-muted">{u.code}</p>
                      </td>
                      <td className="px-5 py-3 text-right tabular-nums">
                        {formatInteger(u.redemptions)}
                        {u.maxRedemptions != null ? <span className="text-muted"> / {formatInteger(u.maxRedemptions)}</span> : null}
                      </td>
                      <td className="px-5 py-3 text-right tabular-nums">{formatAmount(u.discountTotal)}</td>
                      <td className="px-5 py-3">
                        {budget == null ? (
                          <span className="text-muted">No budget</span>
                        ) : (
                          <div>
                            <div className="h-1.5 rounded-full bg-surface-muted" role="meter" aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round(used * 100)} aria-label={`${u.code} budget used`}>
                              <div className={`h-1.5 rounded-full ${used >= 0.9 ? "bg-warning" : "bg-chart-1"}`} style={{ width: `${used * 100}%` }} />
                            </div>
                            <p className="mt-1 text-xs text-muted">
                              {formatPercent(used)} used · {formatAmount(u.budgetRemaining)} left of {formatAmount(budget)}
                            </p>
                          </div>
                        )}
                      </td>
                      <td className="px-5 py-3 text-right tabular-nums">{formatInteger(u.reversedRedemptions)}</td>
                      <td className="px-5 py-3 text-right text-muted">{u.lastRedeemedAt ? formatRelative(u.lastRedeemedAt, now) : "Never"}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      <Card>
        <CardHeader title="Customer usage" description="What one customer has redeemed, against per-customer limits." />
        <form method="get" className="flex flex-wrap items-end gap-2 px-5 py-4">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="customer">Customer id</Label>
            <Input id="customer" name="customer" defaultValue={customerId} placeholder="As sent by your channels" className="w-64" />
          </div>
          <button type="submit" className={buttonClass("secondary")}>
            Look up
          </button>
        </form>
        {customer?.data ? (
          customer.data.campaigns.length === 0 ? (
            <div className="px-5 pb-5">
              <EmptyState icon={UserSearch} title={`No redemptions for ${customerId}`} />
            </div>
          ) : (
            <>
              <p className="border-t border-border px-5 py-3 text-sm">
                <span className="font-medium">{customerId}</span>: {formatInteger(customer.data.transactions)} sales with campaigns, {formatAmount(customer.data.totalDiscount)} discount in total.
              </p>
              <table className="w-full text-sm">
                <tbody className="divide-y divide-border border-t border-border">
                  {customer.data.campaigns.map((c) => (
                    <tr key={c.campaignCode}>
                      <td className="px-5 py-3">
                        <p className="font-medium">{c.campaignName ?? c.campaignCode}</p>
                        <p className="font-mono text-xs text-muted">{c.campaignCode}</p>
                      </td>
                      <td className="px-5 py-3 text-right tabular-nums">
                        {formatInteger(c.redemptions)}
                        {c.maxRedemptionsPerCustomer != null ? <span className="text-muted"> / {formatInteger(c.maxRedemptionsPerCustomer)} allowed</span> : null}
                        {c.maxRedemptionsPerCustomer != null && toNumber(c.redemptions) > toNumber(c.maxRedemptionsPerCustomer) ? (
                          <span className="block text-xs text-warning">Over the limit — offline sales are recorded even when a limit is reached</span>
                        ) : null}
                      </td>
                      <td className="px-5 py-3 text-right tabular-nums">{formatAmount(c.discount)}</td>
                      <td className="px-5 py-3 text-right text-muted">{formatRelative(c.lastRedeemedAt, now)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </>
          )
        ) : null}
      </Card>
    </>
  );
}

import type { Metadata } from "next";
import Link from "next/link";
import { CheckCircle2, Circle, History, Trophy } from "lucide-react";
import { Kpi } from "@/components/dashboard/kpi";
import { PeriodPicker } from "@/components/dashboard/period-picker";
import { TrendChart } from "@/components/dashboard/trend-chart";
import { Card, CardHeader, PageHeader } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import { getSession, serverApi } from "@/lib/api/server";
import { formatAmount, formatInteger, formatPercent, formatRelative, periodChange, toNumber } from "@/lib/format";
import { resolvePeriod } from "@/lib/period";

export const metadata: Metadata = { title: "Dashboard" };

/** KPIs, trend, top campaigns and recent activity — all computed by the API from campaigns and the ledger. */
export default async function DashboardPage({ searchParams }: PageProps<"/">) {
  const period = resolvePeriod((await searchParams).period);
  const session = (await getSession())!;
  const canManage = session.current!.role !== "member";
  const api = await serverApi();
  const range = { from: period.from, to: period.to };

  const [overview, top, activity, stores, keys] = await Promise.all([
    api.GET("/api/v1/analytics/overview", { params: { query: range } }),
    api.GET("/api/v1/analytics/top-campaigns", { params: { query: { ...range, limit: 5 } } }),
    api.GET("/api/v1/audit", { params: { query: { pageSize: 8 } } }),
    api.GET("/api/v1/stores"),
    canManage ? api.GET("/api/v1/api-keys") : Promise.resolve(undefined),
  ]);
  if (!overview.data) throw new Error(`Analytics are unavailable (HTTP ${overview.response.status}).`);

  const o = overview.data;
  const steps = [
    { done: (stores.data?.length ?? 0) > 0, title: "Register your stores", href: "/settings/stores" },
    ...(canManage ? [{ done: (keys?.data ?? []).some((k) => !k.revokedAt), title: "Create an API key", href: "/integrations/api-keys" }] : []),
    { done: toNumber(o.totalCampaigns) > 0, title: "Create your first campaign", href: "/campaigns/new" },
  ];
  const now = new Date();

  return (
    <>
      <PageHeader
        title="Dashboard"
        description={`${session.current!.organizationName} · last ${period.label}`}
        actions={<PeriodPicker current={period.key} />}
      />

      {steps.some((s) => !s.done) ? (
        <Card className="mb-6">
          <CardHeader title="Get started" description={`${steps.filter((s) => s.done).length} of ${steps.length} done`} />
          <ul className="grid gap-px bg-border sm:grid-cols-3">
            {steps.map((step) => (
              <li key={step.title} className="bg-surface">
                <Link href={step.href} className="flex items-center gap-3 px-5 py-3 text-sm hover:bg-surface-muted">
                  {step.done ? <CheckCircle2 className="size-5 text-success" aria-label="Done" /> : <Circle className="size-5 text-muted" aria-label="To do" />}
                  {step.title}
                </Link>
              </li>
            ))}
          </ul>
        </Card>
      ) : null}

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <Kpi label="Active campaigns" value={formatInteger(o.activeCampaigns)} hint={`${formatInteger(o.totalCampaigns)} in total · ${formatInteger(o.draftCampaigns)} ${toNumber(o.draftCampaigns) === 1 ? "draft" : "drafts"}`} />
        <Kpi label="Redemptions" value={formatInteger(o.current.redemptions)} change={periodChange(o.current.redemptions, o.previous.redemptions)} />
        <Kpi label="Discount given" value={formatAmount(o.current.discountTotal)} change={periodChange(o.current.discountTotal, o.previous.discountTotal)} />
        <Kpi
          label="Avg. discount per sale"
          value={formatAmount(o.current.averageDiscountPerTransaction)}
          change={periodChange(o.current.averageDiscountPerTransaction, o.previous.averageDiscountPerTransaction)}
        />
      </div>

      <div className="mt-6 grid gap-6 xl:grid-cols-3">
        <Card className="xl:col-span-2">
          <CardHeader title="Redemption trend" description={`${formatInteger(o.current.transactions)} sales used campaigns in the last ${period.label}`} />
          <TrendChart from={period.from} to={period.to} />
        </Card>

        <Card>
          <CardHeader title="Top campaigns" description="By discount given in the period" />
          {(top.data ?? []).filter((c) => toNumber(c.redemptions) > 0).length === 0 ? (
            <div className="p-5">
              <EmptyState icon={Trophy} title="No redemptions yet">
                Campaigns appear here once sales use them.
              </EmptyState>
            </div>
          ) : (
            <ol className="divide-y divide-border">
              {top.data!
                .filter((c) => toNumber(c.redemptions) > 0)
                .map((c) => (
                  <li key={c.campaignId}>
                    <Link href={`/campaigns/${c.campaignId}`} className="block px-5 py-3 hover:bg-surface-muted">
                      <div className="flex items-baseline justify-between gap-3">
                        <span className="truncate text-sm font-medium">{c.name}</span>
                        <span className="shrink-0 text-sm tabular-nums">{formatAmount(c.discount)}</span>
                      </div>
                      <div className="mt-1 flex items-center gap-3">
                        <div className="h-1.5 flex-1 rounded-full bg-surface-muted" aria-hidden>
                          <div className="h-1.5 rounded-full bg-chart-1" style={{ width: formatPercent(c.discountShare, 1) }} />
                        </div>
                        <span className="w-24 shrink-0 text-right text-xs text-muted">
                          {formatPercent(c.discountShare)} · {formatInteger(c.redemptions)} uses
                        </span>
                      </div>
                    </Link>
                  </li>
                ))}
            </ol>
          )}
        </Card>
      </div>

      <Card className="mt-6">
        <CardHeader title="Recent activity" description="Changes made by people and integrations" />
        {(activity.data?.items ?? []).length === 0 ? (
          <div className="p-5">
            <EmptyState icon={History} title="Nothing has changed yet">
              Campaign, store, key and member changes are listed here.
            </EmptyState>
          </div>
        ) : (
          <ul className="divide-y divide-border">
            {activity.data!.items.map((entry) => (
              <li key={entry.id} className="flex flex-col gap-0.5 px-5 py-3 sm:flex-row sm:items-center sm:gap-4">
                <span className="flex-1 text-sm">{entry.summary}</span>
                <span className="text-xs text-muted">
                  {entry.actorName} · <time dateTime={entry.createdAt}>{formatRelative(entry.createdAt, now)}</time>
                </span>
              </li>
            ))}
          </ul>
        )}
      </Card>
    </>
  );
}

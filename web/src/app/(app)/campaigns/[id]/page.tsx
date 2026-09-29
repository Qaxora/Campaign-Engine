import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { ArrowLeft, ShieldCheck } from "lucide-react";
import { StatusBadge } from "@/components/campaigns/badges";
import { ConflictList } from "@/components/campaigns/conflict-list";
import { LifecycleActions } from "@/components/campaigns/lifecycle-actions";
import { Kpi } from "@/components/dashboard/kpi";
import { Badge, Card, CardHeader } from "@/components/ui/card";
import { getSession, serverApi } from "@/lib/api/server";
import { formatAmount, formatDate, formatInteger, formatPercent, formatRelative, toNumber } from "@/lib/format";

export const metadata: Metadata = { title: "Campaign" };

export default async function CampaignPage({ params }: PageProps<"/campaigns/[id]">) {
  const { id } = await params;
  const session = (await getSession())!;
  const canManage = session.current!.role !== "member";
  const api = await serverApi();

  const { data: campaign } = await api.GET("/api/v1/campaigns/{idOrCode}", { params: { path: { idOrCode: id } } });
  if (!campaign?.id) notFound();

  const [description, usage, conflicts, activity, sales] = await Promise.all([
    api.GET("/api/v1/campaigns/{idOrCode}/description", { params: { path: { idOrCode: campaign.id } } }),
    api.GET("/api/v1/ledger/usage/campaigns/{campaignId}", { params: { path: { campaignId: campaign.id } } }),
    api.POST("/api/v1/campaigns/conflicts", { body: campaign }),
    api.GET("/api/v1/audit", { params: { query: { entityType: "campaign", entityId: campaign.id, pageSize: 10 } } }),
    api.GET("/api/v1/ledger/transactions", { params: { query: { campaign: campaign.code, pageSize: 5 } } }),
  ]);
  const d = description.data!;
  const u = usage.data;
  const now = new Date();
  const conflictList = conflicts.data ?? [];

  return (
    <>
      <Link href="/campaigns" className="mb-4 inline-flex items-center gap-1 text-sm text-muted hover:text-foreground">
        <ArrowLeft className="size-4" aria-hidden /> Campaigns
      </Link>

      <div className="mb-6 flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
        <div>
          <div className="flex flex-wrap items-center gap-3">
            <h1 className="text-2xl font-semibold tracking-tight">{campaign.name}</h1>
            <StatusBadge status={campaign.status} />
          </div>
          <p className="mt-1 font-mono text-sm text-muted">
            {campaign.code} · version {formatInteger(campaign.version)}
            {campaign.updatedAt ? ` · updated ${formatRelative(campaign.updatedAt, now)}` : ""}
          </p>
          <p className="mt-3 max-w-3xl text-base">{d.summary}</p>
          {campaign.description ? <p className="mt-1 max-w-3xl text-sm text-muted">{campaign.description}</p> : null}
        </div>
        {canManage && campaign.status !== "archived" ? (
          <LifecycleActions id={campaign.id} code={campaign.code} status={campaign.status ?? "draft"} conflicts={conflictList} />
        ) : null}
      </div>

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <Kpi label="Redemptions" value={formatInteger(u?.redemptions)} hint={u?.maxRedemptions != null ? `of ${formatInteger(u.maxRedemptions)} allowed` : "No total limit"} />
        <Kpi label="Discount given" value={formatAmount(u?.discountTotal)} hint="Lifetime, reversals excluded" />
        <Kpi
          label="Budget remaining"
          value={u?.budgetRemaining != null ? formatAmount(u.budgetRemaining) : "—"}
          hint={u?.budget != null ? `of ${formatAmount(u.budget)} (${formatPercent(toNumber(u.discountTotal) / Math.max(1, toNumber(u.budget)))} used)` : "No budget set"}
        />
        <Kpi label="Reversed" value={formatInteger(u?.reversedRedemptions)} hint={u?.lastRedeemedAt ? `Last used ${formatRelative(u.lastRedeemedAt, now)}` : "Never used yet"} />
      </div>

      <div className="mt-6 grid gap-6 xl:grid-cols-3">
        <div className="space-y-6 xl:col-span-2">
          <Card>
            <CardHeader title="What it does" description="Generated from the definition by the engine" />
            <dl className="divide-y divide-border text-sm">
              <Row label="Reward" lines={[d.reward]} />
              <Row label="Conditions" lines={d.conditions.length ? d.conditions.map(capitalize) : ["None — every cart with qualifying items"]} />
              <Row label="Products" lines={d.products} />
              <Row label="Who and where" lines={d.audience} />
              <Row label="When" lines={d.schedule} />
              <Row label="Limits" lines={d.limits} />
              <Row label="Combining" lines={[d.combination]} />
            </dl>
          </Card>

          <Card>
            <CardHeader
              title="Conflicts"
              description="Deterministic analysis against live campaigns"
              action={conflictList.length === 0 ? <Badge tone="success">None</Badge> : <Badge tone="warning">{conflictList.length}</Badge>}
            />
            {conflictList.length === 0 ? (
              <p className="flex items-center gap-2 px-5 py-4 text-sm text-muted">
                <ShieldCheck className="size-4 text-success" aria-hidden /> No overlaps with live campaigns.
              </p>
            ) : (
              <ConflictList conflicts={conflictList} />
            )}
          </Card>
        </div>

        <div className="space-y-6">
          <Card>
            <CardHeader title="Recent sales" description="Transactions that used this campaign" />
            {(sales.data?.items ?? []).length === 0 ? (
              <p className="px-5 py-4 text-sm text-muted">No sales yet.</p>
            ) : (
              <ul className="divide-y divide-border text-sm">
                {sales.data!.items.map((t) => (
                  <li key={t.transactionId} className="flex items-center justify-between gap-3 px-5 py-2.5">
                    <span className="min-w-0">
                      <span className="block truncate font-mono text-xs">{t.transactionId}</span>
                      <span className="text-xs text-muted">
                        {[t.channel, t.storeId].filter(Boolean).join(" · ") || "—"} · {formatRelative(t.createdAt, now)}
                      </span>
                    </span>
                    <span className="shrink-0 text-right tabular-nums">
                      {formatAmount(t.totalDiscount)}
                      {t.status === "reversed" ? <span className="block text-xs text-muted">reversed</span> : null}
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </Card>

          <Card>
            <CardHeader title="Activity" />
            {(activity.data?.items ?? []).length === 0 ? (
              <p className="px-5 py-4 text-sm text-muted">No recorded changes.</p>
            ) : (
              <ol className="divide-y divide-border text-sm">
                {activity.data!.items.map((e) => (
                  <li key={e.id} className="px-5 py-2.5">
                    <p>{e.action.replace("campaign.", "").replace(/^./, (c) => c.toUpperCase())}</p>
                    <p className="text-xs text-muted">
                      {e.actorName} · <time dateTime={e.createdAt}>{formatRelative(e.createdAt, now)}</time>
                    </p>
                  </li>
                ))}
              </ol>
            )}
          </Card>

          <Card>
            <CardHeader title="Details" />
            <dl className="space-y-2 px-5 py-4 text-sm">
              <Detail label="Created" value={campaign.createdAt ? formatDate(campaign.createdAt) : "—"} />
              <Detail label="Currency" value={campaign.currency ?? "Any"} />
              <Detail label="Tags" value={campaign.tags?.length ? campaign.tags.join(", ") : "—"} />
              {Object.entries(campaign.metadata ?? {}).map(([k, v]) => (
                <Detail key={k} label={k} value={v} />
              ))}
              {campaign.displayMessage ? <Detail label="Receipt text" value={campaign.displayMessage} /> : null}
            </dl>
          </Card>
        </div>
      </div>
    </>
  );
}

function Row({ label, lines }: { label: string; lines: string[] }) {
  return (
    <div className="grid gap-1 px-5 py-3 sm:grid-cols-[10rem_1fr]">
      <dt className="text-muted">{label}</dt>
      <dd className="space-y-0.5">
        {lines.map((line) => (
          <p key={line}>{line}</p>
        ))}
      </dd>
    </div>
  );
}

function Detail({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between gap-4">
      <dt className="text-muted">{label}</dt>
      <dd className="text-right">{value}</dd>
    </div>
  );
}

function capitalize(text: string): string {
  return text.charAt(0).toUpperCase() + text.slice(1);
}

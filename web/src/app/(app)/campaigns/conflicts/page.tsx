import type { Metadata } from "next";
import Link from "next/link";
import { ArrowLeftRight, ShieldCheck } from "lucide-react";
import { conflictKindLabel, SeverityBadge, StatusBadge } from "@/components/campaigns/badges";
import { Card, PageHeader } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import { serverApi } from "@/lib/api/server";
import { audienceLine, groupBySeverity, parseSeverity, severities, type ConflictReportItem } from "@/lib/conflicts";
import { formatInteger, formatPercent, toNumber } from "@/lib/format";
import { cn } from "@/lib/utils";

export const metadata: Metadata = { title: "Conflicts" };

export default async function ConflictsPage({ searchParams }: PageProps<"/campaigns/conflicts">) {
  const only = parseSeverity((await searchParams).severity);
  const api = await serverApi();
  const { data: report, response } = await api.GET("/api/v1/campaigns/conflicts/report");
  if (!report) throw new Error(`The conflict analysis is unavailable (HTTP ${response.status}).`);

  const counts = { error: toNumber(report.errors), warning: toNumber(report.warnings), info: toNumber(report.infos) };
  const groups = groupBySeverity(report.items, only);

  return (
    <>
      <PageHeader
        title="Conflicts"
        description={`Overlaps among the ${formatInteger(report.liveCampaigns)} live (active or paused) campaigns, found by the engine's deterministic analyzer.`}
      />

      <nav aria-label="Filter by severity" className="mb-6 grid gap-3 sm:grid-cols-4">
        <FilterTile href="/campaigns/conflicts" active={!only} label="All conflicts" value={report.items.length} />
        {severities.map((s) => (
          <FilterTile key={s.key} href={`/campaigns/conflicts?severity=${s.key}`} active={only === s.key} label={s.title} value={counts[s.key]} tone={s.key} />
        ))}
      </nav>

      {report.items.length === 0 ? (
        <EmptyState icon={ShieldCheck} title="No conflicts between live campaigns">
          Every live campaign can run next to the others as defined. New conflicts are reported when campaigns are activated or changed.
        </EmptyState>
      ) : groups.length === 0 ? (
        <EmptyState icon={ShieldCheck} title="Nothing at this severity">
          <Link href="/campaigns/conflicts" className="font-medium text-brand hover:underline">
            Show all conflicts
          </Link>
        </EmptyState>
      ) : (
        <div className="space-y-8">
          {groups.map((group) => (
            <section key={group.key} aria-labelledby={`group-${group.key}`}>
              <h2 id={`group-${group.key}`} className="text-base font-semibold">
                {group.title} <span className="font-normal text-muted">({group.items.length})</span>
              </h2>
              <p className="mb-3 text-sm text-muted">{group.description}</p>
              <ul className="space-y-4">
                {group.items.map((item, i) => (
                  <li key={`${item.campaign.id}-${item.other.id}-${item.conflict.kind}-${i}`}>
                    <ConflictCard item={item} />
                  </li>
                ))}
              </ul>
            </section>
          ))}
        </div>
      )}
    </>
  );
}

function FilterTile({ href, active, label, value, tone }: { href: string; active: boolean; label: string; value: number; tone?: string }) {
  return (
    <Link
      href={href}
      aria-current={active ? "page" : undefined}
      className={cn(
        "rounded-lg border bg-surface px-4 py-3 transition-colors hover:border-brand",
        active ? "border-brand ring-4 ring-ring" : "border-border",
      )}
    >
      <p className="text-sm text-muted">{label}</p>
      <p className={cn("text-2xl font-semibold tabular-nums", tone === "error" && value > 0 && "text-danger", tone === "warning" && value > 0 && "text-warning")}>{value}</p>
    </Link>
  );
}

function ConflictCard({ item }: { item: ConflictReportItem }) {
  const { conflict } = item;
  return (
    <Card>
      <div className="flex flex-wrap items-center gap-2 border-b border-border px-5 py-3">
        <SeverityBadge value={conflict.severity} />
        <h3 className="text-sm font-semibold">{conflictKindLabel(conflict.kind)}</h3>
        <span className="text-xs text-muted">{conflict.certainty === "certain" ? "certain overlap" : "possible overlap"}</span>
        {conflict.combinedRateEstimate != null ? (
          <span className="ml-auto text-xs text-muted">Combined up to about {formatPercent(conflict.combinedRateEstimate)}</span>
        ) : null}
      </div>
      <div className="grid gap-px bg-border md:grid-cols-[1fr_auto_1fr]">
        <Side side={item.campaign} />
        <div className="flex items-center justify-center bg-surface px-3 py-2 text-xs text-muted md:flex-col">
          <ArrowLeftRight className="size-4" aria-hidden />
          <span className="ml-2 md:ml-0 md:mt-1">overlaps with</span>
        </div>
        <Side side={item.other} />
      </div>
      <p className="border-t border-border px-5 py-3 text-sm">
        <span className="font-medium">Reason: </span>
        {conflict.message}
      </p>
    </Card>
  );
}

function Side({ side }: { side: ConflictReportItem["campaign"] }) {
  return (
    <div className="bg-surface px-5 py-4">
      <div className="flex flex-wrap items-center gap-2">
        <Link href={`/campaigns/${side.id}`} className="font-medium hover:text-brand hover:underline">
          {side.name}
        </Link>
        <StatusBadge status={side.status} />
      </div>
      <p className="font-mono text-xs text-muted">{side.code}</p>
      <p className="mt-2 text-sm">{side.summary}</p>
      <dl className="mt-3 grid grid-cols-[6rem_1fr] gap-x-3 gap-y-1 text-sm">
        <dt className="text-muted">Channels</dt>
        <dd>{audienceLine(side.audience, "channels")}</dd>
        <dt className="text-muted">Stores</dt>
        <dd>{audienceLine(side.audience, "stores")}</dd>
        <dt className="text-muted">When</dt>
        <dd>{side.schedule.filter((l) => !l.startsWith("Time zone")).join(" · ")}</dd>
        <dt className="text-muted">Combining</dt>
        <dd>
          {side.stacking === "exclusive" ? "Exclusive" : "Stackable"}, priority {formatInteger(side.priority)}
        </dd>
      </dl>
    </div>
  );
}

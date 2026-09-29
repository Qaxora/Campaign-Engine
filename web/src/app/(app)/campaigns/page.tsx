import type { Metadata } from "next";
import Link from "next/link";
import { ChevronDown, ChevronUp, Megaphone, Plus, SearchX } from "lucide-react";
import { StatusBadge } from "@/components/campaigns/badges";
import { buttonClass } from "@/components/ui/button";
import { Card, PageHeader } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import { Input, Label } from "@/components/ui/form";
import { getSession, serverApi } from "@/lib/api/server";
import { filtersHref, hasFilters, PAGE_SIZE, parseFilters, sorts, statuses, toApiQuery, type CampaignFilters } from "@/lib/campaign-filters";
import { formatDate, formatInteger, formatRelative, toNumber } from "@/lib/format";

export const metadata: Metadata = { title: "Campaigns" };

export default async function CampaignsPage({ searchParams }: PageProps<"/campaigns">) {
  const filters = parseFilters(await searchParams);
  const session = (await getSession())!;
  const canManage = session.current!.role !== "member";
  const api = await serverApi();
  const { data, response } = await api.GET("/api/v1/campaigns", { params: { query: toApiQuery(filters) } });
  if (!data) throw new Error(`Campaigns are unavailable (HTTP ${response.status}).`);

  const total = toNumber(data.totalCount);
  const pages = Math.max(1, Math.ceil(total / PAGE_SIZE));
  const now = new Date();

  return (
    <>
      <PageHeader
        title="Campaigns"
        description="Every campaign of the organization. Status changes are made on the campaign page, after reviewing conflicts."
        actions={
          canManage ? (
            <Link href="/campaigns/new" className={buttonClass()}>
              <Plus className="size-4" aria-hidden /> Create campaign
            </Link>
          ) : null
        }
      />

      <form method="get" className="mb-4 grid gap-3 rounded-lg border border-border bg-surface p-4 sm:grid-cols-2 lg:grid-cols-6">
        <div className="flex flex-col gap-1.5 lg:col-span-2">
          <Label htmlFor="search">Search</Label>
          <Input id="search" name="search" placeholder="Code or name" defaultValue={filters.search} />
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="status">Status</Label>
          <select id="status" name="status" defaultValue={filters.status} className="h-10 rounded-md border border-border bg-surface px-2 text-sm">
            <option value="">Any status</option>
            {statuses.map((s) => (
              <option key={s} value={s}>
                {s[0]!.toUpperCase() + s.slice(1)}
              </option>
            ))}
          </select>
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="channel">Channel</Label>
          <Input id="channel" name="channel" placeholder="store, web…" defaultValue={filters.channel} />
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="from">Running from</Label>
          <Input id="from" name="from" type="date" defaultValue={filters.from} />
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="to">Running to</Label>
          <Input id="to" name="to" type="date" defaultValue={filters.to} />
        </div>
        <input type="hidden" name="sort" value={filters.sort} />
        <input type="hidden" name="order" value={filters.order} />
        <div className="flex items-end gap-2 sm:col-span-2 lg:col-span-6">
          <button type="submit" className={buttonClass("secondary", "sm")}>
            Apply filters
          </button>
          {hasFilters(filters) ? (
            <Link href="/campaigns" className={buttonClass("ghost", "sm")}>
              Clear
            </Link>
          ) : null}
          <span className="ml-auto text-sm text-muted">
            {formatInteger(total)} {total === 1 ? "campaign" : "campaigns"}
          </span>
        </div>
      </form>

      {data.items.length === 0 ? (
        hasFilters(filters) ? (
          <EmptyState icon={SearchX} title="No campaigns match these filters">
            <Link href="/campaigns" className="font-medium text-brand hover:underline">
              Clear the filters
            </Link>
          </EmptyState>
        ) : (
          <EmptyState icon={Megaphone} title="No campaigns yet">
            {canManage ? (
              <Link href="/campaigns/new" className="font-medium text-brand hover:underline">
                Create your first campaign
              </Link>
            ) : (
              "An owner or admin creates campaigns."
            )}
          </EmptyState>
        )
      ) : (
        <Card className="overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full min-w-[720px] text-sm">
              <thead className="border-b border-border bg-surface-muted/60 text-left text-xs text-muted">
                <tr>
                  <SortHeader filters={filters} sort="name" label="Campaign" />
                  <th className="px-4 py-2.5 font-medium">Status</th>
                  <SortHeader filters={filters} sort="priority" label="Priority" align="right" />
                  <SortHeader filters={filters} sort="startsAt" label="Schedule" />
                  <th className="px-4 py-2.5 font-medium">Channels</th>
                  <SortHeader filters={filters} sort="updated" label="Updated" align="right" />
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {data.items.map((c) => (
                  <tr key={c.id} className="hover:bg-surface-muted/60">
                    <td className="px-4 py-3">
                      <Link href={`/campaigns/${c.id}`} className="font-medium hover:text-brand hover:underline">
                        {c.name}
                      </Link>
                      <p className="font-mono text-xs text-muted">{c.code}</p>
                    </td>
                    <td className="px-4 py-3">
                      <StatusBadge status={c.status} />
                    </td>
                    <td className="px-4 py-3 text-right tabular-nums">{formatInteger(c.priority)}</td>
                    <td className="px-4 py-3 text-muted">
                      {c.schedule?.startsAt || c.schedule?.endsAt
                        ? `${c.schedule.startsAt ? formatDate(c.schedule.startsAt) : "…"} – ${c.schedule.endsAt ? formatDate(c.schedule.endsAt) : "…"}`
                        : "Always"}
                    </td>
                    <td className="px-4 py-3 text-muted">{c.channels?.length ? c.channels.join(", ") : "All"}</td>
                    <td className="px-4 py-3 text-right text-muted">{c.updatedAt ? formatRelative(c.updatedAt, now) : ""}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {pages > 1 ? (
            <nav aria-label="Pagination" className="flex items-center justify-between border-t border-border px-4 py-3 text-sm">
              <span className="text-muted">
                Page {filters.page} of {pages}
              </span>
              <div className="flex gap-2">
                {filters.page > 1 ? (
                  <Link href={filtersHref(filters, { page: filters.page - 1 })} className={buttonClass("secondary", "sm")}>
                    Previous
                  </Link>
                ) : null}
                {filters.page < pages ? (
                  <Link href={filtersHref(filters, { page: filters.page + 1 })} className={buttonClass("secondary", "sm")}>
                    Next
                  </Link>
                ) : null}
              </div>
            </nav>
          ) : null}
        </Card>
      )}
    </>
  );
}

function SortHeader({ filters, sort, label, align }: { filters: CampaignFilters; sort: string; label: string; align?: "right" }) {
  const active = filters.sort === sort;
  const nextOrder = active && filters.order === "desc" ? "asc" : "desc";
  const Icon = filters.order === "asc" ? ChevronUp : ChevronDown;
  return (
    <th className={`px-4 py-2.5 font-medium ${align === "right" ? "text-right" : ""}`} aria-sort={active ? (filters.order === "asc" ? "ascending" : "descending") : undefined}>
      <Link href={filtersHref(filters, { sort, order: nextOrder })} className="inline-flex items-center gap-1 hover:text-foreground">
        {label}
        {active ? <Icon className="size-3.5" aria-hidden /> : null}
        <span className="sr-only">Sort by {sorts.find((s) => s.key === sort)?.label}</span>
      </Link>
    </th>
  );
}

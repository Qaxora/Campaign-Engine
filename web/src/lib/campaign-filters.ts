/** URL ⇄ API query for the campaign list. Filtering itself happens in the API. */
export const statuses = ["active", "draft", "paused", "archived"] as const;
export const sorts = [
  { key: "updated", label: "Last updated" },
  { key: "priority", label: "Priority" },
  { key: "name", label: "Name" },
  { key: "startsAt", label: "Start date" },
  { key: "endsAt", label: "End date" },
] as const;

export const PAGE_SIZE = 20;

export interface CampaignFilters {
  search: string;
  status: string;
  channel: string;
  from: string; // yyyy-mm-dd
  to: string; // yyyy-mm-dd, inclusive
  sort: string;
  order: "asc" | "desc";
  page: number;
}

type Params = Record<string, string | string[] | undefined>;

function one(params: Params, key: string): string {
  const value = params[key];
  return (Array.isArray(value) ? value[0] : value)?.trim() ?? "";
}

const isDate = (value: string) => /^\d{4}-\d{2}-\d{2}$/.test(value) && !Number.isNaN(Date.parse(value));

export function parseFilters(params: Params): CampaignFilters {
  const status = one(params, "status");
  const sort = one(params, "sort");
  const page = Number.parseInt(one(params, "page"), 10);
  return {
    search: one(params, "search"),
    status: (statuses as readonly string[]).includes(status) ? status : "",
    channel: one(params, "channel"),
    from: isDate(one(params, "from")) ? one(params, "from") : "",
    to: isDate(one(params, "to")) ? one(params, "to") : "",
    sort: sorts.some((s) => s.key === sort) ? sort : "updated",
    order: one(params, "order") === "asc" ? "asc" : "desc",
    page: Number.isFinite(page) && page > 0 ? page : 1,
  };
}

/** The API query: dates become a UTC range, with the "to" day included. */
export function toApiQuery(f: CampaignFilters) {
  const next = (day: string) => new Date(Date.parse(`${day}T00:00:00Z`) + 86_400_000).toISOString();
  return {
    search: f.search || undefined,
    status: f.status || undefined,
    channel: f.channel || undefined,
    activeFrom: f.from ? `${f.from}T00:00:00.000Z` : undefined,
    activeTo: f.to ? next(f.to) : undefined,
    sort: f.sort,
    order: f.order,
    page: f.page,
    pageSize: PAGE_SIZE,
  };
}

/** A link to the same list with some filters changed; changing anything but the page resets paging. */
export function filtersHref(f: CampaignFilters, change: Partial<CampaignFilters>): string {
  const merged = { ...f, ...change, page: change.page ?? 1 };
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(merged)) {
    const isDefault = (key === "sort" && value === "updated") || (key === "order" && value === "desc") || (key === "page" && value === 1);
    if (value !== "" && !isDefault) params.set(key, String(value));
  }

  const query = params.toString();
  return query ? `/campaigns?${query}` : "/campaigns";
}

export function hasFilters(f: CampaignFilters): boolean {
  return Boolean(f.search || f.status || f.channel || f.from || f.to);
}

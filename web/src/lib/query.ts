/** Small helpers for list pages whose filters live in the URL. */
export type SearchParams = Record<string, string | string[] | undefined>;

export function param(params: SearchParams, key: string): string {
  const value = params[key];
  return ((Array.isArray(value) ? value[0] : value) ?? "").trim();
}

export function pageParam(params: SearchParams): number {
  const page = Number.parseInt(param(params, "page"), 10);
  return Number.isFinite(page) && page > 0 ? page : 1;
}

export const isDay = (value: string) => /^\d{4}-\d{2}-\d{2}$/.test(value) && !Number.isNaN(Date.parse(value));

/** Inclusive calendar days → a UTC [from, to) range for the API. */
export function dayRange(from: string, to: string): { from?: string; to?: string } {
  return {
    from: isDay(from) ? `${from}T00:00:00.000Z` : undefined,
    to: isDay(to) ? new Date(Date.parse(`${to}T00:00:00Z`) + 86_400_000).toISOString() : undefined,
  };
}

/** `base?…` with empty values and page 1 left out. */
export function hrefWith(base: string, values: Record<string, string | number | undefined>): string {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(values)) {
    if (value !== undefined && value !== "" && !(key === "page" && value === 1)) params.set(key, String(value));
  }

  const query = params.toString();
  return query ? `${base}?${query}` : base;
}

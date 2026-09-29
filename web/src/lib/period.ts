/** Reporting periods offered in the UI. The API accepts any `[from, to)` range up to 366 days. */
export const periods = [
  { key: "7d", label: "7 days", days: 7 },
  { key: "30d", label: "30 days", days: 30 },
  { key: "90d", label: "90 days", days: 90 },
] as const;

export type PeriodKey = (typeof periods)[number]["key"];

export interface ResolvedPeriod {
  key: PeriodKey;
  label: string;
  from: string;
  to: string;
}

/** Unknown or missing values fall back to 30 days. `to` is the start of the next UTC hour so pages cache-bust hourly, not per request. */
export function resolvePeriod(value: string | string[] | undefined, now: Date = new Date()): ResolvedPeriod {
  const period = periods.find((p) => p.key === value) ?? periods[1];
  const to = new Date(now);
  to.setUTCMinutes(0, 0, 0);
  to.setUTCHours(to.getUTCHours() + 1);
  const from = new Date(to.getTime() - period.days * 86_400_000);
  return { key: period.key, label: period.label, from: from.toISOString(), to: to.toISOString() };
}

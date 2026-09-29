/**
 * Display formatting only. Every number here was computed by the API; the UI never re-derives
 * business values. A fixed locale keeps server and client renders identical.
 */
const LOCALE = "en-US";

/** openapi-typescript types .NET integers and decimals as `number | string`. */
export function toNumber(value: number | string | null | undefined): number {
  const n = typeof value === "string" ? Number(value) : (value ?? 0);
  return Number.isFinite(n) ? n : 0;
}

export function formatInteger(value: number | string | null | undefined): string {
  return new Intl.NumberFormat(LOCALE, { maximumFractionDigits: 0 }).format(toNumber(value));
}

/** Amounts in the transactions' currency; the ledger does not convert between currencies. */
export function formatAmount(value: number | string | null | undefined): string {
  return new Intl.NumberFormat(LOCALE, { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(toNumber(value));
}

/** 12400 → "12.4K" for axis ticks. */
export function formatCompact(value: number): string {
  return new Intl.NumberFormat(LOCALE, { notation: "compact", maximumFractionDigits: 1 }).format(value);
}

export function formatPercent(fraction: number | string | null | undefined, digits = 0): string {
  return new Intl.NumberFormat(LOCALE, { style: "percent", maximumFractionDigits: digits }).format(toNumber(fraction));
}

export interface Change {
  direction: "up" | "down" | "flat" | "new";
  text: string;
}

/** Change against the previous period, for KPI tiles. */
export function periodChange(current: number | string, previous: number | string): Change {
  const now = toNumber(current);
  const before = toNumber(previous);
  if (before === 0) {
    return now === 0 ? { direction: "flat", text: "No change" } : { direction: "new", text: "New this period" };
  }

  const ratio = (now - before) / before;
  if (Math.abs(ratio) < 0.005) return { direction: "flat", text: "No change" };
  return { direction: ratio > 0 ? "up" : "down", text: `${ratio > 0 ? "+" : "−"}${formatPercent(Math.abs(ratio), 1)}` };
}

/** "3 min ago", "2 h ago", "5 d ago", then a date. */
export function formatRelative(iso: string, now: Date = new Date()): string {
  const seconds = Math.max(0, Math.round((now.getTime() - new Date(iso).getTime()) / 1000));
  if (seconds < 60) return "just now";
  if (seconds < 3600) return `${Math.floor(seconds / 60)} min ago`;
  if (seconds < 86400) return `${Math.floor(seconds / 3600)} h ago`;
  if (seconds < 7 * 86400) return `${Math.floor(seconds / 86400)} d ago`;
  return formatDate(iso);
}

/** "2026-03-10" or an ISO timestamp → "Mar 10, 2026" (dates without time are read as calendar days). */
export function formatDate(value: string, withYear = true): string {
  const date = /^\d{4}-\d{2}-\d{2}$/.test(value) ? new Date(`${value}T00:00:00Z`) : new Date(value);
  return new Intl.DateTimeFormat(LOCALE, { month: "short", day: "numeric", year: withYear ? "numeric" : undefined, timeZone: /^\d{4}-\d{2}-\d{2}$/.test(value) ? "UTC" : undefined }).format(date);
}

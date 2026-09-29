import type { components } from "@/lib/api/schema";

export type ConflictReportItem = components["schemas"]["ConflictReportItem"];
export type Severity = components["schemas"]["ConflictSeverity"];

export const severities: { key: Severity; title: string; description: string }[] = [
  { key: "error", title: "Blocking", description: "Activation is refused until these are fixed (or explicitly forced)." },
  { key: "warning", title: "Warnings", description: "Likely to give more discount than intended." },
  { key: "info", title: "For your information", description: "Overlaps the engine resolves by priority, stacking or exclusivity." },
];

export function parseSeverity(value: string | string[] | undefined): Severity | undefined {
  return severities.find((s) => s.key === value)?.key;
}

/** Groups the API's (already ordered) items by severity, keeping only non-empty groups. */
export function groupBySeverity(items: ConflictReportItem[], only?: Severity) {
  return severities
    .filter((s) => !only || s.key === only)
    .map((s) => ({ ...s, items: items.filter((i) => i.conflict.severity === s.key) }))
    .filter((g) => g.items.length > 0);
}

/** Audience lines are "Channels: …", "Stores: …", "All stores" …; pick the ones the brief asks to compare. */
export function audienceLine(audience: string[], kind: "channels" | "stores"): string {
  const line = audience.find((l) => (kind === "channels" ? l.startsWith("Channels:") || l === "All channels" : l.startsWith("Stores:") || l === "All stores"));
  return line?.replace(/^(Channels|Stores): /, "") ?? "—";
}

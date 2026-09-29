import Link from "next/link";
import type { components } from "@/lib/api/schema";
import { formatPercent } from "@/lib/format";
import { conflictKindLabel, SeverityBadge } from "./badges";

type Conflict = components["schemas"]["CampaignConflict"];

/** Conflicts exactly as the deterministic analyzer reported them. */
export function ConflictList({ conflicts }: { conflicts: Conflict[] }) {
  return (
    <ul className="divide-y divide-border">
      {conflicts.map((c, i) => (
        <li key={`${c.otherId}-${c.kind}-${i}`} className="flex flex-col gap-1.5 px-5 py-3">
          <div className="flex flex-wrap items-center gap-2">
            <SeverityBadge value={c.severity} />
            <span className="text-sm font-medium">{conflictKindLabel(c.kind)}</span>
            <span className="text-sm text-muted">with</span>
            <Link href={`/campaigns/${c.otherId}`} className="text-sm font-medium text-brand hover:underline">
              {c.otherCode}
            </Link>
            {c.certainty === "possible" ? <span className="text-xs text-muted">(possible overlap)</span> : null}
          </div>
          <p className="text-sm text-muted">
            {c.message}
            {c.combinedRateEstimate != null ? ` Combined up to about ${formatPercent(c.combinedRateEstimate)}.` : null}
          </p>
        </li>
      ))}
    </ul>
  );
}

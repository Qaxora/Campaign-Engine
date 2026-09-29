import Link from "next/link";
import { periods, type PeriodKey } from "@/lib/period";
import { cn } from "@/lib/utils";

/** Plain links, so the period is in the URL and survives reloads and sharing. */
export function PeriodPicker({ current }: { current: PeriodKey }) {
  return (
    <nav aria-label="Period" className="inline-flex rounded-md border border-border bg-surface p-0.5 text-sm">
      {periods.map((p) => (
        <Link
          key={p.key}
          href={`?period=${p.key}`}
          aria-current={p.key === current ? "page" : undefined}
          className={cn("rounded px-3 py-1", p.key === current ? "bg-surface-muted font-medium text-foreground" : "text-muted hover:text-foreground")}
        >
          {p.label}
        </Link>
      ))}
    </nav>
  );
}

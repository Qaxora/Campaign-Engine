import { ArrowDownRight, ArrowUpRight, Minus, Sparkles } from "lucide-react";
import type { Change } from "@/lib/format";
import { cn } from "@/lib/utils";

/**
 * A headline number with its change against the previous period. The arrow and sign carry the
 * direction, so it never depends on colour alone; direction is not judged good or bad.
 */
export function Kpi({ label, value, hint, change }: { label: string; value: string; hint?: string; change?: Change }) {
  const Icon = change?.direction === "up" ? ArrowUpRight : change?.direction === "down" ? ArrowDownRight : change?.direction === "new" ? Sparkles : Minus;
  return (
    <div className="rounded-lg border border-border bg-surface px-5 py-4">
      <p className="text-sm text-muted">{label}</p>
      <p className="mt-1 text-2xl font-semibold tabular-nums tracking-tight">{value}</p>
      <div className="mt-1 flex min-h-5 items-center gap-1 text-xs text-muted">
        {change ? (
          <>
            <Icon className={cn("size-3.5", change.direction === "up" || change.direction === "new" ? "text-brand" : "")} aria-hidden />
            <span className="font-medium text-foreground">{change.text}</span>
            <span>vs previous period</span>
          </>
        ) : (
          hint
        )}
      </div>
    </div>
  );
}

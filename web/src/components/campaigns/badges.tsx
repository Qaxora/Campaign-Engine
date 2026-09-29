import { AlertOctagon, AlertTriangle, Info } from "lucide-react";
import type { components } from "@/lib/api/schema";
import { Badge } from "@/components/ui/card";

type Status = components["schemas"]["CampaignStatus"];
type Severity = components["schemas"]["ConflictSeverity"];

const statusTone = { active: "success", draft: "neutral", paused: "warning", archived: "neutral" } as const;
const statusLabel = { active: "Active", draft: "Draft", paused: "Paused", archived: "Archived" } as const;

export function StatusBadge({ status }: { status: Status | undefined }) {
  const key = (status ?? "draft") as keyof typeof statusTone;
  return <Badge tone={statusTone[key]}>{statusLabel[key]}</Badge>;
}

const severity = {
  error: { tone: "danger", label: "Blocks activation", Icon: AlertOctagon },
  warning: { tone: "warning", label: "Warning", Icon: AlertTriangle },
  info: { tone: "neutral", label: "Info", Icon: Info },
} as const;

/** Severity with icon and words, never colour alone. */
export function SeverityBadge({ value }: { value: Severity }) {
  const s = severity[value as keyof typeof severity] ?? severity.info;
  return (
    <Badge tone={s.tone}>
      <s.Icon className="mr-1 size-3" aria-hidden />
      {s.label}
    </Badge>
  );
}

/** "stackedDiscount" → "Stacked discount" */
export function conflictKindLabel(kind: string): string {
  const spaced = kind.replace(/([A-Z])/g, " $1").toLowerCase();
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}

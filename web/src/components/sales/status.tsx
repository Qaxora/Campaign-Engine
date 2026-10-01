import { Badge } from "@/components/ui/card";

export function SaleStatus({ status, offline }: { status: string; offline?: boolean }) {
  return (
    <span className="inline-flex flex-wrap gap-1">
      {status === "reversed" ? <Badge tone="warning">Reversed</Badge> : <Badge tone="success">Confirmed</Badge>}
      {offline ? <Badge>Offline</Badge> : null}
    </span>
  );
}

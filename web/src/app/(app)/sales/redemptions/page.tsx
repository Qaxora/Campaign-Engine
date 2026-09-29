import type { Metadata } from "next";
import { SectionPending } from "@/components/shell/section-pending";

export const metadata: Metadata = { title: "Redemptions" };

export default function Page() {
  return <SectionPending title="Redemptions" description="Campaign discounts given in completed sales." issue={17} api="GET /api/v1/ledger/transactions" />;
}

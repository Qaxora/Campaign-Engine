import type { Metadata } from "next";
import { SectionPending } from "@/components/shell/section-pending";

export const metadata: Metadata = { title: "Usage" };

export default function Page() {
  return <SectionPending title="Usage" description="Consumption of limits and budgets per campaign and customer." issue={17} api="GET /api/v1/ledger/usage/campaigns" />;
}

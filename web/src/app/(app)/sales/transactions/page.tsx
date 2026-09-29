import type { Metadata } from "next";
import { SectionPending } from "@/components/shell/section-pending";

export const metadata: Metadata = { title: "Transactions" };

export default function Page() {
  return <SectionPending title="Transactions" description="Every sale recorded through the integration API, including reversals." issue={17} api="GET /api/v1/ledger/transactions" />;
}

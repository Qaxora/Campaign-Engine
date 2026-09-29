import type { Metadata } from "next";
import { SectionPending } from "@/components/shell/section-pending";

export const metadata: Metadata = { title: "Conflicts" };

export default function Page() {
  return <SectionPending title="Conflicts" description="Campaigns that overlap in products, channels, stores and time." issue={15} api="GET /api/v1/campaigns/conflicts" />;
}

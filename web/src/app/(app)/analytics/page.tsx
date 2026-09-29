import type { Metadata } from "next";
import { SectionPending } from "@/components/shell/section-pending";

export const metadata: Metadata = { title: "Analytics" };

export default function Page() {
  return <SectionPending title="Analytics" description="Campaign performance, redemption trends and top campaigns." issue={18} api="GET /api/v1/analytics/overview" />;
}

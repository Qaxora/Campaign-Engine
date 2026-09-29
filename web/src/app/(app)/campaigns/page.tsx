import type { Metadata } from "next";
import { SectionPending } from "@/components/shell/section-pending";

export const metadata: Metadata = { title: "Campaigns" };

export default function Page() {
  return <SectionPending title="Campaigns" description="Every campaign of the organization with its status, schedule and priority." issue={13} api="GET /api/v1/campaigns" />;
}

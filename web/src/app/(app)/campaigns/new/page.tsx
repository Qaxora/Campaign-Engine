import type { Metadata } from "next";
import { SectionPending } from "@/components/shell/section-pending";

export const metadata: Metadata = { title: "Create campaign" };

export default function Page() {
  return <SectionPending title="Create campaign" description="Build a campaign visually: audience, conditions, reward, limits and stacking." issue={14} api="POST /api/v1/campaigns" />;
}

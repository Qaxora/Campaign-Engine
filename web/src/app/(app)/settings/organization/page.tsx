import type { Metadata } from "next";
import { SectionPending } from "@/components/shell/section-pending";

export const metadata: Metadata = { title: "Organization" };

export default function Page() {
  return <SectionPending title="Organization" description="Name and plan of your organization." issue={20} api="GET /api/v1/organization" />;
}

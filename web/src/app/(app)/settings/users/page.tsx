import type { Metadata } from "next";
import { SectionPending } from "@/components/shell/section-pending";

export const metadata: Metadata = { title: "Users" };

export default function Page() {
  return <SectionPending title="Users" description="Members of the organization and their roles." issue={20} api="GET /api/v1/members" />;
}

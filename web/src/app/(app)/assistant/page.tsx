import type { Metadata } from "next";
import { SectionPending } from "@/components/shell/section-pending";

export const metadata: Metadata = { title: "AI Assistant" };

export default function Page() {
  return <SectionPending title="AI Assistant" description="Describe a campaign in plain language and review the proposal before anything is saved." issue={23} api="AI endpoints (#21)" />;
}

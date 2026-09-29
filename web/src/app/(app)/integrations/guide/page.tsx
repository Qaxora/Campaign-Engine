import type { Metadata } from "next";
import { SectionPending } from "@/components/shell/section-pending";

export const metadata: Metadata = { title: "Integration guide" };

export default function Page() {
  return <SectionPending title="Integration guide" description="How to evaluate carts, record redemptions and reverse sales." issue={19} api="POST /api/v1/evaluate" />;
}

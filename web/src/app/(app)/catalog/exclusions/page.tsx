import type { Metadata } from "next";
import { SectionPending } from "@/components/shell/section-pending";

export const metadata: Metadata = { title: "Exclusions" };

export default function Page() {
  return <SectionPending title="Exclusions" description="Products that no campaign may discount." issue={16} api="GET /api/v1/product-lists" />;
}

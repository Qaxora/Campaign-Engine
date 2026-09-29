import type { Metadata } from "next";
import { SectionPending } from "@/components/shell/section-pending";

export const metadata: Metadata = { title: "Products" };

export default function Page() {
  return <SectionPending title="Products" description="The product catalog used by the builder and the AI assistant." issue={16} api="GET /api/v1/products" />;
}

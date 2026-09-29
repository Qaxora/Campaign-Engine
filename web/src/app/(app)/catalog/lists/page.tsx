import type { Metadata } from "next";
import { SectionPending } from "@/components/shell/section-pending";

export const metadata: Metadata = { title: "Product lists" };

export default function Page() {
  return <SectionPending title="Product lists" description="Named SKU lists that campaigns target or exclude." issue={16} api="GET /api/v1/product-lists" />;
}

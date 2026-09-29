import type { Metadata } from "next";
import { SectionPending } from "@/components/shell/section-pending";

export const metadata: Metadata = { title: "Stores" };

export default function Page() {
  return <SectionPending title="Stores" description="Stores whose codes carts send as storeId." issue={20} api="GET /api/v1/stores" />;
}

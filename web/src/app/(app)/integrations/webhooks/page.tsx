import type { Metadata } from "next";
import { SectionPending } from "@/components/shell/section-pending";

export const metadata: Metadata = { title: "Webhooks" };

export default function Page() {
  return <SectionPending title="Webhooks" description="Notify your systems when campaigns and product lists change." issue={19} api="GET /api/v1/webhooks" />;
}

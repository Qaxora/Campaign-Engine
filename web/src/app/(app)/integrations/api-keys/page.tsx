import type { Metadata } from "next";
import { SectionPending } from "@/components/shell/section-pending";

export const metadata: Metadata = { title: "API keys" };

export default function Page() {
  return <SectionPending title="API keys" description="Credentials for POS, e-commerce and ERP integrations." issue={19} api="GET /api/v1/api-keys" />;
}

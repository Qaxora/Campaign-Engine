import type { Metadata } from "next";
import { redirect } from "next/navigation";
import { CampaignBuilder } from "@/components/builder/campaign-builder";
import { PageHeader } from "@/components/ui/card";
import { getSession } from "@/lib/api/server";
import { loadBuilderCatalog } from "@/lib/builder/catalog";
import { emptyForm } from "@/lib/builder/model";

export const metadata: Metadata = { title: "Create campaign" };

export default async function NewCampaignPage() {
  const session = (await getSession())!;
  if (session.current!.role === "member") redirect("/campaigns");

  return (
    <>
      <PageHeader title="Create campaign" description="Build it section by section. The review on the right is the engine's own reading of your campaign." />
      <CampaignBuilder initial={emptyForm()} catalog={await loadBuilderCatalog()} />
    </>
  );
}

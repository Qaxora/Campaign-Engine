import type { Metadata } from "next";
import { notFound, redirect } from "next/navigation";
import { CampaignBuilder } from "@/components/builder/campaign-builder";
import { PageHeader } from "@/components/ui/card";
import { getSession, serverApi } from "@/lib/api/server";
import { loadBuilderCatalog } from "@/lib/builder/catalog";
import { fromDefinition } from "@/lib/builder/model";

export const metadata: Metadata = { title: "Edit campaign" };

export default async function EditCampaignPage({ params }: PageProps<"/campaigns/[id]/edit">) {
  const { id } = await params;
  const session = (await getSession())!;
  if (session.current!.role === "member") redirect(`/campaigns/${id}`);

  const api = await serverApi();
  const { data: campaign } = await api.GET("/api/v1/campaigns/{idOrCode}", { params: { path: { idOrCode: id } } });
  if (!campaign?.id) notFound();
  if (campaign.status === "archived") redirect(`/campaigns/${campaign.id}`);

  return (
    <>
      <PageHeader title={`Edit ${campaign.name}`} description={`${campaign.code} · ${campaign.status}. Saving keeps the status; the version you loaded protects against overwriting someone else's change.`} />
      <CampaignBuilder initial={fromDefinition(campaign)} catalog={await loadBuilderCatalog(campaign)} />
    </>
  );
}

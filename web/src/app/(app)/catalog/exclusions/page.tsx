import type { Metadata } from "next";
import { ListIndex } from "@/components/catalog/list-index";
import { PageHeader } from "@/components/ui/card";

export const metadata: Metadata = { title: "Exclusions" };

export default function ExclusionsPage() {
  return (
    <>
      <PageHeader
        title="Exclusions"
        description="Products on these lists are never discounted, whatever a campaign says — unless a campaign explicitly overrides global exclusions."
      />
      <ListIndex kind="globalExclusion" />
    </>
  );
}

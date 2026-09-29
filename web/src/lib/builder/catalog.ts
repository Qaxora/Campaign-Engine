import { serverApi } from "@/lib/api/server";
import type { BuilderCatalog } from "@/components/builder/campaign-builder";
import type { Campaign } from "./model";

/** Picker suggestions from the organization's catalog, product lists and stores. */
export async function loadBuilderCatalog(campaign?: Campaign): Promise<BuilderCatalog> {
  const api = await serverApi();
  const [facets, lists, stores, description] = await Promise.all([
    api.GET("/api/v1/products/facets"),
    api.GET("/api/v1/product-lists"),
    api.GET("/api/v1/stores"),
    campaign?.id ? api.GET("/api/v1/campaigns/{idOrCode}/description", { params: { path: { idOrCode: campaign.id } } }) : Promise.resolve(undefined),
  ]);

  // The engine describes each condition in order; kept-as-is conditions show that sentence.
  const advanced: Record<string, string> = {};
  (campaign?.conditions ?? []).forEach((condition, i) => {
    const line = description?.data?.conditions[i];
    if (line) advanced[JSON.stringify(condition)] = line.charAt(0).toUpperCase() + line.slice(1);
  });

  return {
    categories: (facets.data?.categories ?? []).map((f) => ({ value: f.value, label: `${f.value} (${f.count})` })),
    brands: (facets.data?.brands ?? []).map((f) => ({ value: f.value, label: `${f.value} (${f.count})` })),
    productLists: (lists.data ?? [])
      .filter((l) => l.kind !== "globalExclusion")
      .map((l) => ({ value: l.code, label: `${l.code} — ${l.name}` })),
    stores: (stores.data ?? []).map((s) => ({ value: s.code, label: `${s.code} — ${s.name}` })),
    advanced,
  };
}

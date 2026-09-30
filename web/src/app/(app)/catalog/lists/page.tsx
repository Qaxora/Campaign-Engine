import type { Metadata } from "next";
import { ListIndex } from "@/components/catalog/list-index";
import { PageHeader } from "@/components/ui/card";

export const metadata: Metadata = { title: "Product lists" };

export default function ProductListsPage() {
  return (
    <>
      <PageHeader title="Product lists" description="Named SKU lists that campaigns target or exclude. Keep them in sync with ERP exports through CSV import." />
      <ListIndex kind="standard" />
    </>
  );
}

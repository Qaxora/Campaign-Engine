import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { ArrowLeft } from "lucide-react";
import { ListEditor } from "@/components/catalog/list-editor";
import { Badge } from "@/components/ui/card";
import { getSession, serverApi } from "@/lib/api/server";
import { formatInteger, formatRelative } from "@/lib/format";

export const metadata: Metadata = { title: "Product list" };

export default async function ProductListPage({ params }: PageProps<"/catalog/lists/[code]">) {
  const { code } = await params;
  const session = (await getSession())!;
  const api = await serverApi();
  const { data: list } = await api.GET("/api/v1/product-lists/{code}", { params: { path: { code: decodeURIComponent(code) } } });
  if (!list) notFound();
  const exclusion = list.kind === "globalExclusion";

  return (
    <>
      <Link href={exclusion ? "/catalog/exclusions" : "/catalog/lists"} className="mb-4 inline-flex items-center gap-1 text-sm text-muted hover:text-foreground">
        <ArrowLeft className="size-4" aria-hidden /> {exclusion ? "Exclusions" : "Product lists"}
      </Link>
      <div className="mb-6">
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="text-2xl font-semibold tracking-tight">{list.name}</h1>
          <Badge tone={exclusion ? "danger" : "neutral"}>{exclusion ? "Global exclusion" : "Standard"}</Badge>
        </div>
        <p className="mt-1 font-mono text-sm text-muted">
          {list.code} · version {formatInteger(list.version)}
          {list.updatedAt ? ` · updated ${formatRelative(list.updatedAt)}` : ""}
        </p>
        {list.description ? <p className="mt-2 text-sm text-muted">{list.description}</p> : null}
      </div>
      <ListEditor list={list} canManage={session.current!.role !== "member"} />
    </>
  );
}

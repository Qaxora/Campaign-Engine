import type { Metadata } from "next";
import Link from "next/link";
import { Boxes, SearchX } from "lucide-react";
import { CsvImport } from "@/components/catalog/csv-import";
import { buttonClass } from "@/components/ui/button";
import { Badge, Card, CardHeader, PageHeader } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import { Input, Label } from "@/components/ui/form";
import { getSession, serverApi } from "@/lib/api/server";
import { parseProductFilters, productQuery, productsHref, PRODUCTS_PAGE_SIZE } from "@/lib/catalog";
import { formatInteger, toNumber } from "@/lib/format";

export const metadata: Metadata = { title: "Products" };

const select = "h-10 rounded-md border border-border bg-surface px-2 text-sm";

export default async function ProductsPage({ searchParams }: PageProps<"/catalog/products">) {
  const filters = parseProductFilters(await searchParams);
  const session = (await getSession())!;
  const canManage = session.current!.role !== "member";
  const api = await serverApi();
  const [products, facets] = await Promise.all([
    api.GET("/api/v1/products", { params: { query: productQuery(filters) } }),
    api.GET("/api/v1/products/facets"),
  ]);
  if (!products.data) throw new Error(`The catalog is unavailable (HTTP ${products.response.status}).`);

  const total = toNumber(products.data.totalCount);
  const pages = Math.max(1, Math.ceil(total / PRODUCTS_PAGE_SIZE));
  const filtered = Boolean(filters.search || filters.category || filters.brand || filters.active);

  return (
    <>
      <PageHeader
        title="Products"
        description={`${formatInteger(facets.data?.productCount ?? 0)} products. The catalog feeds the builder's pickers and the AI assistant; carts still carry their own prices and categories.`}
      />

      {canManage ? (
        <Card className="mb-6">
          <CardHeader
            title="Import from CSV"
            description="Header row with sku and name; optional brand, categories (separated by |) and active; any other column becomes an attribute. Existing SKUs are updated."
          />
          <div className="px-5 py-4">
            <CsvImport endpoint="/api/v1/products/import" result="products" />
          </div>
        </Card>
      ) : null}

      <form method="get" className="mb-4 grid gap-3 rounded-lg border border-border bg-surface p-4 sm:grid-cols-2 lg:grid-cols-5">
        <div className="flex flex-col gap-1.5 lg:col-span-2">
          <Label htmlFor="search">Search</Label>
          <Input id="search" name="search" placeholder="SKU or name" defaultValue={filters.search} />
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="category">Category</Label>
          <select id="category" name="category" defaultValue={filters.category} className={select}>
            <option value="">Any category</option>
            {(facets.data?.categories ?? []).map((f) => (
              <option key={f.value} value={f.value}>
                {f.value} ({f.count})
              </option>
            ))}
          </select>
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="brand">Brand</Label>
          <select id="brand" name="brand" defaultValue={filters.brand} className={select}>
            <option value="">Any brand</option>
            {(facets.data?.brands ?? []).map((f) => (
              <option key={f.value} value={f.value}>
                {f.value} ({f.count})
              </option>
            ))}
          </select>
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="active">Status</Label>
          <select id="active" name="active" defaultValue={filters.active} className={select}>
            <option value="">Any</option>
            <option value="true">Active</option>
            <option value="false">Inactive</option>
          </select>
        </div>
        <div className="flex items-center gap-2 sm:col-span-2 lg:col-span-5">
          <button type="submit" className={buttonClass("secondary", "sm")}>
            Apply filters
          </button>
          {filtered ? (
            <Link href="/catalog/products" className={buttonClass("ghost", "sm")}>
              Clear
            </Link>
          ) : null}
          <span className="ml-auto text-sm text-muted">{formatInteger(total)} shown</span>
        </div>
      </form>

      {products.data.items.length === 0 ? (
        filtered ? (
          <EmptyState icon={SearchX} title="No products match these filters" />
        ) : (
          <EmptyState icon={Boxes} title="The catalog is empty">
            Import a CSV export from your ERP or e-commerce platform.
          </EmptyState>
        )
      ) : (
        <Card className="overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full min-w-[720px] text-sm">
              <thead className="border-b border-border bg-surface-muted/60 text-left text-xs text-muted">
                <tr>
                  <th className="px-4 py-2.5 font-medium">Product</th>
                  <th className="px-4 py-2.5 font-medium">Brand</th>
                  <th className="px-4 py-2.5 font-medium">Categories</th>
                  <th className="px-4 py-2.5 font-medium">Attributes</th>
                  <th className="px-4 py-2.5 font-medium">Status</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {products.data.items.map((p) => (
                  <tr key={p.sku}>
                    <td className="px-4 py-3">
                      <p className="font-medium">{p.name}</p>
                      <p className="font-mono text-xs text-muted">{p.sku}</p>
                    </td>
                    <td className="px-4 py-3">{p.brand ?? "—"}</td>
                    <td className="px-4 py-3 text-muted">{p.categories.join(" › ") || "—"}</td>
                    <td className="px-4 py-3 text-muted">
                      {Object.entries(p.attributes).map(([k, v]) => `${k}: ${v}`).join(", ") || "—"}
                    </td>
                    <td className="px-4 py-3">{p.active ? <Badge tone="success">Active</Badge> : <Badge>Inactive</Badge>}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {pages > 1 ? (
            <nav aria-label="Pagination" className="flex items-center justify-between border-t border-border px-4 py-3 text-sm">
              <span className="text-muted">
                Page {filters.page} of {pages}
              </span>
              <div className="flex gap-2">
                {filters.page > 1 ? (
                  <Link href={productsHref(filters, filters.page - 1)} className={buttonClass("secondary", "sm")}>
                    Previous
                  </Link>
                ) : null}
                {filters.page < pages ? (
                  <Link href={productsHref(filters, filters.page + 1)} className={buttonClass("secondary", "sm")}>
                    Next
                  </Link>
                ) : null}
              </div>
            </nav>
          ) : null}
        </Card>
      )}
    </>
  );
}

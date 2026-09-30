/** URL ⇄ API query for the product catalog, and SKU text parsing. No catalog rules live here. */
export const PRODUCTS_PAGE_SIZE = 25;

type Params = Record<string, string | string[] | undefined>;
const one = (params: Params, key: string) => ((Array.isArray(params[key]) ? params[key]![0] : params[key]) as string | undefined)?.trim() ?? "";

export interface ProductFilters {
  search: string;
  category: string;
  brand: string;
  active: "" | "true" | "false";
  page: number;
}

export function parseProductFilters(params: Params): ProductFilters {
  const active = one(params, "active");
  const page = Number.parseInt(one(params, "page"), 10);
  return {
    search: one(params, "search"),
    category: one(params, "category"),
    brand: one(params, "brand"),
    active: active === "true" || active === "false" ? active : "",
    page: Number.isFinite(page) && page > 0 ? page : 1,
  };
}

export function productQuery(f: ProductFilters) {
  return {
    search: f.search || undefined,
    category: f.category || undefined,
    brand: f.brand || undefined,
    active: f.active === "" ? undefined : f.active === "true",
    page: f.page,
    pageSize: PRODUCTS_PAGE_SIZE,
  };
}

export function productsHref(f: ProductFilters, page: number): string {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries({ ...f, page })) {
    if (value !== "" && !(key === "page" && value === 1)) params.set(key, String(value));
  }

  const query = params.toString();
  return query ? `/catalog/products?${query}` : "/catalog/products";
}

/** Pasted SKUs: one per line, or separated by commas, semicolons, tabs or spaces; duplicates removed. */
export function parseSkus(text: string): string[] {
  const seen = new Set<string>();
  const result: string[] = [];
  for (const sku of text.split(/[\s,;]+/).map((s) => s.trim()).filter(Boolean)) {
    const key = sku.toUpperCase();
    if (!seen.has(key)) {
      seen.add(key);
      result.push(sku);
    }
  }

  return result;
}

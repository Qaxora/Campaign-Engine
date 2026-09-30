import { describe, expect, it } from "vitest";
import { parseProductFilters, parseSkus, productQuery, productsHref } from "./catalog";

describe("catalog", () => {
  it("parses product filters and builds the API query", () => {
    const f = parseProductFilters({ search: " tv ", active: "maybe", page: "2", category: "electronics" });
    expect(f).toEqual({ search: "tv", category: "electronics", brand: "", active: "", page: 2 });
    expect(productQuery({ ...f, active: "false" })).toMatchObject({ search: "tv", category: "electronics", brand: undefined, active: false, page: 2 });
  });

  it("builds paging links that keep filters", () => {
    const f = parseProductFilters({ brand: "Acme" });
    expect(productsHref(f, 3)).toBe("/catalog/products?brand=Acme&page=3");
    expect(productsHref(parseProductFilters({}), 1)).toBe("/catalog/products");
  });

  it("parses pasted SKUs in any common layout", () => {
    expect(parseSkus("TS-01\nTS-02, ts-01;SH-01\tSK-01  \n\n")).toEqual(["TS-01", "TS-02", "SH-01", "SK-01"]);
    expect(parseSkus("   ")).toEqual([]);
  });
});

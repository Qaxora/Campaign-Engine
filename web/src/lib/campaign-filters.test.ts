import { describe, expect, it } from "vitest";
import { filtersHref, hasFilters, parseFilters, toApiQuery } from "./campaign-filters";

describe("campaign filters", () => {
  it("parses the URL and drops invalid values", () => {
    const f = parseFilters({ search: " summer ", status: "hacked", from: "2026-13-45", to: "2026-06-30", sort: "priority", order: "asc", page: "0" });

    expect(f).toEqual({ search: "summer", status: "", channel: "", from: "", to: "2026-06-30", sort: "priority", order: "asc", page: 1 });
    expect(hasFilters(f)).toBe(true);
    expect(hasFilters(parseFilters({}))).toBe(false);
  });

  it("turns the inclusive day range into a UTC [from, to) window for the API", () => {
    const query = toApiQuery(parseFilters({ from: "2026-06-01", to: "2026-06-30", status: "active" }));

    expect(query.activeFrom).toBe("2026-06-01T00:00:00.000Z");
    expect(query.activeTo).toBe("2026-07-01T00:00:00.000Z");
    expect(query.status).toBe("active");
    expect(query.search).toBeUndefined();
  });

  it("builds links that keep filters, omit defaults and reset the page", () => {
    const f = parseFilters({ status: "active", page: "3" });

    expect(filtersHref(f, { page: 4 })).toBe("/campaigns?status=active&page=4");
    expect(filtersHref(f, { sort: "name", order: "asc" })).toBe("/campaigns?status=active&sort=name&order=asc");
    expect(filtersHref(parseFilters({}), {})).toBe("/campaigns");
  });
});

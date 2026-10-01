import { describe, expect, it } from "vitest";
import { dayRange, hrefWith, pageParam, param } from "./query";

describe("query helpers", () => {
  it("reads single values and pages", () => {
    expect(param({ a: [" x ", "y"] }, "a")).toBe("x");
    expect(param({}, "a")).toBe("");
    expect(pageParam({ page: "3" })).toBe(3);
    expect(pageParam({ page: "-1" })).toBe(1);
  });

  it("turns inclusive days into a UTC range", () => {
    expect(dayRange("2026-09-01", "2026-09-30")).toEqual({ from: "2026-09-01T00:00:00.000Z", to: "2026-10-01T00:00:00.000Z" });
    expect(dayRange("nope", "")).toEqual({ from: undefined, to: undefined });
  });

  it("builds links without empty values or page 1", () => {
    expect(hrefWith("/sales/transactions", { status: "reversed", channel: "", page: 1 })).toBe("/sales/transactions?status=reversed");
    expect(hrefWith("/sales/transactions", { page: 2 })).toBe("/sales/transactions?page=2");
  });
});

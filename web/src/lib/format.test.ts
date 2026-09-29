import { describe, expect, it } from "vitest";
import { formatAmount, formatDate, formatInteger, formatRelative, periodChange, toNumber } from "./format";

describe("format", () => {
  it("accepts the number | string values of the generated types", () => {
    expect(toNumber("12.5")).toBe(12.5);
    expect(toNumber(undefined)).toBe(0);
    expect(toNumber("not a number")).toBe(0);
    expect(formatInteger("1234")).toBe("1,234");
    expect(formatAmount(1234.5)).toBe("1,234.50");
  });

  it("describes the change against the previous period", () => {
    expect(periodChange(120, 100)).toEqual({ direction: "up", text: "+20%" });
    expect(periodChange(75, 100)).toEqual({ direction: "down", text: "−25%" });
    expect(periodChange(100.2, 100).direction).toBe("flat");
    expect(periodChange(5, 0)).toEqual({ direction: "new", text: "New this period" });
    expect(periodChange(0, 0).direction).toBe("flat");
  });

  it("formats relative times and calendar days", () => {
    const now = new Date("2026-03-10T12:00:00Z");
    expect(formatRelative("2026-03-10T11:59:30Z", now)).toBe("just now");
    expect(formatRelative("2026-03-10T11:15:00Z", now)).toBe("45 min ago");
    expect(formatRelative("2026-03-10T07:00:00Z", now)).toBe("5 h ago");
    expect(formatRelative("2026-03-08T12:00:00Z", now)).toBe("2 d ago");
    expect(formatDate("2026-03-01")).toBe("Mar 1, 2026");
    expect(formatDate("2026-03-01", false)).toBe("Mar 1");
  });
});

import { niceMax } from "@/components/dashboard/trend-chart";

describe("niceMax", () => {
  it("rounds the axis maximum to 1, 2 or 5 × 10ⁿ", () => {
    expect(niceMax(0)).toBe(0);
    expect(niceMax(7)).toBe(10);
    expect(niceMax(130)).toBe(200);
    expect(niceMax(420)).toBe(500);
    expect(niceMax(1000)).toBe(1000);
  });
});

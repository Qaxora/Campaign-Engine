import { describe, expect, it } from "vitest";
import { resolvePeriod } from "./period";

describe("resolvePeriod", () => {
  const now = new Date("2026-03-10T12:34:56Z");

  it("ends at the next full hour and spans the chosen number of days", () => {
    expect(resolvePeriod("7d", now)).toEqual({ key: "7d", label: "7 days", from: "2026-03-03T13:00:00.000Z", to: "2026-03-10T13:00:00.000Z" });
  });

  it("falls back to 30 days", () => {
    expect(resolvePeriod(undefined, now).key).toBe("30d");
    expect(resolvePeriod("1y", now).key).toBe("30d");
    expect(resolvePeriod(["7d", "90d"], now).key).toBe("30d");
  });
});

import { describe, expect, it } from "vitest";
import { audienceLine, groupBySeverity, parseSeverity, type ConflictReportItem } from "./conflicts";

const item = (severity: "error" | "warning" | "info", code: string) =>
  ({ conflict: { severity, otherCode: code }, campaign: {}, other: {} }) as unknown as ConflictReportItem;

describe("conflicts", () => {
  it("groups by severity in fixed order and drops empty groups", () => {
    const groups = groupBySeverity([item("error", "A"), item("info", "B"), item("info", "C")]);

    expect(groups.map((g) => [g.key, g.items.length])).toEqual([
      ["error", 1],
      ["info", 2],
    ]);
    expect(groupBySeverity([item("error", "A"), item("info", "B")], "info").map((g) => g.key)).toEqual(["info"]);
  });

  it("parses the severity filter", () => {
    expect(parseSeverity("warning")).toBe("warning");
    expect(parseSeverity("fatal")).toBeUndefined();
    expect(parseSeverity(undefined)).toBeUndefined();
  });

  it("reads channels and stores from the engine's audience lines", () => {
    const audience = ["Channels: store, web", "All stores", "Every customer"];
    expect(audienceLine(audience, "channels")).toBe("store, web");
    expect(audienceLine(audience, "stores")).toBe("All stores");
    expect(audienceLine(["All channels", "Stores: IST-1"], "stores")).toBe("IST-1");
  });
});

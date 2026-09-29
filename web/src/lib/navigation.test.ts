import { describe, expect, it } from "vitest";
import { activeHref } from "./navigation";

describe("navigation", () => {
  it("highlights the most specific section", () => {
    expect(activeHref("/")).toBe("/");
    expect(activeHref("/campaigns")).toBe("/campaigns");
    expect(activeHref("/campaigns/new")).toBe("/campaigns/new");
    expect(activeHref("/campaigns/3f2a")).toBe("/campaigns");
    expect(activeHref("/settings/stores")).toBe("/settings/stores");
  });
});

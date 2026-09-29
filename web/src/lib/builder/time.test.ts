import { describe, expect, it } from "vitest";
import { isoToZoned, isValidTimeZone, zonedToIso } from "./time";

describe("builder time", () => {
  it("converts wall-clock times in the campaign zone to offset timestamps and back", () => {
    expect(zonedToIso("2026-10-01T00:00", "Europe/Istanbul")).toBe("2026-10-01T00:00:00+03:00");
    expect(zonedToIso("2026-01-15T09:30", "Europe/Berlin")).toBe("2026-01-15T09:30:00+01:00");
    expect(zonedToIso("2026-07-15T09:30", "Europe/Berlin")).toBe("2026-07-15T09:30:00+02:00");
    expect(zonedToIso("2026-07-15T09:30", "UTC")).toBe("2026-07-15T09:30:00+00:00");
    expect(zonedToIso("", "UTC")).toBeNull();

    expect(isoToZoned("2026-09-30T21:00:00Z", "Europe/Istanbul")).toBe("2026-10-01T00:00");
    expect(isoToZoned("2026-10-01T00:00:00+03:00", "UTC")).toBe("2026-09-30T21:00");
    expect(isoToZoned(null, "UTC")).toBe("");
  });

  it("recognises time zones", () => {
    expect(isValidTimeZone("Europe/Istanbul")).toBe(true);
    expect(isValidTimeZone("Mars/Olympus")).toBe(false);
  });
});

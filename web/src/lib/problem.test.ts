import { describe, expect, it } from "vitest";
import { problemMessage } from "./problem";

describe("problemMessage", () => {
  it("prefers validation errors, then detail, then title", () => {
    expect(problemMessage({ title: "Validation failed", errors: ["name is required.", "code is invalid."] })).toBe("name is required. code is invalid.");
    expect(problemMessage({ errors: { email: ["Email is taken."] } })).toBe("Email is taken.");
    expect(problemMessage({ title: "Conflict", detail: "An account with this email already exists." })).toBe("An account with this email already exists.");
    expect(problemMessage({ title: "Not found" })).toBe("Not found");
  });

  it("falls back to a message for the status code", () => {
    expect(problemMessage(null, 401)).toMatch(/sign in again/);
    expect(problemMessage(null, 403)).toMatch(/permission/);
    expect(problemMessage(null, 503)).toMatch(/unavailable/);
    expect(problemMessage(undefined)).toBe("Something went wrong.");
  });
});

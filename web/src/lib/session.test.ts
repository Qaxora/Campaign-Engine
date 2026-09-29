import { describe, expect, it } from "vitest";
import { isPublicPath, safeRedirectPath, sessionCookie, SESSION_COOKIE } from "./session";

describe("session", () => {
  it("stores the token in an httpOnly, lax cookie that is secure in production", () => {
    const cookie = sessionCookie("qxs_abc", "2026-10-13T08:00:00Z", true);

    expect(cookie).toMatchObject({ name: SESSION_COOKIE, value: "qxs_abc", httpOnly: true, sameSite: "lax", secure: true, path: "/" });
    expect(cookie.expires.toISOString()).toBe("2026-10-13T08:00:00.000Z");
    expect(sessionCookie("t", new Date(), false).secure).toBe(false);
  });

  it("only allows local redirect targets after sign-in", () => {
    expect(safeRedirectPath("/campaigns?status=active")).toBe("/campaigns?status=active");
    expect(safeRedirectPath("https://evil.example")).toBe("/");
    expect(safeRedirectPath("//evil.example")).toBe("/");
    expect(safeRedirectPath(String.raw`/\evil.example`)).toBe("/"); // browsers treat /\ like //
    expect(safeRedirectPath(undefined)).toBe("/");
  });

  it("knows which pages are public", () => {
    expect(isPublicPath("/login")).toBe(true);
    expect(isPublicPath("/register")).toBe(true);
    expect(isPublicPath("/")).toBe(false);
    expect(isPublicPath("/login/extra")).toBe(false);
  });
});

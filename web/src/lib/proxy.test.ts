import { describe, expect, it } from "vitest";
import { downstreamHeaders, isBlockedPath, isSameOrigin, upstreamHeaders, upstreamUrl } from "./proxy";

describe("proxy", () => {
  it("builds the upstream URL with encoded segments and the query string", () => {
    expect(upstreamUrl("http://api:8080/", ["campaigns", "SUMMER 20%"], "?force=true")).toBe(
      "http://api:8080/api/v1/campaigns/SUMMER%2020%25?force=true",
    );
  });

  it("keeps sign-in and sign-out away from the generic proxy", () => {
    expect(isBlockedPath(["auth", "login"])).toBe(true);
    expect(isBlockedPath(["Auth", "Register"])).toBe(true);
    expect(isBlockedPath(["auth", "logout"])).toBe(true);
    expect(isBlockedPath(["auth", "me"])).toBe(false);
    expect(isBlockedPath(["auth", "switch-organization"])).toBe(false);
  });

  it("forwards only safe headers and replaces credentials with the session token", () => {
    const incoming = new Headers({
      "content-type": "application/json",
      accept: "application/json",
      cookie: "qxc_session=secret; other=1",
      authorization: "Bearer attacker",
      "x-api-key": "stolen",
    });
    const headers = upstreamHeaders(incoming, "qxs_token");

    expect(headers.get("authorization")).toBe("Bearer qxs_token");
    expect(headers.get("content-type")).toBe("application/json");
    expect(headers.get("cookie")).toBeNull();
    expect(headers.get("x-api-key")).toBeNull();
  });

  it("returns content headers and disables caching", () => {
    const headers = downstreamHeaders(new Headers({ "content-type": "application/json", etag: '"v1"', "set-cookie": "x=1", server: "kestrel" }));

    expect(headers.get("etag")).toBe('"v1"');
    expect(headers.get("cache-control")).toBe("no-store");
    expect(headers.get("set-cookie")).toBeNull();
    expect(headers.get("server")).toBeNull();
  });

  it("refuses cross-site state changes", () => {
    expect(isSameOrigin("GET", "https://evil.example", "campaign.qaxora.com")).toBe(true);
    expect(isSameOrigin("POST", null, "campaign.qaxora.com")).toBe(true);
    expect(isSameOrigin("POST", "https://campaign.qaxora.com", "campaign.qaxora.com")).toBe(true);
    expect(isSameOrigin("POST", "https://evil.example", "campaign.qaxora.com")).toBe(false);
    expect(isSameOrigin("DELETE", "not a url", "campaign.qaxora.com")).toBe(false);
  });
});

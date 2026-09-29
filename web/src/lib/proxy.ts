/**
 * Helpers for the backend-for-frontend proxy at /api/v1/* (ADR 0007). The browser talks only to this
 * origin; the proxy adds the session token and forwards to the Campaign API. No business logic here.
 */

/** Sign-in endpoints return or revoke the token, so they go through /api/session/* instead. */
const BLOCKED = new Set(["auth/login", "auth/register", "auth/logout"]);

export function isBlockedPath(segments: string[]): boolean {
  return BLOCKED.has(segments.join("/").toLowerCase());
}

export function upstreamUrl(apiBaseUrl: string, segments: string[], search: string): string {
  const base = apiBaseUrl.replace(/\/+$/, "");
  const path = segments.map(encodeURIComponent).join("/");
  return `${base}/api/v1/${path}${search}`;
}

/** Headers sent upstream: content negotiation from the browser plus the session token — nothing else. */
export function upstreamHeaders(incoming: Headers, token: string): Headers {
  const headers = new Headers();
  for (const name of ["content-type", "accept", "if-none-match", "accept-language"]) {
    const value = incoming.get(name);
    if (value) headers.set(name, value);
  }

  headers.set("authorization", `Bearer ${token}`);
  return headers;
}

/** Headers returned to the browser. */
export function downstreamHeaders(upstream: Headers): Headers {
  const headers = new Headers();
  for (const name of ["content-type", "etag", "location", "content-disposition"]) {
    const value = upstream.get(name);
    if (value) headers.set(name, value);
  }

  headers.set("cache-control", "no-store");
  return headers;
}

/**
 * Rejects cross-site state-changing requests. SameSite=Lax already keeps the cookie off cross-site
 * POSTs; this is a second line for older browsers.
 */
export function isSameOrigin(method: string, origin: string | null, host: string | null): boolean {
  if (method === "GET" || method === "HEAD" || origin === null) return true;
  try {
    return new URL(origin).host === host;
  } catch {
    return false;
  }
}

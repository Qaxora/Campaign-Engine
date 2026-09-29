/**
 * The API session token lives only in this httpOnly cookie (ADR 0007). Browser code never sees it;
 * server components and the /api/v1 proxy read it and call the API with `Authorization: Bearer`.
 */
export const SESSION_COOKIE = "qxc_session";

export interface SessionCookie {
  name: string;
  value: string;
  httpOnly: true;
  sameSite: "lax";
  secure: boolean;
  path: "/";
  expires: Date;
}

export function sessionCookie(token: string, expiresAt: string | Date, production = process.env.NODE_ENV === "production"): SessionCookie {
  return {
    name: SESSION_COOKIE,
    value: token,
    httpOnly: true,
    sameSite: "lax",
    secure: production,
    path: "/",
    expires: new Date(expiresAt),
  };
}

/** Pages reachable without a session. */
export function isPublicPath(pathname: string): boolean {
  return pathname === "/login" || pathname === "/register";
}

/** Only same-site relative paths are allowed after sign-in, never another origin. */
export function safeRedirectPath(next: string | null | undefined): string {
  if (!next || !next.startsWith("/") || next.startsWith("//") || next.startsWith("/\\")) {
    return "/";
  }

  return next;
}

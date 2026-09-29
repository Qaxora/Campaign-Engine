import { NextResponse, type NextRequest } from "next/server";
import { isPublicPath, SESSION_COOKIE } from "@/lib/session";

/**
 * Cheap routing guard: pages need a session cookie, sign-in pages do not. Whether the cookie is still
 * valid is decided by the API (the app layout redirects to /api/session/end when it is not).
 */
export function proxy(request: NextRequest) {
  const { pathname, search } = request.nextUrl;
  const signedIn = request.cookies.has(SESSION_COOKIE);

  if (!signedIn && !isPublicPath(pathname)) {
    const login = new URL("/login", request.url);
    if (pathname !== "/") login.searchParams.set("next", pathname + search);
    return NextResponse.redirect(login);
  }

  if (signedIn && isPublicPath(pathname)) {
    return NextResponse.redirect(new URL("/", request.url));
  }

  return NextResponse.next();
}

export const config = {
  // Pages only: API routes, Next internals and static files handle themselves.
  matcher: ["/((?!api/|_next/|favicon.ico|.*\\.(?:svg|png|jpg|ico|webp)$).*)"],
};

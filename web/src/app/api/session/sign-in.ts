import { NextResponse } from "next/server";
import { apiBaseUrl } from "@/lib/config";
import { sessionCookie } from "@/lib/session";

/**
 * Calls a sign-in endpoint of the API, keeps the token in the httpOnly cookie and returns the session
 * without the token.
 */
export async function signIn(apiPath: "/api/v1/auth/login" | "/api/v1/auth/register", body: unknown): Promise<Response> {
  const upstream = await fetch(`${apiBaseUrl()}${apiPath}`, {
    method: "POST",
    headers: { "content-type": "application/json", accept: "application/json" },
    body: JSON.stringify(body),
    cache: "no-store",
  });
  const payload = await upstream.json().catch(() => null);
  if (!upstream.ok || !payload?.token) {
    return NextResponse.json(payload ?? { title: "Sign-in failed." }, { status: upstream.ok ? 502 : upstream.status });
  }

  const response = NextResponse.json({ session: payload.session });
  response.cookies.set(sessionCookie(payload.token, payload.session.expiresAt));
  return response;
}

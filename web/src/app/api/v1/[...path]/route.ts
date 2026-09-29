import { cookies } from "next/headers";
import { NextResponse, type NextRequest } from "next/server";
import { apiBaseUrl } from "@/lib/config";
import { downstreamHeaders, isBlockedPath, isSameOrigin, upstreamHeaders, upstreamUrl } from "@/lib/proxy";
import { SESSION_COOKIE } from "@/lib/session";

/** Backend-for-frontend proxy: browser → /api/v1/* here → Campaign API with the session token (ADR 0007). */
async function forward(request: NextRequest, ctx: RouteContext<"/api/v1/[...path]">): Promise<Response> {
  const { path } = await ctx.params;
  if (isBlockedPath(path)) {
    return NextResponse.json({ title: "Use /api/session/* to sign in or out." }, { status: 404 });
  }

  if (!isSameOrigin(request.method, request.headers.get("origin"), request.headers.get("host"))) {
    return NextResponse.json({ title: "Cross-site request refused." }, { status: 403 });
  }

  const token = (await cookies()).get(SESSION_COOKIE)?.value;
  if (!token) {
    return NextResponse.json({ title: "Not signed in.", status: 401 }, { status: 401 });
  }

  const hasBody = request.method !== "GET" && request.method !== "HEAD";
  const upstream = await fetch(upstreamUrl(apiBaseUrl(), path, request.nextUrl.search), {
    method: request.method,
    headers: upstreamHeaders(request.headers, token),
    body: hasBody ? await request.arrayBuffer() : undefined,
    cache: "no-store",
    redirect: "manual",
  });

  return new Response(upstream.status === 204 || upstream.status === 304 ? null : upstream.body, {
    status: upstream.status,
    headers: downstreamHeaders(upstream.headers),
  });
}

export const GET = forward;
export const POST = forward;
export const PUT = forward;
export const PATCH = forward;
export const DELETE = forward;

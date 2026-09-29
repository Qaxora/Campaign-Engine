import { cache } from "react";
import { cookies } from "next/headers";
import createClient from "openapi-fetch";
import { apiBaseUrl } from "@/lib/config";
import { SESSION_COOKIE } from "@/lib/session";
import type { components, paths } from "./schema";

export type SessionInfo = components["schemas"]["SessionInfo"];
export type MembershipInfo = components["schemas"]["MembershipInfo"];

/** Typed API client for server components and route handlers, authenticated with the session cookie. */
export async function serverApi() {
  const token = (await cookies()).get(SESSION_COOKIE)?.value;
  return createClient<paths>({
    baseUrl: apiBaseUrl(),
    headers: token ? { authorization: `Bearer ${token}` } : {},
    cache: "no-store",
  });
}

/** The signed-in user and organization, or null. Deduplicated per request. */
export const getSession = cache(async (): Promise<SessionInfo | null> => {
  if (!(await cookies()).get(SESSION_COOKIE)) return null;
  const api = await serverApi();
  const { data, response } = await api.GET("/api/v1/auth/me");
  if (response.status === 401) return null;
  if (!data) throw new Error(`Could not load the session (HTTP ${response.status}).`);
  return data;
});

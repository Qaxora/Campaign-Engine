"use client";

import createClient from "openapi-fetch";
import type { paths } from "./schema";

/**
 * Typed API client for client components. It calls this origin's /api/v1/* proxy, which adds the
 * session token server-side — the browser never holds it.
 */
export const api = createClient<paths>({ baseUrl: "" });

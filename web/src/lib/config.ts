/** Server-side configuration. `CAMPAIGN_API_URL` is never exposed to the browser. */
export function apiBaseUrl(): string {
  const url = process.env.CAMPAIGN_API_URL;
  if (!url) {
    throw new Error("CAMPAIGN_API_URL is not set. Point it at the Campaign API, e.g. http://localhost:5080.");
  }

  return url.replace(/\/+$/, "");
}

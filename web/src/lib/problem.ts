/** RFC 9457 problem details as returned by the Campaign API. */
export interface Problem {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]> | string[];
}

/** One readable message from an API error body, for forms and toasts. */
export function problemMessage(body: unknown, status?: number): string {
  if (body && typeof body === "object") {
    const problem = body as Problem;
    const errors = Array.isArray(problem.errors)
      ? problem.errors
      : problem.errors
        ? Object.values(problem.errors).flat()
        : [];
    if (errors.length > 0) return errors.join(" ");
    if (problem.detail) return problem.detail;
    if (problem.title) return problem.title;
  }

  if (typeof body === "string" && body.trim().length > 0 && body.length < 300) return body;
  if (status === 401) return "Your session has ended. Please sign in again.";
  if (status === 403) return "You do not have permission to do this.";
  if (status && status >= 500) return "The service is unavailable. Please try again.";
  return "Something went wrong.";
}

import { z } from "zod";

/** Client-side checks mirror the API's rules for fast feedback; the API remains the authority. */
export const loginSchema = z.object({
  email: z.string().trim().email("Enter a valid email address."),
  password: z.string().min(1, "Enter your password."),
});

export const registerSchema = z.object({
  name: z.string().trim().min(1, "Enter your name.").max(128),
  email: z.string().trim().email("Enter a valid email address.").max(256),
  password: z.string().min(8, "Use at least 8 characters."),
  organizationName: z.string().trim().min(1, "Enter your company name.").max(128),
});

export const organizationSchema = z.object({
  name: z.string().trim().min(1, "Enter the organization name.").max(128),
});

export type FieldErrors<T> = Partial<Record<keyof T, string>>;

/** First error per field, for inline messages. */
export function fieldErrors<T>(error: z.ZodError<T>): FieldErrors<T> {
  const result: FieldErrors<T> = {};
  for (const issue of error.issues) {
    const key = issue.path[0] as keyof T;
    result[key] ??= issue.message;
  }

  return result;
}

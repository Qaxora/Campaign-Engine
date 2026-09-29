import type { Metadata } from "next";
import { redirect } from "next/navigation";
import { getSession } from "@/lib/api/server";
import { CreateOrganizationForm } from "./create-organization-form";

export const metadata: Metadata = { title: "Create organization" };

/** For accounts without an organization (added users who left, or anyone creating a second one). */
export default async function OnboardingPage() {
  const session = await getSession();
  if (!session) redirect("/api/session/end");

  return (
    <main className="flex min-h-dvh items-center justify-center px-4 py-12">
      <div className="w-full max-w-sm rounded-lg border border-border bg-surface p-6 shadow-sm">
        <h1 className="text-xl font-semibold">Create an organization</h1>
        <p className="mb-6 mt-1 text-sm text-muted">
          {session.current
            ? "Organizations keep campaigns, stores, keys and sales completely separate."
            : `Signed in as ${session.user.email}. You are not a member of any organization yet — create one, or ask an admin to add you.`}
        </p>
        <CreateOrganizationForm canCancel={session.current != null} />
      </div>
    </main>
  );
}

import { redirect } from "next/navigation";
import { AppShell } from "@/components/shell/app-shell";
import { getSession } from "@/lib/api/server";

export default async function AppLayout({ children }: { children: React.ReactNode }) {
  const session = await getSession();
  if (!session) redirect("/api/session/end");
  if (!session.current) redirect("/onboarding");

  return <AppShell session={session}>{children}</AppShell>;
}

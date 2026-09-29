"use client";

import { useState } from "react";
import { Menu, X } from "lucide-react";
import type { SessionInfo } from "@/lib/api/server";
import { OrganizationSwitcher } from "./organization-switcher";
import { Sidebar } from "./sidebar";
import { UserMenu } from "./user-menu";

/** Fixed sidebar on desktop, a drawer on small screens. */
export function AppShell({ session, children }: { session: SessionInfo; children: React.ReactNode }) {
  const [open, setOpen] = useState(false);

  return (
    <div className="min-h-dvh lg:pl-64">
      <aside className="fixed inset-y-0 left-0 z-30 hidden w-64 border-r border-border bg-surface lg:block">
        <Sidebar />
      </aside>

      {open ? (
        <div className="fixed inset-0 z-40 lg:hidden" role="dialog" aria-modal="true" aria-label="Navigation">
          <button className="absolute inset-0 bg-black/40" aria-label="Close navigation" onClick={() => setOpen(false)} />
          <aside className="absolute inset-y-0 left-0 w-72 max-w-[85%] border-r border-border bg-surface">
            <button
              className="absolute right-3 top-3 rounded-md p-1.5 text-muted hover:bg-surface-muted"
              aria-label="Close navigation"
              onClick={() => setOpen(false)}
            >
              <X className="size-5" />
            </button>
            <Sidebar onNavigate={() => setOpen(false)} />
          </aside>
        </div>
      ) : null}

      <header className="sticky top-0 z-20 flex h-14 items-center gap-3 border-b border-border bg-surface/95 px-4 backdrop-blur sm:px-6">
        <button className="-ml-1 rounded-md p-1.5 text-muted hover:bg-surface-muted lg:hidden" aria-label="Open navigation" onClick={() => setOpen(true)}>
          <Menu className="size-5" />
        </button>
        <OrganizationSwitcher session={session} />
        <div className="ml-auto">
          <UserMenu session={session} />
        </div>
      </header>

      <main className="mx-auto w-full max-w-7xl px-4 py-6 sm:px-6 lg:py-8">{children}</main>
    </div>
  );
}

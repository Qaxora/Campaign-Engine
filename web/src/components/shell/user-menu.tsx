"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { LogOut } from "lucide-react";
import type { SessionInfo } from "@/lib/api/server";
import { initials } from "@/lib/utils";

export function UserMenu({ session }: { session: SessionInfo }) {
  const router = useRouter();
  const [open, setOpen] = useState(false);

  async function signOut() {
    await fetch("/api/session/logout", { method: "POST" });
    router.replace("/login");
    router.refresh();
  }

  return (
    <div className="relative">
      <button
        className="flex size-8 items-center justify-center rounded-full bg-surface-muted text-xs font-semibold hover:ring-4 hover:ring-ring"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-label="Account"
        onClick={() => setOpen((value) => !value)}
      >
        {initials(session.user.name)}
      </button>
      {open ? (
        <div role="menu" className="absolute right-0 top-full z-50 mt-1 w-64 rounded-lg border border-border bg-surface p-1 shadow-lg">
          <div className="border-b border-border px-3 py-2">
            <p className="truncate text-sm font-medium">{session.user.name}</p>
            <p className="truncate text-xs text-muted">{session.user.email}</p>
          </div>
          <button role="menuitem" onClick={signOut} className="mt-1 flex w-full items-center gap-2 rounded-md px-3 py-2 text-sm hover:bg-surface-muted">
            <LogOut className="size-4" aria-hidden />
            Sign out
          </button>
        </div>
      ) : null}
    </div>
  );
}

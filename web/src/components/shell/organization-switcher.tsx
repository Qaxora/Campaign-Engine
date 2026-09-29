"use client";

import { useRouter } from "next/navigation";
import { useState, useTransition } from "react";
import { Check, ChevronsUpDown, Plus } from "lucide-react";
import { api } from "@/lib/api/client";
import type { SessionInfo } from "@/lib/api/server";
import { problemMessage } from "@/lib/problem";
import { cn, initials } from "@/lib/utils";
import { Badge } from "@/components/ui/card";

/** Switches the session to another organization; every page then reloads with that tenant's data. */
export function OrganizationSwitcher({ session }: { session: SessionInfo }) {
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [error, setError] = useState<string>();
  const [pending, startTransition] = useTransition();
  const current = session.current;

  async function switchTo(organizationId: string) {
    setOpen(false);
    if (organizationId === current?.organizationId) return;
    const { error: problem, response } = await api.POST("/api/v1/auth/switch-organization", { body: { organizationId } });
    if (problem || !response.ok) {
      setError(problemMessage(problem, response.status));
      return;
    }

    startTransition(() => {
      router.push("/");
      router.refresh();
    });
  }

  return (
    <div className="relative">
      <button
        className="flex items-center gap-2 rounded-md px-2 py-1.5 text-sm hover:bg-surface-muted disabled:opacity-60"
        aria-haspopup="listbox"
        aria-expanded={open}
        disabled={pending}
        onClick={() => setOpen((value) => !value)}
      >
        <span className="flex size-6 items-center justify-center rounded bg-brand-soft text-xs font-semibold text-brand">
          {initials(current?.organizationName ?? "?")}
        </span>
        <span className="max-w-[12rem] truncate font-medium">{current?.organizationName}</span>
        {current ? <Badge>{current.role}</Badge> : null}
        <ChevronsUpDown className="size-4 text-muted" aria-hidden />
      </button>
      {error ? <p className="absolute left-0 top-full mt-1 text-xs text-danger">{error}</p> : null}
      {open ? (
        <div className="absolute left-0 top-full z-50 mt-1 w-72 rounded-lg border border-border bg-surface p-1 shadow-lg">
          <ul role="listbox" aria-label="Organizations">
            {session.memberships.map((membership) => {
              const selected = membership.organizationId === current?.organizationId;
              return (
                <li key={membership.organizationId} role="option" aria-selected={selected}>
                  <button
                    className={cn("flex w-full items-center gap-2 rounded-md px-2 py-2 text-left text-sm hover:bg-surface-muted", selected && "font-medium")}
                    onClick={() => switchTo(membership.organizationId)}
                  >
                    <span className="flex size-6 items-center justify-center rounded bg-surface-muted text-xs font-semibold">
                      {initials(membership.organizationName)}
                    </span>
                    <span className="flex-1 truncate">{membership.organizationName}</span>
                    {selected ? <Check className="size-4 text-brand" aria-hidden /> : <span className="text-xs text-muted">{membership.role}</span>}
                  </button>
                </li>
              );
            })}
          </ul>
          <a href="/onboarding" className="mt-1 flex items-center gap-2 border-t border-border px-2 py-2 text-sm text-muted hover:text-foreground">
            <Plus className="size-4" aria-hidden />
            New organization
          </a>
        </div>
      ) : null}
    </div>
  );
}

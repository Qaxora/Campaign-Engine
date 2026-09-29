"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { Megaphone } from "lucide-react";
import { activeHref, navigation } from "@/lib/navigation";
import { cn } from "@/lib/utils";

export function Sidebar({ onNavigate }: { onNavigate?: () => void }) {
  const current = activeHref(usePathname());

  return (
    <nav aria-label="Main" className="flex h-full flex-col">
      <Link href="/" onClick={onNavigate} className="flex h-14 items-center gap-2 border-b border-border px-5">
        <span className="flex size-7 items-center justify-center rounded-md bg-brand text-brand-foreground">
          <Megaphone className="size-4" aria-hidden />
        </span>
        <span className="font-semibold tracking-tight">Qaxora Campaign</span>
      </Link>
      <ul className="flex-1 space-y-5 overflow-y-auto px-3 py-4">
        {navigation.map((section) => (
          <li key={section.title}>
            {section.href ? (
              <NavItem href={section.href} active={current === section.href} onNavigate={onNavigate}>
                <section.icon className="size-4" aria-hidden />
                {section.title}
              </NavItem>
            ) : (
              <>
                <p className="mb-1 flex items-center gap-2 px-2 text-xs font-medium uppercase tracking-wide text-muted">
                  <section.icon className="size-3.5" aria-hidden />
                  {section.title}
                </p>
                <ul className="space-y-0.5">
                  {section.children!.map((link) => (
                    <li key={link.href}>
                      <NavItem href={link.href} active={current === link.href} onNavigate={onNavigate} indent>
                        {link.title}
                      </NavItem>
                    </li>
                  ))}
                </ul>
              </>
            )}
          </li>
        ))}
      </ul>
    </nav>
  );
}

function NavItem({
  href,
  active,
  indent,
  onNavigate,
  children,
}: {
  href: string;
  active: boolean;
  indent?: boolean;
  onNavigate?: () => void;
  children: React.ReactNode;
}) {
  return (
    <Link
      href={href}
      onClick={onNavigate}
      aria-current={active ? "page" : undefined}
      className={cn(
        "flex items-center gap-2 rounded-md px-2 py-1.5 text-sm transition-colors",
        indent && "pl-7",
        active ? "bg-brand-soft font-medium text-brand" : "text-foreground/80 hover:bg-surface-muted hover:text-foreground",
      )}
    >
      {children}
    </Link>
  );
}

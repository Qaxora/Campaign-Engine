import { Megaphone } from "lucide-react";

export default function AuthLayout({ children }: { children: React.ReactNode }) {
  return (
    <main className="flex min-h-dvh items-center justify-center px-4 py-12">
      <div className="w-full max-w-sm">
        <div className="mb-8 flex items-center justify-center gap-2">
          <span className="flex size-9 items-center justify-center rounded-lg bg-brand text-brand-foreground">
            <Megaphone className="size-5" aria-hidden />
          </span>
          <span className="text-lg font-semibold tracking-tight">Qaxora Campaign</span>
        </div>
        <div className="rounded-lg border border-border bg-surface p-6 shadow-sm">{children}</div>
      </div>
    </main>
  );
}

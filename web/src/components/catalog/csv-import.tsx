"use client";

import { useRouter } from "next/navigation";
import { useRef, useState } from "react";
import { Upload } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Alert } from "@/components/ui/form";
import { problemMessage } from "@/lib/problem";

/**
 * Sends a CSV file as-is to an API import endpoint (through the /api/v1 proxy). Parsing, validation
 * and upserting happen in the API; this only reports its answer.
 */
export function CsvImport({
  endpoint,
  result,
  label = "Import CSV",
  modes,
}: {
  endpoint: string;
  /** How to summarise the API answer (a plain value: server components cannot pass functions). */
  result: "products" | "skus";
  label?: string;
  modes?: { value: string; label: string }[];
}) {
  const router = useRouter();
  const input = useRef<HTMLInputElement>(null);
  const [mode, setMode] = useState(modes?.[0]?.value);
  const [pending, setPending] = useState(false);
  const [message, setMessage] = useState<{ tone: "success" | "danger"; text: string }>();

  async function upload(file: File) {
    setPending(true);
    setMessage(undefined);
    const url = mode ? `${endpoint}?mode=${mode}` : endpoint;
    const response = await fetch(url, { method: "PUT", headers: { "content-type": "text/csv" }, body: await file.text() });
    const body = await response.json().catch(() => null);
    setPending(false);
    if (input.current) input.current.value = "";
    if (!response.ok) {
      setMessage({ tone: "danger", text: problemMessage(body, response.status) });
      return;
    }

    setMessage({ tone: "success", text: `${file.name}: ${summarise(result, body)}` });
    router.refresh();
  }

  return (
    <div className="space-y-2">
      <div className="flex flex-wrap items-center gap-2">
        {modes ? (
          <select aria-label="Import mode" value={mode} onChange={(e) => setMode(e.target.value)} className="h-8 rounded-md border border-border bg-surface px-2 text-sm">
            {modes.map((m) => (
              <option key={m.value} value={m.value}>
                {m.label}
              </option>
            ))}
          </select>
        ) : null}
        <input ref={input} type="file" accept=".csv,.txt,text/csv,text/plain" className="sr-only" id={`${endpoint}-file`} onChange={(e) => e.target.files?.[0] && upload(e.target.files[0])} />
        <Button variant="secondary" size="sm" disabled={pending} onClick={() => input.current?.click()}>
          <Upload className="size-4" aria-hidden />
          {pending ? "Importing…" : label}
        </Button>
      </div>
      {message ? <Alert tone={message.tone}>{message.text}</Alert> : null}
    </div>
  );
}

function summarise(kind: "products" | "skus", body: unknown): string {
  if (kind === "products") {
    const r = body as { created: number; updated: number };
    return `${r.created} created, ${r.updated} updated.`;
  }

  return `the list now has ${(body as { skus?: string[] }).skus?.length ?? 0} SKUs.`;
}

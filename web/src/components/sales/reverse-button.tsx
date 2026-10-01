"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Undo2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Dialog } from "@/components/ui/dialog";
import { Alert, Field, Input } from "@/components/ui/form";
import { api } from "@/lib/api/client";
import { problemMessage } from "@/lib/problem";

/** Reverses a sale (void / full return). The API releases usage and budget and is idempotent. */
export function ReverseButton({ transactionId }: { transactionId: string }) {
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string>();
  const [pending, setPending] = useState(false);

  async function reverse() {
    setPending(true);
    setError(undefined);
    const { error: problem, response } = await api.POST("/api/v1/redemptions/{transactionId}/reverse", {
      params: { path: { transactionId } },
      body: { reason: reason.trim() || null },
    });
    setPending(false);
    if (!response.ok) {
      setError(problemMessage(problem, response.status));
      return;
    }

    setOpen(false);
    router.refresh();
  }

  return (
    <>
      <Button variant="secondary" onClick={() => setOpen(true)}>
        <Undo2 className="size-4" aria-hidden /> Reverse sale
      </Button>
      <Dialog
        open={open}
        title={`Reverse ${transactionId}?`}
        onClose={() => setOpen(false)}
        footer={
          <>
            <Button variant="secondary" onClick={() => setOpen(false)}>
              Cancel
            </Button>
            <Button variant="danger" disabled={pending} onClick={reverse}>
              {pending ? "Reversing…" : "Reverse"}
            </Button>
          </>
        }
      >
        <div className="space-y-3 px-5 py-4 text-sm">
          {error ? <Alert>{error}</Alert> : null}
          <p className="text-muted">
            Use this for a voided receipt or a full return. The campaigns&apos; usage, per-customer counts, coupon uses and budgets are released. The
            transaction stays in the ledger as reversed.
          </p>
          <Field id="reason" label="Reason (optional)">
            <Input id="reason" value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Full return at store" />
          </Field>
        </div>
      </Dialog>
    </>
  );
}

"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Archive, Pause, Play, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Dialog } from "@/components/ui/dialog";
import { Alert } from "@/components/ui/form";
import { api } from "@/lib/api/client";
import type { components } from "@/lib/api/schema";
import { problemMessage } from "@/lib/problem";
import { ConflictList } from "./conflict-list";

type Conflict = components["schemas"]["CampaignConflict"];
type Status = components["schemas"]["CampaignStatus"];
type Action = "activate" | "pause" | "archive" | "delete";

/**
 * Lifecycle buttons. Which transitions exist and whether conflicts block activation is decided by the
 * API; this component shows its answers and asks a human to confirm.
 */
export function LifecycleActions({ id, code, status, conflicts }: { id: string; code: string; status: Status; conflicts: Conflict[] }) {
  const router = useRouter();
  const [dialog, setDialog] = useState<Action>();
  const [blocking, setBlocking] = useState<Conflict[]>();
  const [acknowledged, setAcknowledged] = useState(false);
  const [error, setError] = useState<string>();
  const [pending, setPending] = useState(false);

  function open(action: Action) {
    setDialog(action);
    setBlocking(undefined);
    setAcknowledged(false);
    setError(undefined);
  }

  async function run(action: Action, force = false) {
    setPending(true);
    setError(undefined);
    const params = { params: { path: { id } } };
    const result =
      action === "activate"
        ? await api.POST("/api/v1/campaigns/{id}/activate", { params: { path: { id }, query: { force } } })
        : action === "pause"
          ? await api.POST("/api/v1/campaigns/{id}/pause", params)
          : action === "archive"
            ? await api.POST("/api/v1/campaigns/{id}/archive", params)
            : await api.DELETE("/api/v1/campaigns/{id}", params);
    setPending(false);

    if (!result.response.ok) {
      const body = result.error as { conflicts?: Conflict[] } | undefined;
      if (action === "activate" && result.response.status === 409 && body?.conflicts) {
        setBlocking(body.conflicts);
        return;
      }

      setError(problemMessage(body, result.response.status));
      return;
    }

    setDialog(undefined);
    if (action === "delete") router.push("/campaigns");
    router.refresh();
  }

  const shown = blocking ?? conflicts;
  const hasErrors = shown.some((c) => c.severity === "error");

  return (
    <>
      <div className="flex flex-wrap gap-2">
        {status === "draft" || status === "paused" ? (
          <Button onClick={() => open("activate")}>
            <Play className="size-4" aria-hidden /> Activate
          </Button>
        ) : null}
        {status === "active" ? (
          <Button variant="secondary" onClick={() => open("pause")}>
            <Pause className="size-4" aria-hidden /> Pause
          </Button>
        ) : null}
        {status === "active" || status === "paused" ? (
          <Button variant="secondary" onClick={() => open("archive")}>
            <Archive className="size-4" aria-hidden /> Archive
          </Button>
        ) : null}
        {status === "draft" ? (
          <Button variant="ghost" onClick={() => open("delete")}>
            <Trash2 className="size-4" aria-hidden /> Delete
          </Button>
        ) : null}
      </div>

      <Dialog
        open={dialog === "activate"}
        title={`Activate ${code}?`}
        onClose={() => setDialog(undefined)}
        footer={
          <>
            <Button variant="secondary" onClick={() => setDialog(undefined)}>
              Cancel
            </Button>
            <Button disabled={pending || (hasErrors && !acknowledged)} onClick={() => run("activate", hasErrors)}>
              {pending ? "Activating…" : hasErrors ? "Activate anyway" : "Activate"}
            </Button>
          </>
        }
      >
        {error ? (
          <div className="px-5 pt-4">
            <Alert>{error}</Alert>
          </div>
        ) : null}
        {shown.length === 0 ? (
          <p className="px-5 py-4 text-sm text-muted">No conflicts with live campaigns. Carts will be priced with this campaign as soon as it is active.</p>
        ) : (
          <>
            <p className="px-5 pt-4 text-sm text-muted">
              {blocking ? "The API refused activation because of these conflicts:" : "The conflict analysis found:"}
            </p>
            <ConflictList conflicts={shown} />
            {hasErrors ? (
              <label className="flex items-start gap-2 border-t border-border px-5 py-3 text-sm">
                <input type="checkbox" className="mt-0.5" checked={acknowledged} onChange={(e) => setAcknowledged(e.target.checked)} />
                I reviewed the blocking conflicts and want to activate anyway.
              </label>
            ) : null}
          </>
        )}
      </Dialog>

      {(["pause", "archive", "delete"] as const).map((action) => (
        <Dialog
          key={action}
          open={dialog === action}
          title={`${action === "pause" ? "Pause" : action === "archive" ? "Archive" : "Delete"} ${code}?`}
          onClose={() => setDialog(undefined)}
          footer={
            <>
              <Button variant="secondary" onClick={() => setDialog(undefined)}>
                Cancel
              </Button>
              <Button variant={action === "pause" ? "primary" : "danger"} disabled={pending} onClick={() => run(action)}>
                {pending ? "Working…" : action === "pause" ? "Pause" : action === "archive" ? "Archive" : "Delete"}
              </Button>
            </>
          }
        >
          <div className="space-y-3 px-5 py-4 text-sm text-muted">
            {error ? <Alert>{error}</Alert> : null}
            <p>
              {action === "pause"
                ? "Carts stop receiving this discount immediately. You can activate it again later."
                : action === "archive"
                  ? "Archiving is final: the campaign stops and cannot be activated again. Its redemptions stay in the ledger."
                  : "The draft is removed permanently."}
            </p>
          </div>
        </Dialog>
      ))}
    </>
  );
}

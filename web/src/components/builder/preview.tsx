"use client";

import { useEffect, useState } from "react";
import { AlertTriangle, CheckCircle2, Loader2, ShieldCheck } from "lucide-react";
import { ConflictList } from "@/components/campaigns/conflict-list";
import { Badge } from "@/components/ui/card";
import { api } from "@/lib/api/client";
import type { components } from "@/lib/api/schema";
import type { Campaign } from "@/lib/builder/model";
import { problemMessage } from "@/lib/problem";

type Description = components["schemas"]["CampaignDescription"];
type Conflict = components["schemas"]["CampaignConflict"];

export interface Review {
  pending: boolean;
  valid: boolean;
  errors: string[];
  warnings: string[];
  description?: Description;
  conflicts: Conflict[];
}

const initial: Omit<Review, "pending"> = { valid: false, errors: [], warnings: [], conflicts: [] };

/**
 * Asks the API about the current draft: is it valid, what does it do, what does it conflict with.
 * Debounced so typing does not flood the API; nothing is saved. The review is "pending" while it
 * belongs to an older version of the draft.
 */
export function useReview(definition: Campaign): Review {
  const [result, setResult] = useState<{ json: string; review: Omit<Review, "pending"> }>();
  const json = JSON.stringify(definition);

  useEffect(() => {
    let cancelled = false;
    const timer = setTimeout(async () => {
      const body = JSON.parse(json) as Campaign;
      const done = (review: Omit<Review, "pending">) => !cancelled && setResult({ json, review });
      const validation = await api.POST("/api/v1/campaigns/validate", { body });
      if (!validation.data) {
        done({ ...initial, errors: [problemMessage(validation.error, validation.response.status)] });
        return;
      }

      const { valid, errors, warnings } = validation.data;
      if (!valid) {
        done({ valid, errors, warnings, conflicts: [] });
        return;
      }

      const [description, conflicts] = await Promise.all([
        api.POST("/api/v1/campaigns/describe", { body }),
        api.POST("/api/v1/campaigns/conflicts", { body }),
      ]);
      done({ valid, errors, warnings, description: description.data, conflicts: conflicts.data ?? [] });
    }, 600);

    return () => {
      cancelled = true;
      clearTimeout(timer);
    };
  }, [json]);

  return { ...(result?.review ?? initial), pending: result?.json !== json };
}

export function ReviewPanel({ review }: { review: Review }) {
  return (
    <div className="rounded-lg border border-border bg-surface">
      <div className="flex items-center justify-between border-b border-border px-5 py-3">
        <h2 className="text-sm font-semibold">Review</h2>
        {review.pending ? (
          <span className="inline-flex items-center gap-1.5 text-xs text-muted">
            <Loader2 className="size-3.5 animate-spin" aria-hidden /> Checking…
          </span>
        ) : review.valid ? (
          <Badge tone="success">
            <CheckCircle2 className="mr-1 size-3" aria-hidden /> Valid
          </Badge>
        ) : (
          <Badge tone="danger">
            <AlertTriangle className="mr-1 size-3" aria-hidden /> {review.errors.length} to fix
          </Badge>
        )}
      </div>

      <div aria-live="polite" className="space-y-4 px-5 py-4 text-sm">
        {review.errors.length > 0 ? (
          <div>
            <p className="mb-1 font-medium text-danger">Fix before saving</p>
            <ul className="list-disc space-y-0.5 pl-5 text-danger">
              {review.errors.map((e) => (
                <li key={e}>{e}</li>
              ))}
            </ul>
          </div>
        ) : null}

        {review.description ? (
          <div>
            <p className="mb-1 text-xs font-medium uppercase tracking-wide text-muted">What it does</p>
            <p>{review.description.summary}</p>
            <ul className="mt-2 space-y-0.5 text-muted">
              {[...review.description.products, ...review.description.audience, ...review.description.schedule, ...review.description.limits].map((line) => (
                <li key={line}>{line}</li>
              ))}
              <li>{review.description.combination}</li>
            </ul>
          </div>
        ) : null}

        {review.warnings.length > 0 ? (
          <ul className="space-y-0.5 text-warning">
            {review.warnings.map((w) => (
              <li key={w}>{w}</li>
            ))}
          </ul>
        ) : null}
      </div>

      {review.valid ? (
        <div className="border-t border-border">
          <p className="flex items-center justify-between px-5 pt-3 text-xs font-medium uppercase tracking-wide text-muted">
            Conflicts with live campaigns
            {review.conflicts.length > 0 ? <Badge tone="warning">{review.conflicts.length}</Badge> : null}
          </p>
          {review.conflicts.length === 0 ? (
            <p className="flex items-center gap-2 px-5 py-3 text-sm text-muted">
              <ShieldCheck className="size-4 text-success" aria-hidden /> None
            </p>
          ) : (
            <ConflictList conflicts={review.conflicts} />
          )}
        </div>
      ) : null}
    </div>
  );
}

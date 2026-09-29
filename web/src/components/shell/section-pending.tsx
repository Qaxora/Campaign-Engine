import { Construction } from "lucide-react";
import { PageHeader } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";

/**
 * A section whose screen ships in a later roadmap issue. The API behind it already exists; the page
 * says so plainly instead of showing invented data.
 */
export function SectionPending({ title, description, issue, api }: { title: string; description: string; issue: number; api: string }) {
  return (
    <>
      <PageHeader title={title} description={description} />
      <EmptyState icon={Construction} title="This screen is on the way">
        <p>
          It ships with{" "}
          <a className="font-medium text-brand hover:underline" href={`https://github.com/Qaxora/Campaign-Engine/issues/${issue}`}>
            issue #{issue}
          </a>
          . The data is already available through <code className="rounded bg-surface-muted px-1 py-0.5 font-mono text-xs">{api}</code>.
        </p>
      </EmptyState>
    </>
  );
}

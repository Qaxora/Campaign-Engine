import Link from "next/link";
import { CheckCircle2, Circle } from "lucide-react";
import { Badge, Card, CardHeader, PageHeader } from "@/components/ui/card";
import { getSession, serverApi } from "@/lib/api/server";

/**
 * Getting-started checklist from live data. The KPI dashboard (redemptions, discount, trends) arrives
 * with issue #12 on top of /api/v1/analytics.
 */
export default async function DashboardPage() {
  const session = (await getSession())!;
  const api = await serverApi();
  const [stores, keys, campaigns, organization] = await Promise.all([
    api.GET("/api/v1/stores"),
    api.GET("/api/v1/api-keys"),
    api.GET("/api/v1/campaigns", { params: { query: { pageSize: 1 } } }),
    api.GET("/api/v1/organization"),
  ]);

  // API keys are visible to owners and admins only; members see the step as not applicable.
  const canManage = session.current!.role !== "member";
  const steps = [
    { done: (stores.data?.length ?? 0) > 0, title: "Register your stores", detail: "Carts send the store code as storeId.", href: "/settings/stores" },
    ...(canManage
      ? [{ done: (keys.data ?? []).some((k) => !k.revokedAt), title: "Create an API key", detail: "Connect your POS or e-commerce platform.", href: "/integrations/api-keys" }]
      : []),
    { done: Number(campaigns.data?.totalCount ?? 0) > 0, title: "Create your first campaign", detail: "Visually, or describe it to the AI assistant.", href: "/campaigns/new" },
  ];

  return (
    <>
      <PageHeader
        title={`Welcome, ${session.user.name.split(" ")[0]}`}
        description="Qaxora Campaign keeps your promotions consistent across every store and channel."
      />
      <div className="grid gap-6 lg:grid-cols-3">
        <Card className="lg:col-span-2">
          <CardHeader title="Get started" description={`${steps.filter((s) => s.done).length} of ${steps.length} done`} />
          <ul className="divide-y divide-border">
            {steps.map((step) => (
              <li key={step.title}>
                <Link href={step.href} className="flex items-center gap-3 px-5 py-4 hover:bg-surface-muted">
                  {step.done ? <CheckCircle2 className="size-5 text-success" aria-label="Done" /> : <Circle className="size-5 text-muted" aria-label="To do" />}
                  <span className="flex-1">
                    <span className="block text-sm font-medium">{step.title}</span>
                    <span className="block text-sm text-muted">{step.detail}</span>
                  </span>
                </Link>
              </li>
            ))}
          </ul>
        </Card>
        <Card>
          <CardHeader title="Organization" />
          <dl className="space-y-3 px-5 py-4 text-sm">
            <div className="flex justify-between gap-4">
              <dt className="text-muted">Name</dt>
              <dd className="font-medium">{organization.data?.name}</dd>
            </div>
            <div className="flex justify-between gap-4">
              <dt className="text-muted">Plan</dt>
              <dd>
                <Badge tone="brand">{organization.data?.plan}</Badge>
              </dd>
            </div>
            <div className="flex justify-between gap-4">
              <dt className="text-muted">Your role</dt>
              <dd className="capitalize">{session.current!.role}</dd>
            </div>
          </dl>
        </Card>
      </div>
    </>
  );
}

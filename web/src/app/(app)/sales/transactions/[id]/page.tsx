import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { ArrowLeft } from "lucide-react";
import { ReverseButton } from "@/components/sales/reverse-button";
import { SaleStatus } from "@/components/sales/status";
import { Card, CardHeader } from "@/components/ui/card";
import { getSession, serverApi } from "@/lib/api/server";
import { formatAmount, formatDate, formatRelative } from "@/lib/format";

export const metadata: Metadata = { title: "Transaction" };

export default async function TransactionPage({ params }: PageProps<"/sales/transactions/[id]">) {
  const { id } = await params;
  const transactionId = decodeURIComponent(id);
  const session = (await getSession())!;
  const api = await serverApi();
  const { data } = await api.GET("/api/v1/ledger/transactions/{transactionId}", { params: { path: { transactionId } } });
  if (!data) notFound();
  const t = data.transaction;
  const now = new Date();

  return (
    <>
      <Link href="/sales/transactions" className="mb-4 inline-flex items-center gap-1 text-sm text-muted hover:text-foreground">
        <ArrowLeft className="size-4" aria-hidden /> Transactions
      </Link>
      <div className="mb-6 flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <div className="flex flex-wrap items-center gap-3">
            <h1 className="font-mono text-2xl font-semibold tracking-tight">{t.transactionId}</h1>
            <SaleStatus status={t.status} offline={t.offline} />
          </div>
          <p className="mt-1 text-sm text-muted">
            {formatDate(t.createdAt)} · {formatRelative(t.createdAt, now)} · recorded by {t.client}
          </p>
        </div>
        {session.current!.role !== "member" && t.status === "confirmed" ? <ReverseButton transactionId={t.transactionId} /> : null}
      </div>

      <div className="grid gap-6 lg:grid-cols-3">
        <Card className="lg:col-span-2">
          <CardHeader title="Campaign discounts" description="As recorded by the ledger when the sale was redeemed" />
          <table className="w-full text-sm">
            <thead className="border-b border-border text-left text-xs text-muted">
              <tr>
                <th className="px-5 py-2.5 font-medium">Campaign</th>
                <th className="px-5 py-2.5 font-medium">Coupon</th>
                <th className="px-5 py-2.5 text-right font-medium">Discount</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {data.redemptions.map((r) => (
                <tr key={r.campaignId}>
                  <td className="px-5 py-3">
                    <Link href={`/campaigns/${r.campaignId}`} className="font-medium hover:text-brand hover:underline">
                      {r.campaignName ?? r.campaignCode}
                    </Link>
                    <p className="font-mono text-xs text-muted">{r.campaignCode}</p>
                  </td>
                  <td className="px-5 py-3 font-mono text-xs">{r.couponCode ?? "—"}</td>
                  <td className="px-5 py-3 text-right tabular-nums">{formatAmount(r.discount)}</td>
                </tr>
              ))}
            </tbody>
            <tfoot className="border-t border-border font-medium">
              <tr>
                <td className="px-5 py-3" colSpan={2}>
                  Total discount
                </td>
                <td className="px-5 py-3 text-right tabular-nums">
                  {formatAmount(t.totalDiscount)} {t.currency}
                </td>
              </tr>
            </tfoot>
          </table>
        </Card>

        <Card>
          <CardHeader title="Sale" />
          <dl className="space-y-2 px-5 py-4 text-sm">
            <Row label="Channel" value={t.channel || "—"} />
            <Row label="Store" value={t.storeId ?? "—"} />
            <Row label="Customer" value={t.customerId ?? "Anonymous"} />
            <Row label="Currency" value={t.currency} />
            <Row label="Source" value={t.offline ? "Offline import (priced locally)" : "Priced by the API"} />
            {t.reversedAt ? <Row label="Reversed" value={`${formatDate(t.reversedAt)}${data.reverseReason ? ` — ${data.reverseReason}` : ""}`} /> : null}
          </dl>
        </Card>
      </div>
    </>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between gap-4">
      <dt className="text-muted">{label}</dt>
      <dd className="text-right">{value}</dd>
    </div>
  );
}

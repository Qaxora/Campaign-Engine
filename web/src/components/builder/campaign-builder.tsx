"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { Plus, Trash2 } from "lucide-react";
import { ConflictList } from "@/components/campaigns/conflict-list";
import { Button, buttonClass } from "@/components/ui/button";
import { Dialog } from "@/components/ui/dialog";
import { Alert } from "@/components/ui/form";
import { api } from "@/lib/api/client";
import type { components } from "@/lib/api/schema";
import {
  conditionTypes,
  days,
  emptyCondition,
  emptyReward,
  rewardTypes,
  toDefinition,
  type BuilderForm,
  type ConditionForm,
  type ConditionType,
  type RewardForm,
  type RewardType,
} from "@/lib/builder/model";
import { isValidTimeZone } from "@/lib/builder/time";
import { problemMessage } from "@/lib/problem";
import { cn } from "@/lib/utils";
import { CheckboxField, ChipInput, NumberField, Section, SelectField, TextField } from "./fields";
import { ReviewPanel, useReview } from "./preview";

type Conflict = components["schemas"]["CampaignConflict"];

export interface BuilderCatalog {
  categories: { value: string; label: string }[];
  brands: { value: string; label: string }[];
  productLists: { value: string; label: string }[];
  stores: { value: string; label: string }[];
  /** Descriptions of kept-as-is conditions, keyed by their JSON. */
  advanced: Record<string, string>;
}

const channelSuggestions = ["store", "web", "mobile", "callCenter", "marketplace"].map((value) => ({ value }));
const zones = ["Europe/Istanbul", "UTC", "Europe/London", "Europe/Berlin", "Asia/Dubai", "America/New_York"];
const sections = [
  ["basics", "Basics"],
  ["schedule", "Schedule"],
  ["audience", "Audience"],
  ["products", "Products"],
  ["conditions", "Conditions"],
  ["reward", "Reward"],
  ["limits", "Limits"],
  ["combining", "Combining"],
] as const;

export function CampaignBuilder({ initial, catalog }: { initial: BuilderForm; catalog: BuilderCatalog }) {
  const router = useRouter();
  const [form, setForm] = useState(initial);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string>();
  const [blocking, setBlocking] = useState<Conflict[]>();
  const [acknowledged, setAcknowledged] = useState(false);
  const editing = Boolean(form.id);

  const definition = useMemo(() => toDefinition(form), [form]);
  const review = useReview(definition);

  const set = <K extends keyof BuilderForm>(key: K) => (value: BuilderForm[K]) => setForm((f) => ({ ...f, [key]: value }));
  const setReward = (change: Partial<RewardForm>) => setForm((f) => ({ ...f, reward: { ...f.reward, ...change } }));
  const setCondition = (key: string, change: Partial<ConditionForm>) =>
    setForm((f) => ({ ...f, conditions: f.conditions.map((c) => (c.key === key ? ({ ...c, ...change } as ConditionForm) : c)) }));

  async function save(force = false) {
    setSaving(true);
    setSaveError(undefined);
    const result = editing
      ? await api.PUT("/api/v1/campaigns/{id}", { params: { path: { id: form.id! }, query: { force } }, body: definition })
      : await api.POST("/api/v1/campaigns", { body: definition });
    setSaving(false);

    if (!result.response.ok) {
      const body = result.error as { conflicts?: Conflict[] } | undefined;
      if (result.response.status === 409 && body?.conflicts) {
        setBlocking(body.conflicts);
        setAcknowledged(false);
        return;
      }

      setSaveError(
        result.response.status === 409
          ? `${problemMessage(body, 409)} Reload the page to edit the latest version.`
          : problemMessage(body, result.response.status),
      );
      return;
    }

    const saved = (result.data as { id?: string; campaign?: { id?: string } }) ?? {};
    const id = saved.campaign?.id ?? saved.id ?? form.id;
    router.push(`/campaigns/${id}`);
    router.refresh();
  }

  return (
    <div className="grid gap-6 xl:grid-cols-[1fr_22rem]">
      <div className="space-y-6">
        <nav aria-label="Builder sections" className="flex flex-wrap gap-1 text-sm">
          {sections.map(([id, label]) => (
            <a key={id} href={`#${id}`} className="rounded-md px-2.5 py-1 text-muted hover:bg-surface-muted hover:text-foreground">
              {label}
            </a>
          ))}
        </nav>

        <Section id="basics" title="Basics" description="How the campaign is identified in carts, receipts and reports.">
          <TextField label="Name" value={form.name} onChange={set("name")} placeholder="October 20% over 1000" />
          <TextField label="Code" value={form.code} onChange={(v) => set("code")(v.toUpperCase())} mono placeholder="OCT-20" hint={editing ? "Changing the code breaks references in other systems." : "Unique; letters, digits, - _ ."} />
          <TextField label="Description" value={form.description} onChange={set("description")} wide />
          <TextField label="Receipt / basket message" value={form.displayMessage} onChange={set("displayMessage")} wide hint="Shown to customers with the discount." />
          <ChipInput label="Tags" values={form.tags} onChange={set("tags")} placeholder="autumn, clearance…" wide />
        </Section>

        <Section id="schedule" title="Schedule" description="Leave start and end empty for a campaign that runs until it is paused.">
          <TextField label="Starts" type="datetime-local" value={form.startsAt} onChange={set("startsAt")} />
          <TextField label="Ends (exclusive)" type="datetime-local" value={form.endsAt} onChange={set("endsAt")} />
          <div className="flex flex-col gap-1.5">
            <TextField label="Time zone" value={form.timeZone} onChange={set("timeZone")} hint={isValidTimeZone(form.timeZone) ? "Start, end, days and hours are read in this zone." : "Unknown time zone."} />
            <div className="flex flex-wrap gap-1">
              {zones.map((z) => (
                <button key={z} type="button" onClick={() => set("timeZone")(z)} className={cn("rounded px-1.5 py-0.5 text-xs", form.timeZone === z ? "bg-brand-soft text-brand" : "text-muted hover:bg-surface-muted")}>
                  {z}
                </button>
              ))}
            </div>
          </div>
          <fieldset className="flex flex-col gap-1.5">
            <legend className="mb-1.5 text-sm font-medium">Days</legend>
            <div className="flex flex-wrap gap-1">
              {days.map((d) => {
                const on = form.daysOfWeek.includes(d);
                return (
                  <button
                    key={d}
                    type="button"
                    aria-pressed={on}
                    onClick={() => set("daysOfWeek")(on ? form.daysOfWeek.filter((x) => x !== d) : [...form.daysOfWeek, d])}
                    className={cn("rounded-md border px-2 py-1 text-xs capitalize", on ? "border-brand bg-brand-soft text-brand" : "border-border text-muted hover:text-foreground")}
                  >
                    {d.slice(0, 3)}
                  </button>
                );
              })}
            </div>
            <p className="text-xs text-muted">None selected = every day.</p>
          </fieldset>
          <TextField label="Daily from" type="time" value={form.dailyStart} onChange={set("dailyStart")} />
          <TextField label="Daily until" type="time" value={form.dailyEnd} onChange={set("dailyEnd")} hint="May cross midnight, e.g. 22:00–02:00." />
        </Section>

        <Section id="audience" title="Audience" description="Empty lists mean everyone: all channels, all stores, every customer.">
          <ChipInput label="Channels" values={form.channels} onChange={set("channels")} suggestions={channelSuggestions} placeholder="All channels" />
          <TextField label="Currency" value={form.currency} onChange={(v) => set("currency")(v.toUpperCase())} placeholder="Any" hint="Only carts in this currency." mono />
          <ChipInput label="Stores" values={form.storesInclude} onChange={set("storesInclude")} suggestions={catalog.stores} placeholder="All stores" upper />
          <ChipInput label="Except stores" values={form.storesExclude} onChange={set("storesExclude")} suggestions={catalog.stores} upper />
          <ChipInput label="Customer segments" values={form.customerSegments} onChange={set("customerSegments")} placeholder="Every customer" hint="Segment codes your channels send with the customer." />
          <ChipInput label="Coupon codes" values={form.couponCodes} onChange={set("couponCodes")} placeholder="No coupon needed" upper hint="The cart must carry one of these codes." />
          {form.couponCodes.length > 0 ? <NumberField label="Uses per code" value={form.maxUsesPerCode} onChange={set("maxUsesPerCode")} hint="1 for single-use codes; empty for unlimited." /> : null}
        </Section>

        <Section id="products" title="Products" description="What the reward applies to. With no product filter, every discountable product qualifies.">
          <ChipInput label="Categories" values={form.categories} onChange={set("categories")} suggestions={catalog.categories} placeholder="Any category" />
          <ChipInput label="Brands" values={form.brands} onChange={set("brands")} suggestions={catalog.brands} placeholder="Any brand" />
          <ChipInput label="Product lists" values={form.productLists} onChange={set("productLists")} suggestions={catalog.productLists} upper />
          <ChipInput label="SKUs" values={form.skus} onChange={set("skus")} placeholder="Paste SKUs, comma separated" />
          <ChipInput label="Except categories" values={form.excludeCategories} onChange={set("excludeCategories")} suggestions={catalog.categories} />
          <ChipInput label="Except brands" values={form.excludeBrands} onChange={set("excludeBrands")} suggestions={catalog.brands} />
          <ChipInput label="Except product lists" values={form.excludeProductLists} onChange={set("excludeProductLists")} suggestions={catalog.productLists} upper />
          <ChipInput label="Except SKUs" values={form.excludeSkus} onChange={set("excludeSkus")} />
          <CheckboxField
            label="Also discount globally excluded products"
            checked={form.ignoreGlobalExclusions}
            onChange={set("ignoreGlobalExclusions")}
            hint="Leave off unless you are sure: global exclusion lists protect regulated and never-discount products."
            wide
          />
        </Section>

        <section id="conditions" aria-labelledby="conditions-title" className="scroll-mt-20 rounded-lg border border-border bg-surface">
          <div className="flex flex-wrap items-center justify-between gap-3 border-b border-border px-5 py-4">
            <div>
              <h2 id="conditions-title" className="text-sm font-semibold">
                Conditions
              </h2>
              <p className="mt-0.5 text-sm text-muted">All must hold. Amounts and quantities count the qualifying products.</p>
            </div>
            <select
              aria-label="Add condition"
              value=""
              onChange={(e) => e.target.value && set("conditions")([...form.conditions, emptyCondition(e.target.value as ConditionType)])}
              className="h-9 rounded-md border border-border bg-surface px-2 text-sm"
            >
              <option value="">+ Add condition</option>
              {conditionTypes.map((c) => (
                <option key={c.type} value={c.type}>
                  {c.label}
                </option>
              ))}
            </select>
          </div>
          {form.conditions.length === 0 ? (
            <p className="px-5 py-4 text-sm text-muted">No conditions: every cart with qualifying products gets the reward.</p>
          ) : (
            <ul className="divide-y divide-border">
              {form.conditions.map((c) => (
                <li key={c.key} className="flex gap-3 px-5 py-4">
                  <div className="grid flex-1 gap-4 sm:grid-cols-2">
                    <ConditionFields condition={c} advanced={catalog.advanced} onChange={(change) => setCondition(c.key, change)} />
                  </div>
                  <button type="button" aria-label="Remove condition" onClick={() => set("conditions")(form.conditions.filter((x) => x.key !== c.key))} className="self-start rounded p-1.5 text-muted hover:bg-surface-muted hover:text-danger">
                    <Trash2 className="size-4" />
                  </button>
                </li>
              ))}
            </ul>
          )}
        </section>

        <Section id="reward" title="Reward" description="Exactly one reward per campaign.">
          <SelectField
            label="Reward type"
            value={form.reward.type}
            options={rewardTypes.map((r) => ({ value: r.type, label: r.label }))}
            onChange={(type: RewardType) => set("reward")(emptyReward(type))}
            wide
          />
          <RewardFields reward={form.reward} onChange={setReward} />
        </Section>

        <Section id="limits" title="Limits" description="Empty means no limit. Limits are enforced by the ledger, also under concurrent sales.">
          <NumberField label="Total redemptions" value={form.maxRedemptions} onChange={set("maxRedemptions")} />
          <NumberField label="Redemptions per customer" value={form.maxRedemptionsPerCustomer} onChange={set("maxRedemptionsPerCustomer")} hint="Only identified customers are counted." />
          <NumberField label="Budget" value={form.budget} onChange={set("budget")} hint="Total discount the campaign may give." />
          <NumberField label="Max discount per order" value={form.maxDiscountPerOrder} onChange={set("maxDiscountPerOrder")} />
        </Section>

        <Section id="combining" title="Combining" description="How this campaign interacts with the others.">
          <SelectField
            label="Stacking"
            value={form.stacking}
            options={[
              { value: "stackable", label: "Stackable — combines with other stackable campaigns" },
              { value: "exclusive", label: "Exclusive — never combined; the better offer wins" },
            ]}
            onChange={set("stacking")}
            wide
          />
          <NumberField label="Priority" value={form.priority} onChange={set("priority")} hint="Higher runs first and wins ties." />
          <TextField label="Exclusivity group" value={form.exclusivityGroup} onChange={set("exclusivityGroup")} hint="Only one campaign of a group applies." />
        </Section>
      </div>

      <aside className="space-y-4 xl:sticky xl:top-20 xl:self-start">
        <ReviewPanel review={review} />
        {saveError ? <Alert>{saveError}</Alert> : null}
        <div className="flex gap-2">
          <Button className="flex-1" disabled={!review.valid || review.pending || saving} onClick={() => save(false)}>
            {saving ? "Saving…" : editing ? "Save changes" : "Save as draft"}
          </Button>
          <Link href={editing ? `/campaigns/${form.id}` : "/campaigns"} className={buttonClass("secondary")}>
            Cancel
          </Link>
        </div>
        <p className="text-xs text-muted">
          {editing
            ? "Saving a live campaign re-checks its conflicts. Status is changed on the campaign page."
            : "New campaigns are saved as drafts. You activate them on the campaign page after reviewing conflicts."}
        </p>
      </aside>

      <Dialog
        open={blocking !== undefined}
        title="Save blocked by conflicts"
        onClose={() => setBlocking(undefined)}
        footer={
          <>
            <Button variant="secondary" onClick={() => setBlocking(undefined)}>
              Keep editing
            </Button>
            <Button
              disabled={!acknowledged || saving}
              onClick={async () => {
                setBlocking(undefined);
                await save(true);
              }}
            >
              Save anyway
            </Button>
          </>
        }
      >
        <p className="px-5 pt-4 text-sm text-muted">This campaign is live, and the change conflicts with other live campaigns:</p>
        <ConflictList conflicts={blocking ?? []} />
        <label className="flex items-start gap-2 border-t border-border px-5 py-3 text-sm">
          <input type="checkbox" className="mt-0.5" checked={acknowledged} onChange={(e) => setAcknowledged(e.target.checked)} />
          I reviewed the blocking conflicts and want to save anyway.
        </label>
      </Dialog>
    </div>
  );
}

function RewardFields({ reward: r, onChange }: { reward: RewardForm; onChange: (change: Partial<RewardForm>) => void }) {
  switch (r.type) {
    case "percentageDiscount":
      return (
        <>
          <NumberField label="Percent off" value={r.percent} onChange={(percent) => onChange({ percent })} placeholder="20" />
          <NumberField label="Maximum discount" value={r.maxDiscount} onChange={(maxDiscount) => onChange({ maxDiscount })} hint="Optional cap per order." />
        </>
      );
    case "amountDiscount":
      return (
        <>
          <NumberField label="Amount off" value={r.amount} onChange={(amount) => onChange({ amount })} placeholder="100" />
          <CheckboxField label="Per unit" checked={r.perUnit} onChange={(perUnit) => onChange({ perUnit })} hint="Off every qualifying unit instead of once per order." />
        </>
      );
    case "fixedUnitPrice":
      return <NumberField label="Price per unit" value={r.price} onChange={(price) => onChange({ price })} placeholder="99.90" />;
    case "buyXGetY":
      return (
        <>
          <NumberField label="Buy" value={r.buyQuantity} onChange={(buyQuantity) => onChange({ buyQuantity })} />
          <NumberField label="Get" value={r.getQuantity} onChange={(getQuantity) => onChange({ getQuantity })} />
          <NumberField label="Discount on the cheapest (%)" value={r.discountPercent} onChange={(discountPercent) => onChange({ discountPercent })} hint="100 = free; 50 = second item half price." />
          <NumberField label="Max per order" value={r.maxApplications} onChange={(maxApplications) => onChange({ maxApplications })} hint="Optional." />
        </>
      );
    case "bundlePrice":
      return (
        <>
          <NumberField label="Any (quantity)" value={r.quantity} onChange={(quantity) => onChange({ quantity })} />
          <NumberField label="For (price)" value={r.price} onChange={(price) => onChange({ price })} />
          <NumberField label="Max bundles per order" value={r.maxApplications} onChange={(maxApplications) => onChange({ maxApplications })} hint="Optional." />
        </>
      );
    case "tieredDiscount":
      return (
        <>
          <SelectField
            label="Tiers by"
            value={r.basis}
            options={[
              { value: "subtotal", label: "Cart amount" },
              { value: "quantity", label: "Quantity" },
            ]}
            onChange={(basis) => onChange({ basis })}
            wide
          />
          <div className="space-y-2 sm:col-span-2">
            {r.tiers.map((tier, i) => (
              <div key={i} className="grid grid-cols-[1fr_8rem_1fr_auto] items-end gap-2">
                <NumberField label={r.basis === "quantity" ? "From quantity" : "From amount"} value={tier.threshold} onChange={(threshold) => onChange({ tiers: r.tiers.map((t, j) => (j === i ? { ...t, threshold } : t)) })} />
                <SelectField
                  label="Discount"
                  value={tier.kind}
                  options={[
                    { value: "percent", label: "% off" },
                    { value: "amount", label: "Amount off" },
                  ]}
                  onChange={(kind) => onChange({ tiers: r.tiers.map((t, j) => (j === i ? { ...t, kind } : t)) })}
                />
                <NumberField label="Value" value={tier.value} onChange={(value) => onChange({ tiers: r.tiers.map((t, j) => (j === i ? { ...t, value } : t)) })} />
                <button type="button" aria-label="Remove tier" disabled={r.tiers.length === 1} onClick={() => onChange({ tiers: r.tiers.filter((_, j) => j !== i) })} className="mb-2 rounded p-1.5 text-muted hover:text-danger disabled:opacity-40">
                  <Trash2 className="size-4" />
                </button>
              </div>
            ))}
            <button type="button" onClick={() => onChange({ tiers: [...r.tiers, { threshold: "", kind: "percent", value: "" }] })} className="inline-flex items-center gap-1 text-sm text-brand hover:underline">
              <Plus className="size-4" aria-hidden /> Add tier
            </button>
          </div>
        </>
      );
    case "freeShipping":
      return <NumberField label="Maximum shipping covered" value={r.maxDiscount} onChange={(maxDiscount) => onChange({ maxDiscount })} hint="Optional; empty covers all of it." />;
    case "giftProduct":
      return (
        <>
          <TextField label="Gift SKU" value={r.sku} onChange={(sku) => onChange({ sku })} mono />
          <NumberField label="Quantity" value={r.quantity} onChange={(quantity) => onChange({ quantity })} />
        </>
      );
  }
}

function ConditionFields({ condition: c, advanced, onChange }: { condition: ConditionForm; advanced: Record<string, string>; onChange: (change: Partial<ConditionForm>) => void }) {
  const label = conditionTypes.find((t) => t.type === c.type)?.label;
  switch (c.type) {
    case "minSubtotal":
      return <NumberField label={label!} value={c.amount} onChange={(amount) => onChange({ amount })} placeholder="1000" hint="Qualifying products must total at least this." />;
    case "minQuantity":
      return <NumberField label={label!} value={c.quantity} onChange={(quantity) => onChange({ quantity })} placeholder="3" />;
    case "firstOrder":
      return <p className="text-sm sm:col-span-2">Customer&apos;s first order (the channel reports it with the customer).</p>;
    case "payment":
      return (
        <>
          <ChipInput label="Payment methods" values={c.methods} onChange={(methods) => onChange({ methods })} suggestions={[{ value: "creditCard" }, { value: "debitCard" }, { value: "cash" }, { value: "giftCard" }]} placeholder="Any" />
          <ChipInput label="Bank codes" values={c.bankCodes} onChange={(bankCodes) => onChange({ bankCodes })} placeholder="Any bank" />
          <ChipInput label="Card BIN prefixes" values={c.cardBins} onChange={(cardBins) => onChange({ cardBins })} placeholder="Any card" />
          <div className="grid grid-cols-2 gap-2">
            <NumberField label="Min installments" value={c.minInstallments} onChange={(minInstallments) => onChange({ minInstallments })} />
            <NumberField label="Max installments" value={c.maxInstallments} onChange={(maxInstallments) => onChange({ maxInstallments })} />
          </div>
        </>
      );
    case "cartAttribute":
      return (
        <>
          <TextField label="Cart attribute" value={c.attribute} onChange={(attribute) => onChange({ attribute })} placeholder="deliveryType" mono />
          <ChipInput label="Is one of" values={c.values} onChange={(values) => onChange({ values })} placeholder="clickAndCollect" />
        </>
      );
    case "advanced":
      return (
        <div className="sm:col-span-2">
          <p className="text-sm">{advanced[JSON.stringify(c.value)] ?? "Advanced condition"}</p>
          <p className="text-xs text-muted">Combined or product-scoped condition — kept exactly as defined. Remove it to replace it.</p>
        </div>
      );
  }
}

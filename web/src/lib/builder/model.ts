import type { components } from "@/lib/api/schema";
import { toNumber } from "@/lib/format";
import { isoToZoned, zonedToIso } from "./time";

/**
 * Form state of the visual builder and its mapping to the campaign definition the API accepts.
 * This is serialization only: no rule is evaluated here. Validation, conflicts and descriptions come
 * from the API (/validate, /conflicts, /describe).
 */
export type Campaign = components["schemas"]["Campaign"];
type Condition = components["schemas"]["Condition"];

export const rewardTypes = [
  { type: "percentageDiscount", label: "Percentage off" },
  { type: "amountDiscount", label: "Amount off" },
  { type: "fixedUnitPrice", label: "Fixed unit price" },
  { type: "buyXGetY", label: "Buy X get Y" },
  { type: "bundlePrice", label: "Bundle price" },
  { type: "tieredDiscount", label: "Tiered discount" },
  { type: "freeShipping", label: "Free shipping" },
  { type: "giftProduct", label: "Gift product" },
] as const;
export type RewardType = (typeof rewardTypes)[number]["type"];

export const conditionTypes = [
  { type: "minSubtotal", label: "Minimum cart amount" },
  { type: "minQuantity", label: "Minimum quantity" },
  { type: "firstOrder", label: "First order" },
  { type: "payment", label: "Payment method / card" },
  { type: "cartAttribute", label: "Cart attribute" },
] as const;
export type ConditionType = (typeof conditionTypes)[number]["type"];

export const days = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"] as const;

export interface TierForm {
  threshold: string;
  kind: "percent" | "amount";
  value: string;
}

export interface RewardForm {
  type: RewardType;
  percent: string;
  maxDiscount: string;
  amount: string;
  perUnit: boolean;
  price: string;
  buyQuantity: string;
  getQuantity: string;
  discountPercent: string;
  maxApplications: string;
  quantity: string;
  basis: "subtotal" | "quantity";
  tiers: TierForm[];
  sku: string;
}

export type ConditionForm =
  | { key: string; type: "minSubtotal"; amount: string }
  | { key: string; type: "minQuantity"; quantity: string }
  | { key: string; type: "firstOrder" }
  | { key: string; type: "payment"; methods: string[]; bankCodes: string[]; cardBins: string[]; minInstallments: string; maxInstallments: string }
  | { key: string; type: "cartAttribute"; attribute: string; values: string[] }
  /** Composite (allOf / anyOf / not) conditions are kept as they are; the builder shows their description. */
  | { key: string; type: "advanced"; value: Condition };

export interface BuilderForm {
  id?: string;
  version?: number;
  status?: Campaign["status"];
  code: string;
  name: string;
  description: string;
  displayMessage: string;
  tags: string[];
  // schedule
  startsAt: string;
  endsAt: string;
  timeZone: string;
  daysOfWeek: string[];
  dailyStart: string;
  dailyEnd: string;
  // audience
  channels: string[];
  storesInclude: string[];
  storesExclude: string[];
  customerSegments: string[];
  couponCodes: string[];
  maxUsesPerCode: string;
  currency: string;
  // products
  categories: string[];
  brands: string[];
  skus: string[];
  productLists: string[];
  excludeCategories: string[];
  excludeBrands: string[];
  excludeSkus: string[];
  excludeProductLists: string[];
  ignoreGlobalExclusions: boolean;
  // rules
  conditions: ConditionForm[];
  reward: RewardForm;
  // limits and combining
  maxRedemptions: string;
  maxRedemptionsPerCustomer: string;
  budget: string;
  maxDiscountPerOrder: string;
  priority: string;
  stacking: "stackable" | "exclusive";
  exclusivityGroup: string;
  /** Fields the builder does not edit (product attributes, metadata) survive a round trip. */
  preserved: Pick<Campaign, "metadata"> & { attributes?: Record<string, string[]> };
}

let keySeed = 0;
export const newKey = () => `c${++keySeed}`;

export function emptyReward(type: RewardType = "percentageDiscount"): RewardForm {
  return {
    type,
    percent: "",
    maxDiscount: "",
    amount: "",
    perUnit: false,
    price: "",
    buyQuantity: "2",
    getQuantity: "1",
    discountPercent: "100",
    maxApplications: "",
    quantity: "3",
    basis: "subtotal",
    tiers: [{ threshold: "", kind: "percent", value: "" }],
    sku: "",
  };
}

export function emptyCondition(type: ConditionType): ConditionForm {
  const key = newKey();
  switch (type) {
    case "minSubtotal":
      return { key, type, amount: "" };
    case "minQuantity":
      return { key, type, quantity: "" };
    case "firstOrder":
      return { key, type };
    case "payment":
      return { key, type, methods: [], bankCodes: [], cardBins: [], minInstallments: "", maxInstallments: "" };
    case "cartAttribute":
      return { key, type, attribute: "", values: [] };
  }
}

export function emptyForm(timeZone = "Europe/Istanbul"): BuilderForm {
  return {
    code: "",
    name: "",
    description: "",
    displayMessage: "",
    tags: [],
    startsAt: "",
    endsAt: "",
    timeZone,
    daysOfWeek: [],
    dailyStart: "",
    dailyEnd: "",
    channels: [],
    storesInclude: [],
    storesExclude: [],
    customerSegments: [],
    couponCodes: [],
    maxUsesPerCode: "",
    currency: "TRY",
    categories: [],
    brands: [],
    skus: [],
    productLists: [],
    excludeCategories: [],
    excludeBrands: [],
    excludeSkus: [],
    excludeProductLists: [],
    ignoreGlobalExclusions: false,
    conditions: [],
    reward: emptyReward(),
    maxRedemptions: "",
    maxRedemptionsPerCustomer: "",
    budget: "",
    maxDiscountPerOrder: "",
    priority: "0",
    stacking: "stackable",
    exclusivityGroup: "",
    preserved: {},
  };
}

/** Empty input → omitted; anything else is sent as typed so the API can report invalid numbers. */
function num(value: string): number | string | undefined {
  const text = value.trim();
  if (text === "") return undefined;
  const n = Number(text.replace(",", "."));
  return Number.isFinite(n) ? n : text;
}

const str = (value: number | string | null | undefined) => (value === null || value === undefined ? "" : String(value));
const text = (value: string) => (value.trim() === "" ? null : value.trim());

function toReward(r: RewardForm): Record<string, unknown> {
  switch (r.type) {
    case "percentageDiscount":
      return { type: r.type, percent: num(r.percent), maxDiscount: num(r.maxDiscount) ?? null };
    case "amountDiscount":
      return { type: r.type, amount: num(r.amount), perUnit: r.perUnit };
    case "fixedUnitPrice":
      return { type: r.type, price: num(r.price) };
    case "buyXGetY":
      return {
        type: r.type,
        buyQuantity: num(r.buyQuantity),
        getQuantity: num(r.getQuantity),
        discountPercent: num(r.discountPercent) ?? 100,
        maxApplications: num(r.maxApplications) ?? null,
      };
    case "bundlePrice":
      return { type: r.type, quantity: num(r.quantity), price: num(r.price), maxApplications: num(r.maxApplications) ?? null };
    case "tieredDiscount":
      return {
        type: r.type,
        basis: r.basis,
        tiers: r.tiers.map((t) => ({ threshold: num(t.threshold), percent: t.kind === "percent" ? num(t.value) : null, amount: t.kind === "amount" ? num(t.value) : null })),
      };
    case "freeShipping":
      return { type: r.type, maxAmount: num(r.maxDiscount) ?? null };
    case "giftProduct":
      return { type: r.type, sku: r.sku.trim(), quantity: num(r.quantity) ?? 1 };
  }
}

function toCondition(c: ConditionForm): Condition {
  switch (c.type) {
    case "minSubtotal":
      return { type: c.type, amount: num(c.amount) } as Condition;
    case "minQuantity":
      return { type: c.type, quantity: num(c.quantity) } as Condition;
    case "firstOrder":
      return { type: c.type } as Condition;
    case "payment":
      return {
        type: c.type,
        methods: c.methods,
        bankCodes: c.bankCodes,
        cardBins: c.cardBins,
        minInstallments: num(c.minInstallments) ?? null,
        maxInstallments: num(c.maxInstallments) ?? null,
      } as Condition;
    case "cartAttribute":
      return { type: c.type, key: c.attribute.trim(), values: c.values } as Condition;
    case "advanced":
      return c.value;
  }
}

export function toDefinition(f: BuilderForm): Campaign {
  const coupon = f.couponCodes.length > 0 ? { codes: f.couponCodes, maxUsesPerCode: num(f.maxUsesPerCode) ?? null } : null;
  const definition = {
    id: f.id,
    version: f.version,
    code: f.code.trim(),
    name: f.name.trim(),
    description: text(f.description),
    displayMessage: text(f.displayMessage),
    tags: f.tags,
    priority: num(f.priority) ?? 0,
    stacking: f.stacking,
    exclusivityGroup: text(f.exclusivityGroup),
    currency: text(f.currency)?.toUpperCase() ?? null,
    schedule: {
      startsAt: zonedToIso(f.startsAt, f.timeZone),
      endsAt: zonedToIso(f.endsAt, f.timeZone),
      timeZone: f.timeZone,
      daysOfWeek: f.daysOfWeek,
      dailyStart: f.dailyStart || null,
      dailyEnd: f.dailyEnd || null,
    },
    channels: f.channels,
    stores: { include: f.storesInclude, exclude: f.storesExclude },
    customerSegments: f.customerSegments,
    coupon,
    target: {
      categories: f.categories,
      brands: f.brands,
      skus: f.skus,
      productLists: f.productLists,
      attributes: f.preserved.attributes ?? {},
      excludeCategories: f.excludeCategories,
      excludeBrands: f.excludeBrands,
      excludeSkus: f.excludeSkus,
      excludeProductLists: f.excludeProductLists,
    },
    conditions: f.conditions.map(toCondition),
    reward: toReward(f.reward),
    limits: {
      maxRedemptions: num(f.maxRedemptions) ?? null,
      maxRedemptionsPerCustomer: num(f.maxRedemptionsPerCustomer) ?? null,
      budget: num(f.budget) ?? null,
      maxDiscountPerOrder: num(f.maxDiscountPerOrder) ?? null,
    },
    ignoreGlobalExclusions: f.ignoreGlobalExclusions,
    metadata: f.preserved.metadata ?? {},
  };
  return definition as unknown as Campaign;
}

function fromReward(reward: Campaign["reward"]): RewardForm {
  const r = reward as Record<string, unknown> & { type: RewardType };
  const form = emptyReward(r.type);
  const s = (key: string) => str(r[key] as number | string | null | undefined);
  switch (r.type) {
    case "percentageDiscount":
      return { ...form, percent: s("percent"), maxDiscount: s("maxDiscount") };
    case "amountDiscount":
      return { ...form, amount: s("amount"), perUnit: Boolean(r.perUnit) };
    case "fixedUnitPrice":
      return { ...form, price: s("price") };
    case "buyXGetY":
      return { ...form, buyQuantity: s("buyQuantity"), getQuantity: s("getQuantity"), discountPercent: s("discountPercent") || "100", maxApplications: s("maxApplications") };
    case "bundlePrice":
      return { ...form, quantity: s("quantity"), price: s("price"), maxApplications: s("maxApplications") };
    case "tieredDiscount": {
      const tiers = (r.tiers as components["schemas"]["Tier"][] | undefined) ?? [];
      return {
        ...form,
        basis: (r.basis as "subtotal" | "quantity") ?? "subtotal",
        tiers: tiers.map((t) => (t.percent != null ? { threshold: str(t.threshold), kind: "percent", value: str(t.percent) } : { threshold: str(t.threshold), kind: "amount", value: str(t.amount) })),
      };
    }
    case "freeShipping":
      return { ...form, maxDiscount: s("maxAmount") };
    case "giftProduct":
      return { ...form, sku: s("sku"), quantity: s("quantity") || "1" };
    default:
      return form;
  }
}

function fromCondition(condition: Condition): ConditionForm {
  const c = condition as Record<string, unknown> & { type: string };
  const key = newKey();
  const list = (k: string) => (c[k] as string[] | undefined) ?? [];
  switch (c.type) {
    case "minSubtotal":
      if (c.products) break;
      return { key, type: "minSubtotal", amount: str(c.amount as number) };
    case "minQuantity":
      if (c.products) break;
      return { key, type: "minQuantity", quantity: str(c.quantity as number) };
    case "firstOrder":
      return { key, type: "firstOrder" };
    case "payment":
      return {
        key,
        type: "payment",
        methods: list("methods"),
        bankCodes: list("bankCodes"),
        cardBins: list("cardBins"),
        minInstallments: str(c.minInstallments as number | null),
        maxInstallments: str(c.maxInstallments as number | null),
      };
    case "cartAttribute":
      return { key, type: "cartAttribute", attribute: String(c.key ?? ""), values: list("values") };
  }

  // Composite conditions and product-scoped thresholds are not editable here; they are kept intact.
  return { key, type: "advanced", value: condition };
}

export function fromDefinition(c: Campaign): BuilderForm {
  const zone = c.schedule?.timeZone || "UTC";
  const t = c.target ?? {};
  return {
    id: c.id,
    version: c.version === undefined ? undefined : toNumber(c.version),
    status: c.status,
    code: c.code,
    name: c.name,
    description: c.description ?? "",
    displayMessage: c.displayMessage ?? "",
    tags: c.tags ?? [],
    startsAt: isoToZoned(c.schedule?.startsAt, zone),
    endsAt: isoToZoned(c.schedule?.endsAt, zone),
    timeZone: zone,
    daysOfWeek: c.schedule?.daysOfWeek ?? [],
    dailyStart: String(c.schedule?.dailyStart ?? "").slice(0, 5),
    dailyEnd: String(c.schedule?.dailyEnd ?? "").slice(0, 5),
    channels: c.channels ?? [],
    storesInclude: c.stores?.include ?? [],
    storesExclude: c.stores?.exclude ?? [],
    customerSegments: c.customerSegments ?? [],
    couponCodes: c.coupon?.codes ?? [],
    maxUsesPerCode: str(c.coupon?.maxUsesPerCode),
    currency: c.currency ?? "",
    categories: t.categories ?? [],
    brands: t.brands ?? [],
    skus: t.skus ?? [],
    productLists: t.productLists ?? [],
    excludeCategories: t.excludeCategories ?? [],
    excludeBrands: t.excludeBrands ?? [],
    excludeSkus: t.excludeSkus ?? [],
    excludeProductLists: t.excludeProductLists ?? [],
    ignoreGlobalExclusions: c.ignoreGlobalExclusions ?? false,
    conditions: (c.conditions ?? []).map(fromCondition),
    reward: fromReward(c.reward),
    maxRedemptions: str(c.limits?.maxRedemptions),
    maxRedemptionsPerCustomer: str(c.limits?.maxRedemptionsPerCustomer),
    budget: str(c.limits?.budget),
    maxDiscountPerOrder: str(c.limits?.maxDiscountPerOrder),
    priority: str(c.priority ?? 0),
    stacking: c.stacking ?? "stackable",
    exclusivityGroup: c.exclusivityGroup ?? "",
    preserved: { metadata: c.metadata, attributes: t.attributes },
  };
}

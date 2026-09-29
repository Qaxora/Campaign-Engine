import { describe, expect, it } from "vitest";
import { emptyCondition, emptyForm, emptyReward, fromDefinition, toDefinition, type Campaign } from "./model";

describe("builder model", () => {
  it("builds the campaign definition from the form", () => {
    const form = {
      ...emptyForm("Europe/Istanbul"),
      code: " OCT-20 ",
      name: "October 20%",
      startsAt: "2026-10-01T00:00",
      endsAt: "2026-11-01T00:00",
      daysOfWeek: ["saturday", "sunday"],
      channels: ["store", "web"],
      excludeCategories: ["electronics"],
      conditions: [{ ...emptyCondition("minSubtotal"), amount: "1000" } as never],
      reward: { ...emptyReward("percentageDiscount"), percent: "20", maxDiscount: "500" },
      budget: "50000",
      couponCodes: ["OCT20"],
      maxUsesPerCode: "1",
    };

    const d = JSON.parse(JSON.stringify(toDefinition(form)));

    expect(d.code).toBe("OCT-20");
    expect(d.currency).toBe("TRY");
    expect(d.schedule).toEqual({
      startsAt: "2026-10-01T00:00:00+03:00",
      endsAt: "2026-11-01T00:00:00+03:00",
      timeZone: "Europe/Istanbul",
      daysOfWeek: ["saturday", "sunday"],
      dailyStart: null,
      dailyEnd: null,
    });
    expect(d.target.excludeCategories).toEqual(["electronics"]);
    expect(d.conditions).toEqual([{ type: "minSubtotal", amount: 1000 }]);
    expect(d.reward).toEqual({ type: "percentageDiscount", percent: 20, maxDiscount: 500 });
    expect(d.limits).toEqual({ maxRedemptions: null, maxRedemptionsPerCustomer: null, budget: 50000, maxDiscountPerOrder: null });
    expect(d.coupon).toEqual({ codes: ["OCT20"], maxUsesPerCode: 1 });
  });

  it("sends unparsable numbers as typed so the API reports them", () => {
    const d = JSON.parse(JSON.stringify(toDefinition({ ...emptyForm(), reward: { ...emptyReward(), percent: "twenty" } })));
    expect(d.reward.percent).toBe("twenty");
  });

  it("round-trips every reward type", () => {
    const rewards = [
      { type: "amountDiscount", amount: 100, perUnit: true },
      { type: "fixedUnitPrice", price: 99.9 },
      { type: "buyXGetY", buyQuantity: 2, getQuantity: 1, discountPercent: 50, maxApplications: 2 },
      { type: "bundlePrice", quantity: 3, price: 100, maxApplications: null },
      { type: "tieredDiscount", basis: "quantity", tiers: [{ threshold: 3, percent: 10, amount: null }, { threshold: 5, percent: null, amount: 50 }] },
      { type: "freeShipping", maxAmount: 40 },
      { type: "giftProduct", sku: "TOTE-1", quantity: 2 },
    ];
    for (const reward of rewards) {
      const campaign = { code: "X", name: "x", reward } as unknown as Campaign;
      const back = JSON.parse(JSON.stringify(toDefinition(fromDefinition(campaign))));
      expect(back.reward).toMatchObject(reward);
    }
  });

  it("keeps what it cannot edit: composite conditions, product attributes and metadata", () => {
    const composite = { type: "anyOf", conditions: [{ type: "firstOrder" }, { type: "minSubtotal", amount: 500 }] };
    const scoped = { type: "minQuantity", quantity: 2, products: { categories: ["shoes"] } };
    const campaign = {
      id: "3f2a",
      version: 4,
      code: "KEEP",
      name: "keep",
      conditions: [composite, scoped, { type: "payment", bankCodes: ["0062"], minInstallments: 3 }],
      target: { attributes: { season: ["SS26"] } },
      metadata: { erpCampaignNo: "2026-0042" },
      schedule: { startsAt: "2026-09-30T21:00:00Z", timeZone: "Europe/Istanbul", dailyStart: "10:00:00", dailyEnd: "14:00:00" },
      reward: { type: "percentageDiscount", percent: 5 },
    } as unknown as Campaign;

    const form = fromDefinition(campaign);
    expect(form.conditions.map((c) => c.type)).toEqual(["advanced", "advanced", "payment"]);
    expect(form.startsAt).toBe("2026-10-01T00:00");
    expect(form.dailyStart).toBe("10:00");
    expect(form.version).toBe(4);

    const back = JSON.parse(JSON.stringify(toDefinition(form)));
    expect(back.conditions[0]).toEqual(composite);
    expect(back.conditions[1]).toEqual(scoped);
    expect(back.conditions[2]).toMatchObject({ type: "payment", bankCodes: ["0062"], minInstallments: 3 });
    expect(back.target.attributes).toEqual({ season: ["SS26"] });
    expect(back.metadata).toEqual({ erpCampaignNo: "2026-0042" });
    expect(back.schedule.startsAt).toBe("2026-10-01T00:00:00+03:00");
    expect(back.id).toBe("3f2a");
  });
});

using CampaignEngine.Core.Campaigns;
using CampaignEngine.Core.Carts;
using CampaignEngine.Core.Rules.Conditions;
using CampaignEngine.Core.Rules.Rewards;
using CampaignEngine.Core.Serialization;

namespace CampaignEngine.Core.Tests;

public class SerializationTests
{
    private const string HandWrittenCampaign = """
        {
          "code": "SUMMER-3X2",
          "name": "3 for 2 on t-shirts",
          "status": "active",
          "priority": "100",
          "stacking": "exclusive",
          "schedule": {
            "startsAt": "2026-06-01T00:00:00+03:00",
            "endsAt": "2026-09-01T00:00:00+03:00",
            "timeZone": "Europe/Istanbul",
            "daysOfWeek": ["saturday", "sunday"],
            "dailyStart": "10:00",
            "dailyEnd": "14:30:00"
          },
          "channels": ["store"],
          "target": { "categories": ["tshirt"], "attributes": { "Season": ["SS26"] } },
          "conditions": [
            { "quantity": 3, "type": "minQuantity" },
            { "type": "anyOf", "conditions": [ { "type": "firstOrder" }, { "type": "minSubtotal", "amount": "499.90" } ] }
          ],
          "reward": { "buyQuantity": 2, "getQuantity": 1, "type": "buyXGetY" },
          "metadata": { "ErpCampaignNo": "2026-0042" },
          "someFutureField": true
        }
        """;

    [Fact]
    public void Hand_written_json_from_other_languages_is_accepted()
    {
        var campaign = CampaignJson.Deserialize<Campaign>(HandWrittenCampaign);

        Assert.Equal(CampaignStatus.Active, campaign.Status);
        Assert.Equal(100, campaign.Priority);
        Assert.Equal(StackingMode.Exclusive, campaign.Stacking);
        Assert.Equal([DayOfWeek.Saturday, DayOfWeek.Sunday], campaign.Schedule.DaysOfWeek);
        Assert.Equal(new TimeOnly(10, 0), campaign.Schedule.DailyStart);
        Assert.Equal(new TimeOnly(14, 30), campaign.Schedule.DailyEnd);
        Assert.IsType<MinQuantityCondition>(campaign.Conditions[0]);
        var anyOf = Assert.IsType<AnyOfCondition>(campaign.Conditions[1]);
        Assert.Equal(499.90m, Assert.IsType<MinSubtotalCondition>(anyOf.Conditions[1]).Amount);
        var reward = Assert.IsType<BuyXGetYReward>(campaign.Reward);
        Assert.Equal(2, reward.BuyQuantity);
    }

    [Fact]
    public void Case_insensitive_dictionaries_survive_deserialization()
    {
        var campaign = CampaignJson.Deserialize<Campaign>(HandWrittenCampaign);

        Assert.Equal("2026-0042", campaign.Metadata["erpcampaignno"]);
        Assert.True(campaign.Target.Attributes.ContainsKey("season"));
    }

    [Fact]
    public void Round_trip_preserves_the_definition()
    {
        var original = CampaignJson.Deserialize<Campaign>(HandWrittenCampaign);

        var json = CampaignJson.Serialize(original);
        var copy = CampaignJson.Deserialize<Campaign>(json);

        Assert.Equal(json, CampaignJson.Serialize(copy));
        Assert.Contains("\"type\":\"buyXGetY\"", json, StringComparison.Ordinal);
        Assert.Contains("\"dailyStart\":\"10:00\"", json, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"active\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Computed_properties_are_not_part_of_the_contract()
    {
        var json = CampaignJson.Serialize(CampaignJson.Deserialize<Campaign>(HandWrittenCampaign));

        Assert.DoesNotContain("hasIncludeCriteria", json, StringComparison.Ordinal);
        Assert.DoesNotContain("hasExcludeCriteria", json, StringComparison.Ordinal);
        Assert.DoesNotContain("maxRateEstimate", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Cart_accepts_prices_as_strings()
    {
        var cart = CampaignJson.Deserialize<Cart>("""
            { "channel": "web", "lines": [ { "lineId": "1", "sku": "A", "quantity": "2", "unitPrice": "19.90" } ] }
            """);

        Assert.Equal(19.90m, cart.Lines[0].UnitPrice);
        Assert.Equal(2m, cart.Lines[0].Quantity);
    }

    [Fact]
    public void Unknown_rule_type_is_an_error()
    {
        const string json = """{ "code": "X", "name": "X", "reward": { "type": "teleport" } }""";

        Assert.ThrowsAny<System.Text.Json.JsonException>(() => CampaignJson.Deserialize<Campaign>(json));
    }
}

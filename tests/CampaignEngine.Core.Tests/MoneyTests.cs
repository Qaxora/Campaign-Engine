namespace CampaignEngine.Core.Tests;

public class MoneyTests
{
    [Fact]
    public void Allocate_sums_exactly_to_the_total()
    {
        var parts = Money.Allocate(100m, [1m, 1m, 1m], 2);

        Assert.Equal(100m, parts.Sum());
        Assert.Equal([33.34m, 33.33m, 33.33m], parts);
    }

    [Fact]
    public void Allocate_is_proportional_to_weights()
    {
        var parts = Money.Allocate(30m, [100m, 200m], 2);

        Assert.Equal([10m, 20m], parts);
    }

    [Fact]
    public void Allocate_gives_nothing_to_zero_weights()
    {
        var parts = Money.Allocate(10m, [0m, 5m, -3m], 2);

        Assert.Equal([0m, 10m, 0m], parts);
    }

    [Theory]
    [InlineData(2.345, 2.35)]
    [InlineData(2.344, 2.34)]
    [InlineData(-2.345, -2.35)]
    public void Round_is_half_away_from_zero(decimal value, decimal expected) =>
        Assert.Equal(expected, Money.Round(value, 2));
}

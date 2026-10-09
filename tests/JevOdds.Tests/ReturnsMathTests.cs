using JevOdds.Api.Pricing;

namespace JevOdds.Tests;

public class ReturnsMathTests
{
    private static readonly double[] Closes =
    [
        100, 101.5, 99.2, 103.4, 102.1, 104.8, 100.7, 106.2, 105.0, 107.4,
        103.9, 108.6, 110.2, 107.1, 111.5, 109.8, 113.0, 112.2, 115.4, 114.1, 116.8
    ];

    [Fact]
    public void SampleStdev_of_one_two_three_is_one()
    {
        var stdev = ReturnsMath.SampleStdev([1, 2, 3]);
        Assert.Equal(1, stdev, 8);
    }

    [Fact]
    public void Flat_log_returns_have_zero_volatility()
    {
        var vol = ReturnsMath.AnnualizedVolatility([100, 110, 121], 2);
        Assert.NotNull(vol);
        Assert.Equal(0, vol.Value, 8);
    }

    [Fact]
    public void Windowed_volatility_matches_hand_calculation()
    {
        var vol10 = ReturnsMath.AnnualizedVolatility(Closes, 10);
        var vol20 = ReturnsMath.AnnualizedVolatility(Closes, 20);
        var mean20 = ReturnsMath.AnnualizedLogMean(Closes, 20);
        Assert.NotNull(vol10);
        Assert.NotNull(vol20);
        Assert.NotNull(mean20);
        Assert.Equal(0.403500104, vol10.Value, 6);
        Assert.Equal(0.452598086, vol20.Value, 6);
        Assert.Equal(1.956690344, mean20.Value, 6);
    }

    [Fact]
    public void Short_history_returns_null_for_a_long_window()
    {
        Assert.Null(ReturnsMath.AnnualizedVolatility(Closes, 252));
    }

    [Fact]
    public void Blend_averages_the_windows_that_exist()
    {
        Assert.Equal(0.3, ReturnsMath.BlendVolatility(0.2, 0.3, 0.4));
        Assert.Equal(0.25, ReturnsMath.BlendVolatility(0.2, 0.3, null));
        Assert.Null(ReturnsMath.BlendVolatility(null, null, null));
    }

    [Fact]
    public void Empirical_frequency_counts_known_windows()
    {
        double[] closes = [100, 100, 100, 110, 100];
        var result = ReturnsMath.Empirical(closes, 1, 5, "up");
        Assert.Equal(4, result.Samples);
        Assert.Equal(0.25, result.Close);
        Assert.Equal(0.25, result.Touch);
    }

    [Fact]
    public void Empirical_touch_is_at_least_the_close_frequency()
    {
        double[] closes = [100, 112, 101, 90, 100, 108, 95, 100];
        foreach (var direction in new[] { "up", "down", "either" })
        {
            var result = ReturnsMath.Empirical(closes, 2, 8, direction);
            Assert.NotNull(result.Close);
            Assert.NotNull(result.Touch);
            Assert.True(result.Touch >= result.Close);
        }
    }
}

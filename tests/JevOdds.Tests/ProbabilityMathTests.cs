using JevOdds.Api.Pricing;

namespace JevOdds.Tests;

public class ProbabilityMathTests
{
    [Fact]
    public void Normal_cdf_matches_known_values()
    {
        Assert.Equal(0.5, Normal.Cdf(0), 6);
        Assert.Equal(0.841344746, Normal.Cdf(1), 6);
        Assert.Equal(0.158655254, Normal.Cdf(-1), 6);
    }

    [Fact]
    public void Zero_log_drift_touch_is_twice_the_close_probability()
    {
        const double sigma = 0.22;
        var mu = 0.5 * sigma * sigma;
        var closeUp = ProbabilityMath.CloseUp(100, 115, mu, sigma, 0.8);
        var touchUp = ProbabilityMath.TouchUp(100, 115, mu, sigma, 0.8);
        Assert.True(closeUp < 0.5);
        Assert.Equal(2 * closeUp, touchUp, 6);

        var closeDown = ProbabilityMath.CloseDown(100, 85, mu, sigma, 0.8);
        var touchDown = ProbabilityMath.TouchDown(100, 85, mu, sigma, 0.8);
        Assert.Equal(2 * closeDown, touchDown, 6);
    }

    [Fact]
    public void Touch_is_at_least_close_for_a_grid_of_inputs()
    {
        double[] sigmas = [0.15, 0.3, 0.6];
        double[] percents = [5, 15];
        double[] years = [0.1, 1];
        double[] drifts = [-0.1, 0, 0.15];
        foreach (var sigma in sigmas)
        foreach (var percent in percents)
        foreach (var year in years)
        foreach (var mu in drifts)
        {
            var up = 100 * (1 + percent / 100);
            var down = 100 * (1 - percent / 100);
            var closeUp = ProbabilityMath.CloseUp(100, up, mu, sigma, year);
            var touchUp = ProbabilityMath.TouchUp(100, up, mu, sigma, year);
            Assert.True(touchUp + 1e-9 >= closeUp, $"up touch {touchUp} close {closeUp}");

            var closeDown = ProbabilityMath.CloseDown(100, down, mu, sigma, year);
            var touchDown = ProbabilityMath.TouchDown(100, down, mu, sigma, year);
            Assert.True(touchDown + 1e-9 >= closeDown, $"down touch {touchDown} close {closeDown}");

            var closeEither = Math.Min(1, closeUp + closeDown);
            var touchEither = ProbabilityMath.TouchEither(100, down, up, mu, sigma, year);
            Assert.InRange(touchEither, 0, 1);
            Assert.True(touchEither + 1e-6 >= closeEither, $"either touch {touchEither} close {closeEither}");
            Assert.True(touchEither + 1e-3 >= Math.Max(touchUp, touchDown));
            Assert.True(touchEither <= Math.Min(1, touchUp + touchDown) + 1e-3);
        }
    }

    [Fact]
    public void Either_close_is_the_sum_of_the_two_tails()
    {
        const double mu = 0.04;
        const double sigma = 0.25;
        const double years = 0.5;
        var up = ProbabilityMath.CloseUp(100, 110, mu, sigma, years);
        var down = ProbabilityMath.CloseDown(100, 90, mu, sigma, years);
        Assert.True(up + down <= 1 + 1e-9);
        Assert.True(up > 0 && down > 0);
    }

    [Theory]
    [InlineData("up", 10, 0.0, 0.20, 1.0)]
    [InlineData("down", 10, 0.05, 0.25, 0.5)]
    [InlineData("either", 10, 0.0, 0.20, 1.0)]
    public void Monte_Carlo_agrees_with_the_analytic_probability(string direction, double percent, double mu, double sigma, double years)
    {
        var upper = direction is "up" or "either" ? 100 * (1 + percent / 100) : (double?)null;
        var lower = direction is "down" or "either" ? 100 * (1 - percent / 100) : (double?)null;
        double analyticClose;
        double analyticTouch;
        if (direction == "up")
        {
            analyticClose = ProbabilityMath.CloseUp(100, upper!.Value, mu, sigma, years);
            analyticTouch = ProbabilityMath.TouchUp(100, upper.Value, mu, sigma, years);
        }
        else if (direction == "down")
        {
            analyticClose = ProbabilityMath.CloseDown(100, lower!.Value, mu, sigma, years);
            analyticTouch = ProbabilityMath.TouchDown(100, lower.Value, mu, sigma, years);
        }
        else
        {
            analyticClose = ProbabilityMath.CloseUp(100, upper!.Value, mu, sigma, years)
                + ProbabilityMath.CloseDown(100, lower!.Value, mu, sigma, years);
            analyticTouch = ProbabilityMath.TouchEither(100, lower!.Value, upper.Value, mu, sigma, years);
        }

        var mc = ProbabilityMath.MonteCarlo(100, upper, lower, mu, sigma, years, steps: 40, paths: 20_000, seed: 7);
        Assert.True(mc.Touch + 1e-12 >= mc.CloseBeyond, "A path that finishes beyond the target has touched it.");
        Assert.InRange(Math.Abs(mc.CloseBeyond - analyticClose), 0, 0.02);
        Assert.InRange(Math.Abs(mc.Touch - analyticTouch), 0, 0.02);
        Assert.NotEmpty(mc.SamplePaths);
        Assert.All(mc.SamplePaths, path => Assert.Equal(100, path[0], 6));
    }
}

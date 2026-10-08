namespace JevOdds.Api.Pricing;

public static class ProbabilityMath
{
    public readonly record struct DensityPoint(double Price, double Density);

    public readonly record struct McResult(double CloseBeyond, double Touch, double[][] SamplePaths);

    public static double Nu(double mu, double sigma) => mu - 0.5 * sigma * sigma;

    public static double CloseUp(double spot, double barrier, double mu, double sigma, double years)
    {
        if (barrier <= spot)
        {
            return 1;
        }

        if (years <= 0)
        {
            return 0;
        }

        if (sigma <= 1e-8)
        {
            var terminal = spot * Math.Exp(mu * years);
            return terminal >= barrier ? 1 : 0;
        }

        var distance = Math.Log(barrier / spot);
        var scale = sigma * Math.Sqrt(years);
        return Clamp01(Normal.Cdf((-distance + Nu(mu, sigma) * years) / scale));
    }

    public static double CloseDown(double spot, double barrier, double mu, double sigma, double years)
    {
        if (barrier >= spot)
        {
            return 1;
        }

        if (years <= 0)
        {
            return 0;
        }

        if (sigma <= 1e-8)
        {
            var terminal = spot * Math.Exp(mu * years);
            return terminal <= barrier ? 1 : 0;
        }

        var distance = Math.Log(spot / barrier);
        var scale = sigma * Math.Sqrt(years);
        return Clamp01(Normal.Cdf((-distance - Nu(mu, sigma) * years) / scale));
    }

    public static double TouchUp(double spot, double barrier, double mu, double sigma, double years)
    {
        if (barrier <= spot)
        {
            return 1;
        }

        if (years <= 0)
        {
            return 0;
        }

        if (sigma <= 1e-8)
        {
            if (mu <= 0)
            {
                return 0;
            }

            var terminal = spot * Math.Exp(mu * years);
            return terminal >= barrier ? 1 : 0;
        }

        var distance = Math.Log(barrier / spot);
        var nu = Nu(mu, sigma);
        var scale = sigma * Math.Sqrt(years);
        var first = Normal.Cdf((-distance + nu * years) / scale);
        var exponent = 2.0 * nu * distance / (sigma * sigma);
        var second = SafeExp(exponent) * Normal.Cdf((-distance - nu * years) / scale);
        return Clamp01(first + second);
    }

    public static double TouchDown(double spot, double barrier, double mu, double sigma, double years)
    {
        if (barrier >= spot)
        {
            return 1;
        }

        if (years <= 0)
        {
            return 0;
        }

        if (sigma <= 1e-8)
        {
            if (mu >= 0)
            {
                return 0;
            }

            var terminal = spot * Math.Exp(mu * years);
            return terminal <= barrier ? 1 : 0;
        }

        var distance = Math.Log(spot / barrier);
        var nu = Nu(mu, sigma);
        var scale = sigma * Math.Sqrt(years);
        var first = Normal.Cdf((-distance - nu * years) / scale);
        var exponent = -2.0 * nu * distance / (sigma * sigma);
        var second = SafeExp(exponent) * Normal.Cdf((-distance + nu * years) / scale);
        return Clamp01(first + second);
    }

    public static double TouchEither(double spot, double lower, double upper, double mu, double sigma, double years)
    {
        if (spot >= upper || spot <= lower)
        {
            return 1;
        }

        if (years <= 0)
        {
            return 0;
        }

        if (sigma <= 1e-8)
        {
            var terminal = spot * Math.Exp(mu * years);
            if (mu >= 0)
            {
                return terminal >= upper ? 1 : 0;
            }

            return terminal <= lower ? 1 : 0;
        }

        var up = TouchUp(spot, upper, mu, sigma, years);
        var down = TouchDown(spot, lower, mu, sigma, years);
        var floor = Math.Max(up, down);
        var ceiling = Math.Min(1.0, up + down);
        var series = 1.0 - Survival(Math.Log(spot), Math.Log(lower), Math.Log(upper), Nu(mu, sigma), sigma, years);
        if (!double.IsFinite(series) || series < floor - 0.02 || series > ceiling + 0.02)
        {
            return TouchEitherTree(spot, lower, upper, mu, sigma, years);
        }

        if (series < floor)
        {
            series = floor;
        }

        if (series > ceiling)
        {
            series = ceiling;
        }

        return Clamp01(series);
    }

    public static (double Low, double High) OneSigmaRange(double spot, double mu, double sigma, double years)
    {
        var nu = Nu(mu, sigma);
        var width = sigma * Math.Sqrt(Math.Max(years, 0));
        return (spot * Math.Exp(nu * years - width), spot * Math.Exp(nu * years + width));
    }

    public static DensityPoint[] TerminalDensity(
        double spot,
        double mu,
        double sigma,
        double years,
        int count = 96)
    {
        if (count < 8)
        {
            count = 8;
        }

        var nu = Nu(mu, sigma);
        var mean = Math.Log(spot) + nu * years;
        var sd = sigma * Math.Sqrt(Math.Max(years, 1e-8));
        var points = new DensityPoint[count + 1];
        for (var i = 0; i <= count; i++)
        {
            var z = -3.2 + 6.4 * i / count;
            var price = Math.Exp(mean + sd * z);
            var density = Normal.Pdf(z) / (price * sd);
            points[i] = new DensityPoint(price, density);
        }

        return points;
    }

    public static McResult MonteCarlo(
        double spot,
        double? upper,
        double? lower,
        double mu,
        double sigma,
        double years,
        int steps,
        int paths,
        int seed,
        int samplePaths = 8,
        int samplePoints = 36)
    {
        if (paths < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(paths));
        }

        steps = Math.Max(1, steps);
        var rng = new Random(seed);
        var dt = years / steps;
        var nu = Nu(mu, sigma);
        var vol = sigma * Math.Sqrt(dt);
        var variance = sigma * sigma * dt;
        double? logUpper = upper is > 0 ? Math.Log(upper.Value) : null;
        double? logLower = lower is > 0 ? Math.Log(lower.Value) : null;
        var closeHits = 0;
        var touchHits = 0;
        var samples = new List<double[]>(Math.Min(samplePaths, paths));

        for (var pathIndex = 0; pathIndex < paths; pathIndex++)
        {
            var keep = pathIndex < samplePaths;
            List<double>? path = keep ? new List<double>(steps + 1) { spot } : null;
            var x = Math.Log(spot);
            var touched = (logUpper is double startUp && x >= startUp) || (logLower is double startDown && x <= startDown);

            for (var step = 0; step < steps; step++)
            {
                var next = x + nu * dt + vol * NextGaussian(rng);
                if (!touched && HitsBarrier(x, next, logUpper, logLower, variance, rng))
                {
                    touched = true;
                }

                x = next;
                path?.Add(Math.Exp(x));
            }

            var terminal = Math.Exp(x);
            var close = (upper is not null && terminal >= upper.Value) || (lower is not null && terminal <= lower.Value);
            if (close)
            {
                touched = true;
                closeHits++;
            }

            if (touched)
            {
                touchHits++;
            }

            if (path is not null)
            {
                samples.Add(Downsample(path, samplePoints));
            }
        }

        return new McResult(closeHits / (double)paths, touchHits / (double)paths, samples.ToArray());
    }

    private static bool HitsBarrier(double x, double next, double? logUpper, double? logLower, double variance, Random rng)
    {
        if (logUpper is double upper)
        {
            if (x >= upper || next >= upper)
            {
                return true;
            }

            if (variance > 0)
            {
                var probability = Math.Exp(-2.0 * (upper - x) * (upper - next) / variance);
                if (probability >= 1 || rng.NextDouble() < probability)
                {
                    return true;
                }
            }
        }

        if (logLower is double lower)
        {
            if (x <= lower || next <= lower)
            {
                return true;
            }

            if (variance > 0)
            {
                var probability = Math.Exp(-2.0 * (x - lower) * (next - lower) / variance);
                if (probability >= 1 || rng.NextDouble() < probability)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static double Survival(double x, double lower, double upper, double nu, double sigma, double years)
    {
        var width = upper - lower;
        var sigma2 = sigma * sigma;
        var alpha = nu / sigma2;
        var timeFactor = SafeExp(-nu * nu * years / (2.0 * sigma2));
        var sum = 0.0;

        if (Math.Abs(alpha) < 1e-10)
        {
            for (var n = 1; n <= 2000; n += 2)
            {
                var lambda = 0.5 * sigma2 * Math.Pow(n * Math.PI / width, 2);
                var term = (4.0 / (n * Math.PI)) * Math.Sin(n * Math.PI * (x - lower) / width) * Math.Exp(-lambda * years);
                sum += term;
                if (n > 80 && Math.Abs(term) < 1e-14)
                {
                    break;
                }
            }

            return Clamp01(sum);
        }

        for (var n = 1; n <= 2000; n++)
        {
            var beta = n * Math.PI / width;
            var lambda = 0.5 * sigma2 * beta * beta;
            var decay = Math.Exp(-lambda * years);
            var sign = n % 2 == 0 ? 1.0 : -1.0;
            var e1 = alpha * (lower - x);
            var e2 = alpha * (upper - x);
            if (Math.Max(e1, e2) > 80)
            {
                return double.NaN;
            }

            var bracket = SafeExp(e1) - sign * SafeExp(e2);
            var term = timeFactor * (2.0 / width) * Math.Sin(beta * (x - lower)) * decay * beta * bracket / (alpha * alpha + beta * beta);
            if (!double.IsFinite(term))
            {
                return double.NaN;
            }

            sum += term;
            if (decay < 1e-16 && n > 40)
            {
                break;
            }
        }

        return sum;
    }

    private static double TouchEitherTree(double spot, double lower, double upper, double mu, double sigma, double years)
    {
        const int steps = 500;
        var dt = years / steps;
        var dx = sigma * Math.Sqrt(dt);
        var nu = Nu(mu, sigma);
        var logSpot = Math.Log(spot);
        var logLower = Math.Log(lower);
        var logUpper = Math.Log(upper);
        var current = new double[1];
        current[0] = 1.0;

        for (var step = 0; step < steps; step++)
        {
            var next = new double[step + 2];
            for (var node = 0; node <= step; node++)
            {
                var mass = current[node];
                if (mass == 0)
                {
                    continue;
                }

                var logUp = logSpot + (step + 1) * nu * dt + (2 * (node + 1) - (step + 1)) * dx;
                var logDown = logSpot + (step + 1) * nu * dt + (2 * node - (step + 1)) * dx;
                if (logUp > logLower && logUp < logUpper)
                {
                    next[node + 1] += mass * 0.5;
                }

                if (logDown > logLower && logDown < logUpper)
                {
                    next[node] += mass * 0.5;
                }
            }

            current = next;
        }

        var survive = 0.0;
        foreach (var mass in current)
        {
            survive += mass;
        }

        return Clamp01(1.0 - survive);
    }

    private static double NextGaussian(Random rng)
    {
        var u1 = 1.0 - rng.NextDouble();
        var u2 = 1.0 - rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    private static double[] Downsample(List<double> values, int maxPoints)
    {
        if (values.Count <= maxPoints)
        {
            return values.ToArray();
        }

        var result = new double[maxPoints];
        var last = values.Count - 1;
        for (var i = 0; i < maxPoints; i++)
        {
            var index = (int)Math.Round(i * last / (double)(maxPoints - 1));
            result[i] = values[index];
        }

        return result;
    }

    private static double SafeExp(double exponent)
    {
        if (exponent > 80)
        {
            return double.PositiveInfinity;
        }

        if (exponent < -80)
        {
            return 0;
        }

        return Math.Exp(exponent);
    }

    private static double Clamp01(double value)
    {
        if (double.IsNaN(value) || value < 0 || double.IsNegativeInfinity(value))
        {
            return 0;
        }

        if (value > 1 || double.IsPositiveInfinity(value))
        {
            return 1;
        }

        return value;
    }
}

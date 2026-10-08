namespace JevOdds.Api.Pricing;

public static class ReturnsMath
{
    public static double[] LogReturns(IReadOnlyList<double> closes)
    {
        var returns = new List<double>(Math.Max(0, closes.Count - 1));
        for (var i = 1; i < closes.Count; i++)
        {
            var previous = closes[i - 1];
            var current = closes[i];
            if (previous > 0 && current > 0)
            {
                returns.Add(Math.Log(current / previous));
            }
        }

        return returns.ToArray();
    }

    public static double SampleStdev(IReadOnlyList<double> values)
    {
        if (values.Count < 2)
        {
            throw new ArgumentException("Need at least two values for a sample standard deviation.");
        }

        var mean = values.Average();
        var sum = 0.0;
        foreach (var value in values)
        {
            var delta = value - mean;
            sum += delta * delta;
        }

        return Math.Sqrt(sum / (values.Count - 1));
    }

    public static double? AnnualizedVolatility(IReadOnlyList<double> closes, int window)
    {
        var returns = LogReturns(closes);
        if (window < 2 || returns.Length < window)
        {
            return null;
        }

        var slice = returns[^window..];
        return SampleStdev(slice) * Math.Sqrt(252.0);
    }

    public static double? AnnualizedLogMean(IReadOnlyList<double> closes, int window)
    {
        var returns = LogReturns(closes);
        if (window < 1 || returns.Length < window)
        {
            return null;
        }

        var sum = 0.0;
        for (var i = returns.Length - window; i < returns.Length; i++)
        {
            sum += returns[i];
        }

        return sum / window * 252.0;
    }

    public static double? BlendVolatility(double? vol20, double? vol60, double? vol252)
    {
        var values = new[] { vol20, vol60, vol252 }.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return values.Length == 0 ? null : values.Average();
    }

    public readonly record struct Frequency(double? Close, double? Touch, int Samples);

    public static Frequency Empirical(IReadOnlyList<double> closes, int horizon, double percent, string direction)
    {
        if (horizon < 1 || closes.Count <= horizon)
        {
            return new Frequency(null, null, 0);
        }

        var upFactor = 1.0 + percent / 100.0;
        var downFactor = 1.0 - percent / 100.0;
        var samples = 0;
        var closeHits = 0;
        var touchHits = 0;
        var wantUp = direction is "up" or "either";
        var wantDown = direction is "down" or "either";

        for (var start = 0; start + horizon < closes.Count; start++)
        {
            var spot = closes[start];
            if (spot <= 0)
            {
                continue;
            }

            var end = start + horizon;
            var closeHit = false;
            var touchHit = false;
            if (wantUp)
            {
                var barrier = spot * upFactor;
                if (closes[end] >= barrier)
                {
                    closeHit = true;
                }

                for (var step = start + 1; step <= end; step++)
                {
                    if (closes[step] >= barrier)
                    {
                        touchHit = true;
                        break;
                    }
                }
            }

            if (wantDown)
            {
                var barrier = spot * downFactor;
                if (closes[end] <= barrier)
                {
                    closeHit = true;
                }

                if (!touchHit)
                {
                    for (var step = start + 1; step <= end; step++)
                    {
                        if (closes[step] <= barrier)
                        {
                            touchHit = true;
                            break;
                        }
                    }
                }
            }

            samples++;
            if (closeHit)
            {
                closeHits++;
            }

            if (touchHit)
            {
                touchHits++;
            }
        }

        if (samples == 0)
        {
            return new Frequency(null, null, 0);
        }

        return new Frequency(closeHits / (double)samples, touchHits / (double)samples, samples);
    }
}

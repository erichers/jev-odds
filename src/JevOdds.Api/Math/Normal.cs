namespace JevOdds.Api.Pricing;

public static class Normal
{
    // Abramowitz and Stegun 26.2.17. Absolute error is under 1e-7.
    public static double Cdf(double x)
    {
        if (double.IsNaN(x))
        {
            return double.NaN;
        }

        if (x < -8)
        {
            return 0;
        }

        if (x > 8)
        {
            return 1;
        }

        var sign = 1.0;
        if (x < 0)
        {
            sign = -1;
            x = -x;
        }

        const double p = 0.2316419;
        const double b1 = 0.319381530;
        const double b2 = -0.356563782;
        const double b3 = 1.781477937;
        const double b4 = -1.821255978;
        const double b5 = 1.330274429;
        var t = 1.0 / (1.0 + p * x);
        var pdf = Math.Exp(-0.5 * x * x) / Math.Sqrt(2.0 * Math.PI);
        var poly = (((((b5 * t) + b4) * t + b3) * t + b2) * t + b1) * t;
        var upper = 1.0 - pdf * poly;
        return sign > 0 ? upper : 1.0 - upper;
    }

    public static double Pdf(double x)
    {
        return Math.Exp(-0.5 * x * x) / Math.Sqrt(2.0 * Math.PI);
    }
}

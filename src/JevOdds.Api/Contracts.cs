namespace JevOdds.Api;

public sealed class OddsRequest
{
    public string Ticker { get; set; } = "";

    public double Percent { get; set; }

    public string Direction { get; set; } = "up";

    public DateOnly TargetDate { get; set; }

    public string VolWindow { get; set; } = "60";

    public double? VolOverridePercent { get; set; }

    public string Drift { get; set; } = "zero";
}

public sealed record DensityPoint(double Price, double Density);

public sealed record OddsResponse
{
    public required string Ticker { get; init; }
    public string? Name { get; init; }
    public required string Direction { get; init; }
    public double Percent { get; init; }
    public double Spot { get; init; }
    public required string AsOf { get; init; }
    public required string TargetDate { get; init; }
    public required string EffectiveDate { get; init; }
    public int TradingDays { get; init; }
    public double Years { get; init; }
    public double Sigma { get; init; }
    public required string VolSource { get; init; }
    public int VolSampleDays { get; init; }
    public double? Vol20 { get; init; }
    public double? Vol60 { get; init; }
    public double? Vol252 { get; init; }
    public double? VolBlend { get; init; }
    public required string Drift { get; init; }
    public double Mu { get; init; }
    public double Nu { get; init; }
    public double? Upper { get; init; }
    public double? Lower { get; init; }
    public double AnalyticClose { get; init; }
    public double AnalyticTouch { get; init; }
    public double MonteCarloClose { get; init; }
    public double MonteCarloTouch { get; init; }
    public int MonteCarloPaths { get; init; }
    public int MonteCarloSeed { get; init; }
    public double ExpectedLow { get; init; }
    public double ExpectedHigh { get; init; }
    public double? EmpiricalClose { get; init; }
    public double? EmpiricalTouch { get; init; }
    public int EmpiricalSamples { get; init; }
    public required string Origin { get; init; }
    public required string Provider { get; init; }
    public required DensityPoint[] Density { get; init; }
    public required double[][] Paths { get; init; }
    public required string Formula { get; init; }
}

public sealed record MarketSnapshot
{
    public required string Ticker { get; init; }
    public string? Name { get; init; }
    public double LastClose { get; init; }
    public required string AsOf { get; init; }
    public required string Origin { get; init; }
    public required string Provider { get; init; }
    public int Bars { get; init; }
    public double? Vol20 { get; init; }
    public double? Vol60 { get; init; }
    public double? Vol252 { get; init; }
    public double? VolBlend { get; init; }
    public double? LogDriftAnnual { get; init; }
}

public sealed record TickerInfo(string Symbol, string Name, bool Seeded);

public sealed record CalendarResponse(string From, string To, int TradingDays, string? EffectiveDate);

public sealed record HistoryItem(
    long Id,
    string Ticker,
    string? Name,
    double Percent,
    string Direction,
    string TargetDate,
    string VolWindow,
    double? VolOverridePercent,
    string Drift,
    double AnalyticClose,
    double AnalyticTouch,
    string Origin,
    string CreatedAtUtc);

public sealed class OddsException : Exception
{
    public OddsException(string message) : base(message)
    {
    }
}

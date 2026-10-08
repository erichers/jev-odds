namespace JevOdds.Api;

public sealed class OddsOptions
{
    public const string Section = "Odds";

    public int CacheHours { get; set; } = 12;

    public string YahooBaseUrl { get; set; } = "https://query1.finance.yahoo.com";

    public string StooqBaseUrl { get; set; } = "https://stooq.com";

    public int MonteCarloPaths { get; set; } = 20_000;

    public int MonteCarloSeed { get; set; } = 184208;
}

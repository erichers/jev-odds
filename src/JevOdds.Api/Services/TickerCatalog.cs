namespace JevOdds.Api.Services;

public static class TickerCatalog
{
    private static readonly TickerInfo[] All =
    [
        new("SPY", "SPDR S&P 500 ETF", true),
        new("QQQ", "Invesco QQQ", true),
        new("AAPL", "Apple", true),
        new("NVDA", "NVIDIA", true),
        new("TSLA", "Tesla", true),
        new("MSFT", "Microsoft", true),
        new("AMZN", "Amazon", true),
        new("GOOGL", "Alphabet", true),
        new("META", "Meta", true),
        new("AMD", "AMD", true),
        new("NFLX", "Netflix", true),
        new("AVGO", "Broadcom", true),
        new("COST", "Costco", true),
        new("JPM", "JPMorgan Chase", true),
        new("V", "Visa", false),
        new("MA", "Mastercard", false),
        new("UNH", "UnitedHealth", false),
        new("XOM", "Exxon Mobil", false),
        new("JNJ", "Johnson and Johnson", false),
        new("PG", "Procter and Gamble", false),
        new("HD", "Home Depot", false),
        new("LLY", "Eli Lilly", false),
        new("BAC", "Bank of America", false),
        new("WMT", "Walmart", false),
        new("CRM", "Salesforce", false),
        new("ORCL", "Oracle", false),
        new("KO", "Coca-Cola", false),
        new("PEP", "PepsiCo", false),
        new("ADBE", "Adobe", false),
        new("CSCO", "Cisco", false),
        new("INTC", "Intel", false),
        new("DIS", "Disney", false),
        new("ACN", "Accenture", false),
        new("PFE", "Pfizer", false),
        new("T", "AT&T", false),
        new("VZ", "Verizon", false),
        new("NKE", "Nike", false),
        new("WFC", "Wells Fargo", false),
        new("C", "Citigroup", false),
        new("BA", "Boeing", false),
        new("IBM", "IBM", false),
        new("CAT", "Caterpillar", false),
        new("GE", "GE Aerospace", false),
        new("AMGN", "Amgen", false),
        new("HON", "Honeywell", false),
        new("UPS", "UPS", false),
        new("LOW", "Lowe's", false),
        new("SBUX", "Starbucks", false),
        new("INTU", "Intuit", false),
        new("AMAT", "Applied Materials", false),
        new("BKNG", "Booking", false),
        new("DE", "Deere", false),
        new("ISRG", "Intuitive Surgical", false),
        new("ADP", "ADP", false),
        new("GILD", "Gilead", false),
        new("MDLZ", "Mondelez", false),
        new("NOW", "ServiceNow", false),
        new("PANW", "Palo Alto Networks", false),
        new("CRWD", "CrowdStrike", false),
        new("SHOP", "Shopify", false),
        new("UBER", "Uber", false),
        new("PYPL", "PayPal", false),
        new("COIN", "Coinbase", false),
        new("PLTR", "Palantir", false),
        new("QCOM", "Qualcomm", false),
        new("TXN", "Texas Instruments", false),
        new("NEE", "NextEra Energy", false),
        new("MRK", "Merck", false),
        new("ABBV", "AbbVie", false),
        new("CVX", "Chevron", false),
        new("MCD", "McDonald's", false),
        new("BRK-B", "Berkshire Hathaway", false),
        new("IWM", "iShares Russell 2000", true),
        new("DIA", "SPDR Dow Jones", false),
        new("GLD", "SPDR Gold", false),
        new("TLT", "iShares 20+ Year Treasury", false),
        new("XLF", "Financial Select Sector", false),
        new("XLK", "Technology Select Sector", false),
        new("XLE", "Energy Select Sector", false),
        new("ARM", "Arm", false),
        new("SNOW", "Snowflake", false),
        new("ABNB", "Airbnb", false),
        new("F", "Ford", false),
        new("GM", "General Motors", false)
    ];

    private static readonly Dictionary<string, TickerInfo> BySymbol = All.ToDictionary(item => item.Symbol, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<TickerInfo> Search(string? query, int limit = 8)
    {
        if (limit < 1)
        {
            limit = 8;
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return All.Where(item => item.Seeded).ToArray();
        }

        var needle = query.Trim();
        return All
            .Select(item => (item, score: Score(item, needle)))
            .Where(row => row.score > 0)
            .OrderByDescending(row => row.score)
            .ThenBy(row => row.item.Symbol, StringComparer.Ordinal)
            .Take(limit)
            .Select(row => row.item)
            .ToArray();
    }

    public static string? NameOf(string symbol)
    {
        return BySymbol.TryGetValue(symbol, out var info) ? info.Name : null;
    }

    public static bool TryNormalize(string raw, out string symbol)
    {
        symbol = raw.Trim().ToUpperInvariant().Replace('.', '-');
        if (symbol.Length is < 1 or > 12)
        {
            return false;
        }

        foreach (var character in symbol)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '-')
            {
                return false;
            }
        }

        return true;
    }

    private static int Score(TickerInfo item, string needle)
    {
        if (item.Symbol.Equals(needle, StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        if (item.Symbol.StartsWith(needle, StringComparison.OrdinalIgnoreCase))
        {
            return 80;
        }

        if (item.Symbol.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            return 50;
        }

        if (item.Name.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            return 30;
        }

        return 0;
    }
}

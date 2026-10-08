using System.Text.Json;

namespace JevOdds.Tests;

public class SeedDataTests
{
    [Fact]
    public void Shipped_samples_cover_the_demo_tickers()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Seed");
        string[] expected =
        [
            "spy.json", "qqq.json", "iwm.json", "aapl.json", "msft.json", "nvda.json", "tsla.json",
            "amzn.json", "googl.json", "meta.json", "amd.json", "avgo.json", "nflx.json", "jpm.json", "cost.json"
        ];
        foreach (var name in expected)
        {
            var path = Path.Combine(directory, name);
            Assert.True(File.Exists(path), $"Missing seed file {name} in {directory}");
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var ticker = root.GetProperty("ticker").GetString();
            Assert.Equal(Path.GetFileNameWithoutExtension(name), ticker, ignoreCase: true);
            var bars = root.GetProperty("bars");
            Assert.True(bars.GetArrayLength() >= 1000, ticker);
            string? previous = null;
            foreach (var bar in bars.EnumerateArray())
            {
                var date = bar.GetProperty("date").GetString();
                var close = bar.GetProperty("close").GetDouble();
                Assert.True(close > 0, ticker);
                Assert.False(string.IsNullOrWhiteSpace(date));
                if (previous is not null)
                {
                    Assert.True(string.CompareOrdinal(previous, date) < 0, $"{ticker} dates are not sorted");
                }

                previous = date;
            }
        }
    }
}

using System.Globalization;
using System.Text.Json;
using JevOdds.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JevOdds.Api.Services;

public sealed record CloseBar(DateOnly Date, double Close);

public sealed record PriceSeries(string Ticker, IReadOnlyList<CloseBar> Bars, string Origin, string Provider);

public sealed class MarketDataService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly OddsDbContext _db;
    private readonly IHttpClientFactory _http;
    private readonly OddsOptions _options;
    private readonly ILogger<MarketDataService> _logger;

    public MarketDataService(
        OddsDbContext db,
        IHttpClientFactory http,
        IOptions<OddsOptions> options,
        ILogger<MarketDataService> logger)
    {
        _db = db;
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PriceSeries> GetAsync(string ticker, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var cached = await LoadAsync(ticker, cancellationToken);
            var freshCutoff = DateTime.UtcNow.AddHours(-Math.Max(1, _options.CacheHours));
            if (cached is not null
                && cached.Value.Bars.Count >= 40
                && cached.Value.Provider is "yahoo" or "stooq"
                && cached.Value.FetchedAtUtc >= freshCutoff)
            {
                return new PriceSeries(ticker, cached.Value.Bars, "cached", cached.Value.Provider);
            }

            try
            {
                var live = await FetchLiveAsync(ticker, cancellationToken);
                await SaveAsync(ticker, live.Bars, live.Provider, cancellationToken);
                return new PriceSeries(ticker, live.Bars, "live", live.Provider);
            }
            catch (Exception ex) when (ShouldFallBack(ex, cancellationToken))
            {
                _logger.LogInformation(ex, "Live price fetch failed for {Ticker}", ticker);
                if (cached is { Bars.Count: > 0 })
                {
                    return new PriceSeries(ticker, cached.Value.Bars, "cached", cached.Value.Provider);
                }

                throw new OddsException($"No price history for {ticker}. Live data did not respond, and there is no cached series.");
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task SeedAsync(OddsDbContext db, string contentRoot, ILogger logger, CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(contentRoot, "Seed");
        if (!Directory.Exists(directory))
        {
            logger.LogWarning("Seed directory {Directory} was not found", directory);
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.json").OrderBy(path => path, StringComparer.Ordinal))
        {
            SeedFile? seed;
            try
            {
                await using var stream = File.OpenRead(file);
                seed = await JsonSerializer.DeserializeAsync<SeedFile>(stream, JsonOptions, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not read seed file {File}", file);
                continue;
            }

            if (seed is null || !TickerCatalog.TryNormalize(seed.Ticker, out var ticker))
            {
                continue;
            }

            if (await db.PriceBars.AnyAsync(bar => bar.Ticker == ticker, cancellationToken))
            {
                continue;
            }

            var bars = seed.Bars
                .Select(bar =>
                {
                    var parsed = DateOnly.TryParseExact(bar.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date);
                    return (parsed, date, bar.Close);
                })
                .Where(bar => bar.parsed && bar.Close > 0)
                .GroupBy(bar => bar.date)
                .Select(group => group.Last())
                .OrderBy(bar => bar.date)
                .Select(bar => new PriceBar { Ticker = ticker, Date = bar.date, Close = bar.Close })
                .ToList();

            if (bars.Count < 40)
            {
                logger.LogWarning("Seed file {File} did not contain enough bars", file);
                continue;
            }

            db.PriceBars.AddRange(bars);
            db.TickerCaches.Add(new TickerCache
            {
                Ticker = ticker,
                Provider = "seed",
                FetchedAtUtc = DateTime.UnixEpoch
            });
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded {Count} closes for {Ticker}", bars.Count, ticker);
        }
    }

    private async Task<(List<CloseBar> Bars, string Provider, DateTime FetchedAtUtc)?> LoadAsync(string ticker, CancellationToken cancellationToken)
    {
        var meta = await _db.TickerCaches.AsNoTracking().FirstOrDefaultAsync(cache => cache.Ticker == ticker, cancellationToken);
        var bars = await _db.PriceBars.AsNoTracking()
            .Where(bar => bar.Ticker == ticker)
            .OrderBy(bar => bar.Date)
            .Select(bar => new CloseBar(bar.Date, bar.Close))
            .ToListAsync(cancellationToken);
        if (bars.Count == 0)
        {
            return null;
        }

        return (bars, meta?.Provider ?? "seed", meta?.FetchedAtUtc ?? DateTime.UnixEpoch);
    }

    private async Task SaveAsync(string ticker, IReadOnlyList<CloseBar> bars, string provider, CancellationToken cancellationToken)
    {
        await _db.PriceBars.Where(bar => bar.Ticker == ticker).ExecuteDeleteAsync(cancellationToken);
        _db.PriceBars.AddRange(bars.Select(bar => new PriceBar
        {
            Ticker = ticker,
            Date = bar.Date,
            Close = bar.Close
        }));

        var meta = await _db.TickerCaches.FirstOrDefaultAsync(cache => cache.Ticker == ticker, cancellationToken);
        if (meta is null)
        {
            _db.TickerCaches.Add(new TickerCache
            {
                Ticker = ticker,
                Provider = provider,
                FetchedAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            meta.Provider = provider;
            meta.FetchedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<(List<CloseBar> Bars, string Provider)> FetchLiveAsync(string ticker, CancellationToken cancellationToken)
    {
        Exception? yahooError = null;
        try
        {
            var yahooRoot = _options.YahooBaseUrl.TrimEnd('/');
            var yahooUrl = $"{yahooRoot}/v8/finance/chart/{Uri.EscapeDataString(ticker)}?range=5y&interval=1d&includeAdjustedClose=true";
            var body = await GetStringAsync(yahooUrl, cancellationToken);
            var bars = ParseYahoo(body);
            return (bars, "yahoo");
        }
        catch (Exception ex) when (ShouldFallBack(ex, cancellationToken))
        {
            yahooError = ex;
        }

        try
        {
            var stooqRoot = _options.StooqBaseUrl.TrimEnd('/');
            var stooqSymbol = ticker.Replace("-", ".").ToLowerInvariant() + ".us";
            var stooqUrl = $"{stooqRoot}/q/d/?s={Uri.EscapeDataString(stooqSymbol)}&i=d";
            var body = await GetStringAsync(stooqUrl, cancellationToken);
            var bars = ParseStooq(body);
            return (bars, "stooq");
        }
        catch (Exception ex) when (ShouldFallBack(ex, cancellationToken))
        {
            throw new InvalidOperationException("Yahoo and Stooq both failed.", new AggregateException(yahooError!, ex));
        }
    }

    internal static bool ShouldFallBack(Exception exception, CancellationToken cancellationToken)
    {
        return exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested;
    }

    private async Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        var client = _http.CreateClient("market");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        using var response = await client.GetAsync(url, timeout.Token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(timeout.Token);
    }

    internal static List<CloseBar> ParseYahoo(string json)
    {
        using var document = JsonDocument.Parse(json);
        var chart = document.RootElement.GetProperty("chart");
        if (chart.TryGetProperty("error", out var error) && error.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            throw new InvalidOperationException("Yahoo returned an error for that ticker.");
        }

        if (!chart.TryGetProperty("result", out var results) || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Yahoo returned no prices.");
        }

        var result = results[0];
        if (!result.TryGetProperty("timestamp", out var timestamps) || timestamps.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Yahoo returned no prices.");
        }

        var indicators = result.GetProperty("indicators");
        JsonElement? adjusted = null;
        if (indicators.TryGetProperty("adjclose", out var adjustedNode)
            && adjustedNode.ValueKind == JsonValueKind.Array
            && adjustedNode.GetArrayLength() > 0
            && adjustedNode[0].TryGetProperty("adjclose", out var adjustedValues))
        {
            adjusted = adjustedValues;
        }

        var closes = indicators.GetProperty("quote")[0].GetProperty("close");
        var zone = NewYork();
        var byDate = new Dictionary<DateOnly, double>();
        var index = 0;
        foreach (var timestamp in timestamps.EnumerateArray())
        {
            double? close = ReadNullable(adjusted, index) ?? ReadNullable(closes, index);
            index++;
            if (close is not > 0 || timestamp.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            var utc = DateTimeOffset.FromUnixTimeSeconds(timestamp.GetInt64()).UtcDateTime;
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
            byDate[DateOnly.FromDateTime(local)] = close.Value;
        }

        var bars = byDate.OrderBy(pair => pair.Key).Select(pair => new CloseBar(pair.Key, pair.Value)).ToList();
        if (bars.Count < 40)
        {
            throw new InvalidOperationException("Yahoo returned too little history.");
        }

        return bars;
    }

    internal static List<CloseBar> ParseStooq(string csv)
    {
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2 || !lines[0].StartsWith("Date", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Stooq returned an unexpected file.");
        }

        var byDate = new Dictionary<DateOnly, double>();
        foreach (var line in lines.Skip(1))
        {
            var parts = line.Split(',');
            if (parts.Length < 5)
            {
                continue;
            }

            if (!DateOnly.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                continue;
            }

            if (!double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var close) || close <= 0)
            {
                continue;
            }

            byDate[date] = close;
        }

        var bars = byDate.OrderBy(pair => pair.Key).Select(pair => new CloseBar(pair.Key, pair.Value)).ToList();
        if (bars.Count < 40)
        {
            throw new InvalidOperationException("Stooq returned too little history.");
        }

        return bars;
    }

    private static double? ReadNullable(JsonElement? array, int index)
    {
        if (array is not JsonElement element || element.ValueKind != JsonValueKind.Array || index >= element.GetArrayLength())
        {
            return null;
        }

        var value = element[index];
        return value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;
    }

    private static TimeZoneInfo NewYork()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private sealed class SeedFile
    {
        public string Ticker { get; set; } = "";

        public List<SeedBar> Bars { get; set; } = [];
    }

    private sealed class SeedBar
    {
        public string Date { get; set; } = "";

        public double Close { get; set; }
    }
}

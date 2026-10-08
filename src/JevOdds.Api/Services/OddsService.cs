using System.Globalization;
using System.Text;
using JevOdds.Api.Data;
using JevOdds.Api.Pricing;
using Microsoft.Extensions.Options;

namespace JevOdds.Api.Services;

public sealed class OddsService
{
    private readonly MarketDataService _market;
    private readonly OddsDbContext _db;
    private readonly ILogger<OddsService> _logger;
    private readonly int _paths;
    private readonly int _seed;

    public OddsService(MarketDataService market, OddsDbContext db, IOptions<OddsOptions> options, ILogger<OddsService> logger)
    {
        _market = market;
        _db = db;
        _logger = logger;
        _paths = Math.Clamp(options.Value.MonteCarloPaths, 1_000, 50_000);
        _seed = options.Value.MonteCarloSeed;
    }

    public async Task<MarketSnapshot> SnapshotAsync(string rawTicker, CancellationToken cancellationToken)
    {
        if (!TickerCatalog.TryNormalize(rawTicker, out var ticker))
        {
            throw new OddsException("Enter a ticker using letters, numbers, or a hyphen.");
        }

        var series = await _market.GetAsync(ticker, cancellationToken);
        var closes = series.Bars.Select(bar => bar.Close).ToArray();
        var last = series.Bars[^1];
        var vol20 = ReturnsMath.AnnualizedVolatility(closes, 20);
        var vol60 = ReturnsMath.AnnualizedVolatility(closes, 60);
        var vol252 = ReturnsMath.AnnualizedVolatility(closes, 252);
        return new MarketSnapshot
        {
            Ticker = ticker,
            Name = TickerCatalog.NameOf(ticker),
            LastClose = last.Close,
            AsOf = last.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Origin = series.Origin,
            Provider = series.Provider,
            Bars = series.Bars.Count,
            Vol20 = vol20,
            Vol60 = vol60,
            Vol252 = vol252,
            VolBlend = ReturnsMath.BlendVolatility(vol20, vol60, vol252),
            LogDriftAnnual = ReturnsMath.AnnualizedLogMean(closes, Math.Min(252, Math.Max(20, closes.Length - 1)))
        };
    }

    public async Task<OddsResponse> CalculateAsync(OddsRequest request, CancellationToken cancellationToken)
    {
        if (!TickerCatalog.TryNormalize(request.Ticker, out var ticker))
        {
            throw new OddsException("Enter a ticker using letters, numbers, or a hyphen.");
        }

        var direction = (request.Direction ?? "").Trim().ToLowerInvariant();
        if (direction is not ("up" or "down" or "either"))
        {
            throw new OddsException("Direction must be up, down, or either.");
        }

        var drift = (request.Drift ?? "zero").Trim().ToLowerInvariant();
        if (drift is not ("zero" or "historical"))
        {
            throw new OddsException("Drift must be zero or historical.");
        }

        var window = (request.VolWindow ?? "60").Trim().ToLowerInvariant();
        if (window is not ("20" or "60" or "252" or "blend"))
        {
            throw new OddsException("Volatility window must be 20, 60, 252, or blend.");
        }

        if (direction == "up")
        {
            if (request.Percent is <= 0 or > 400)
            {
                throw new OddsException("Enter a percent move greater than 0 and at most 400.");
            }
        }
        else if (request.Percent is <= 0 or > 90)
        {
            throw new OddsException("Enter a percent move greater than 0 and at most 90 for down or either.");
        }

        if (request.VolOverridePercent is <= 0 or > 400)
        {
            throw new OddsException("Volatility override must be greater than 0 and at most 400 percent.");
        }

        var series = await _market.GetAsync(ticker, cancellationToken);
        var closes = series.Bars.Select(bar => bar.Close).ToArray();
        var last = series.Bars[^1];
        if (request.TargetDate <= last.Date)
        {
            throw new OddsException("Target date is on or before the last close. Pick a later date.");
        }

        if (request.TargetDate > last.Date.AddYears(5))
        {
            throw new OddsException("Pick a target date within 5 years of the last close.");
        }

        var tradingDays = TradingCalendar.CountTradingDays(last.Date, request.TargetDate);
        var effective = TradingCalendar.EffectiveSession(last.Date, request.TargetDate);
        if (tradingDays < 1 || effective is null)
        {
            throw new OddsException("No trading sessions between the last close and that date.");
        }

        var years = tradingDays / 252.0;
        var vol20 = ReturnsMath.AnnualizedVolatility(closes, 20);
        var vol60 = ReturnsMath.AnnualizedVolatility(closes, 60);
        var vol252 = ReturnsMath.AnnualizedVolatility(closes, 252);
        var blend = ReturnsMath.BlendVolatility(vol20, vol60, vol252);
        var (sigma, sampleDays, volSource) = ChooseVolatility(window, request.VolOverridePercent, vol20, vol60, vol252, blend);
        var driftWindow = vol252 is not null ? 252 : vol60 is not null ? 60 : 20;
        var logMean = ReturnsMath.AnnualizedLogMean(closes, driftWindow);
        if (drift == "historical" && logMean is null)
        {
            throw new OddsException("Not enough history to estimate a historical mean.");
        }

        var mu = drift == "historical" ? logMean!.Value + 0.5 * sigma * sigma : 0;
        var nu = ProbabilityMath.Nu(mu, sigma);
        double? upper = direction is "up" or "either" ? last.Close * (1 + request.Percent / 100.0) : null;
        double? lower = direction is "down" or "either" ? last.Close * (1 - request.Percent / 100.0) : null;

        double analyticClose;
        double analyticTouch;
        if (direction == "up")
        {
            analyticClose = ProbabilityMath.CloseUp(last.Close, upper!.Value, mu, sigma, years);
            analyticTouch = ProbabilityMath.TouchUp(last.Close, upper.Value, mu, sigma, years);
        }
        else if (direction == "down")
        {
            analyticClose = ProbabilityMath.CloseDown(last.Close, lower!.Value, mu, sigma, years);
            analyticTouch = ProbabilityMath.TouchDown(last.Close, lower.Value, mu, sigma, years);
        }
        else
        {
            analyticClose = ProbabilityMath.CloseUp(last.Close, upper!.Value, mu, sigma, years)
                + ProbabilityMath.CloseDown(last.Close, lower!.Value, mu, sigma, years);
            analyticTouch = ProbabilityMath.TouchEither(last.Close, lower.Value, upper.Value, mu, sigma, years);
            analyticClose = Math.Min(1, analyticClose);
        }

        var steps = Math.Clamp(tradingDays, 1, 80);
        var mc = ProbabilityMath.MonteCarlo(last.Close, upper, lower, mu, sigma, years, steps, _paths, _seed);
        var (low, high) = ProbabilityMath.OneSigmaRange(last.Close, mu, sigma, years);
        var density = ProbabilityMath.TerminalDensity(last.Close, mu, sigma, years)
            .Select(point => new DensityPoint(point.Price, point.Density))
            .ToArray();
        var empirical = ReturnsMath.Empirical(closes, tradingDays, request.Percent, direction);
        var formula = BuildFormula(
            ticker,
            direction,
            request.Percent,
            last,
            upper,
            lower,
            sigma,
            volSource,
            sampleDays,
            mu,
            nu,
            drift,
            driftWindow,
            tradingDays,
            effective.Value,
            years,
            steps);

        var response = new OddsResponse
        {
            Ticker = ticker,
            Name = TickerCatalog.NameOf(ticker),
            Direction = direction,
            Percent = request.Percent,
            Spot = last.Close,
            AsOf = last.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TargetDate = request.TargetDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            EffectiveDate = effective.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TradingDays = tradingDays,
            Years = years,
            Sigma = sigma,
            VolSource = volSource,
            VolSampleDays = sampleDays,
            Vol20 = vol20,
            Vol60 = vol60,
            Vol252 = vol252,
            VolBlend = blend,
            Drift = drift,
            Mu = mu,
            Nu = nu,
            Upper = upper,
            Lower = lower,
            AnalyticClose = analyticClose,
            AnalyticTouch = analyticTouch,
            MonteCarloClose = mc.CloseBeyond,
            MonteCarloTouch = mc.Touch,
            MonteCarloPaths = _paths,
            MonteCarloSeed = _seed,
            ExpectedLow = low,
            ExpectedHigh = high,
            EmpiricalClose = empirical.Close,
            EmpiricalTouch = empirical.Touch,
            EmpiricalSamples = empirical.Samples,
            Origin = series.Origin,
            Provider = series.Provider,
            Density = density,
            Paths = mc.SamplePaths,
            Formula = formula
        };

        await RememberAsync(response, window, request.VolOverridePercent, cancellationToken);
        return response;
    }

    private async Task RememberAsync(OddsResponse response, string window, double? volOverridePercent, CancellationToken cancellationToken)
    {
        try
        {
            _db.QueryRecords.Add(new QueryRecord
            {
                Ticker = response.Ticker,
                Percent = response.Percent,
                Direction = response.Direction,
                TargetDate = DateOnly.Parse(response.TargetDate, CultureInfo.InvariantCulture),
                VolWindow = window,
                VolOverridePercent = volOverridePercent,
                Drift = response.Drift,
                Spot = response.Spot,
                Sigma = response.Sigma,
                AnalyticClose = response.AnalyticClose,
                AnalyticTouch = response.AnalyticTouch,
                MonteCarloClose = response.MonteCarloClose,
                MonteCarloTouch = response.MonteCarloTouch,
                Origin = response.Origin,
                Provider = response.Provider,
                CreatedAtUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not store query history for {Ticker}", response.Ticker);
        }
    }

    private static (double Sigma, int SampleDays, string Source) ChooseVolatility(
        string window,
        double? overridePercent,
        double? vol20,
        double? vol60,
        double? vol252,
        double? blend)
    {
        if (overridePercent is double percent)
        {
            var sample = window switch
            {
                "20" => 20,
                "252" => 252,
                "blend" => 252,
                _ => 60
            };
            return (percent / 100.0, sample, "override");
        }

        return window switch
        {
            "20" => vol20 is double value20
                ? (value20, 20, "20")
                : throw new OddsException("Not enough history for 20 day volatility. Pick another window or enter an override."),
            "252" => vol252 is double value252
                ? (value252, 252, "252")
                : throw new OddsException("Not enough history for 252 day volatility. Pick another window or enter an override."),
            "blend" => blend is double valueBlend
                ? (valueBlend, 0, "blend")
                : throw new OddsException("Not enough history for a blended volatility. Enter an override."),
            _ => vol60 is double value60
                ? (value60, 60, "60")
                : throw new OddsException("Not enough history for 60 day volatility. Pick another window or enter an override.")
        };
    }

    private string BuildFormula(
        string ticker,
        string direction,
        double percent,
        CloseBar last,
        double? upper,
        double? lower,
        double sigma,
        string volSource,
        int sampleDays,
        double mu,
        double nu,
        string drift,
        int driftWindow,
        int tradingDays,
        DateOnly effective,
        double years,
        int steps)
    {
        var text = new StringBuilder();
        text.AppendLine("Model");
        text.AppendLine("  dS/S = mu dt + sigma dW");
        text.AppendLine("  Log drift nu = mu - sigma^2 / 2");
        text.AppendLine("  T = trading days / 252");
        text.AppendLine("  N(x) is the standard normal CDF");
        text.AppendLine();
        text.AppendLine("Inputs");
        text.AppendLine($"  Ticker {ticker}");
        text.AppendLine($"  Spot S = {Money(last.Close)} on {last.Date:yyyy-MM-dd}");
        text.AppendLine($"  Move = {percent.ToString("0.##", CultureInfo.InvariantCulture)} percent, direction = {direction}");
        if (upper is double up)
        {
            text.AppendLine($"  Upper target = {Money(up)}");
        }

        if (lower is double down)
        {
            text.AppendLine($"  Lower target = {Money(down)}");
        }

        var volLabel = volSource switch
        {
            "override" => "override",
            "blend" => "equal-weight blend of the 20, 60, and 252 day windows that have enough history",
            "20" => "20 day realized volatility",
            "252" => "252 day realized volatility",
            _ => "60 day realized volatility"
        };
        text.AppendLine($"  sigma = {sigma.ToString("0.0000", CultureInfo.InvariantCulture)} ({volLabel})");
        if (volSource != "blend" && volSource != "override")
        {
            text.AppendLine($"  Volatility sample = {sampleDays} daily log returns");
        }

        if (drift == "historical")
        {
            text.AppendLine($"  Drift = historical. mu = mean log return over {driftWindow} days, annualized, plus sigma^2 / 2.");
            text.AppendLine("  That sets the model log drift equal to the historical mean log return.");
        }
        else
        {
            text.AppendLine("  Drift = zero. mu = 0, so nu = -sigma^2 / 2.");
        }

        text.AppendLine($"  mu = {mu.ToString("0.0000", CultureInfo.InvariantCulture)}");
        text.AppendLine($"  nu = {nu.ToString("0.0000", CultureInfo.InvariantCulture)}");
        text.AppendLine($"  Trading days = {tradingDays}");
        text.AppendLine($"  T = {years.ToString("0.0000", CultureInfo.InvariantCulture)} years");
        text.AppendLine($"  Effective session = {effective:yyyy-MM-dd}");
        text.AppendLine();
        text.AppendLine("Close beyond");
        text.AppendLine("  Up:   N( (ln(S/K) + nu T) / (sigma sqrt(T)) )");
        text.AppendLine("  Down: N( (ln(K/S) - nu T) / (sigma sqrt(T)) )");
        text.AppendLine("  At or past the target counts.");
        if (direction == "either")
        {
            text.AppendLine("  Either: P(up) + P(down). The two tails do not overlap.");
        }

        text.AppendLine();
        text.AppendLine("Touch");
        text.AppendLine("  Up, a = ln(K/S):");
        text.AppendLine("    N( (-a + nu T) / (sigma sqrt(T)) ) + exp(2 nu a / sigma^2) N( (-a - nu T) / (sigma sqrt(T)) )");
        text.AppendLine("  Down uses the matching lower-barrier formula.");
        if (direction == "either")
        {
            text.AppendLine("  Either: 1 minus the chance the log price stays between the barriers.");
            text.AppendLine("  That chance is a Fourier sine series for Brownian motion with drift.");
        }

        text.AppendLine();
        text.AppendLine("Monte Carlo");
        text.AppendLine($"  {_paths} geometric Brownian paths, seed {_seed}.");
        text.AppendLine($"  {steps} steps across the horizon, with a Brownian bridge for a hit between steps.");
        text.AppendLine();
        text.AppendLine("Empirical");
        text.AppendLine("  Share of past windows of this many sessions whose daily closes finished at or past the move,");
        text.AppendLine("  and the share whose closes touched it. Closes only, so this is not the continuous touch probability.");
        text.AppendLine();
        text.AppendLine("1 sigma range");
        text.AppendLine("  S * exp(nu T - sigma sqrt(T)) to S * exp(nu T + sigma sqrt(T))");
        return text.ToString().TrimEnd();
    }

    private static string Money(double value)
    {
        return value >= 1
            ? value.ToString("0.00", CultureInfo.InvariantCulture)
            : value.ToString("0.0000", CultureInfo.InvariantCulture);
    }
}

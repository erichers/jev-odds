using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace JevOdds.Api.Services;

public sealed class PdfReportService
{
    static PdfReportService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Build(OddsResponse odds, string? resultUrl = null)
    {
        var direction = odds.Direction switch
        {
            "down" => "down",
            "either" => "up or down",
            _ => "up"
        };

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(style => style.FontSize(11).FontColor(Colors.Black));
                page.Header().Column(column =>
                {
                    column.Item().Text("Jev Odds").FontSize(22).SemiBold();
                    column.Item().Text($"{odds.Ticker} {direction} {odds.Percent.ToString("0.##", CultureInfo.InvariantCulture)}% by {odds.TargetDate}")
                        .FontSize(13);
                });
                page.Content().PaddingVertical(16).Column(column =>
                {
                    column.Spacing(8);
                    column.Item().Text($"Close beyond  {Percent(odds.AnalyticClose)} analytic,  {Percent(odds.MonteCarloClose)} Monte Carlo").FontSize(14);
                    column.Item().Text($"Touch  {Percent(odds.AnalyticTouch)} analytic,  {Percent(odds.MonteCarloTouch)} Monte Carlo").FontSize(14);
                    column.Item().Text($"Spot {Money(odds.Spot)} as of {odds.AsOf}. {odds.TradingDays} trading days to {odds.EffectiveDate}.");
                    column.Item().Text($"1 sigma range {Money(odds.ExpectedLow)} to {Money(odds.ExpectedHigh)}.");
                    column.Item().Text($"Volatility {odds.Sigma.ToString("0.00%", CultureInfo.InvariantCulture)} ({odds.VolSource}). Drift {odds.Drift}. mu {odds.Mu.ToString("0.00%", CultureInfo.InvariantCulture)}.");
                    column.Item().Text(odds.Origin == "live"
                        ? $"Data: live, {ProviderName(odds.Provider)}."
                        : $"Data: cached, {ProviderName(odds.Provider)}.");
                    if (odds.EmpiricalSamples > 0 && odds.EmpiricalClose is double empiricalClose && odds.EmpiricalTouch is double empiricalTouch)
                    {
                        column.Item().Text($"Historical frequency over {odds.EmpiricalSamples} windows: close {Percent(empiricalClose)}, touch {Percent(empiricalTouch)}.");
                    }

                    column.Item().PaddingTop(8).Text("How this was computed").SemiBold();
                    column.Item().Text(odds.Formula).FontSize(8).FontColor(Colors.Grey.Darken3);
                });
                page.Footer().Column(column =>
                {
                    if (!string.IsNullOrWhiteSpace(resultUrl))
                    {
                        column.Item().Text(resultUrl).FontSize(8).FontColor(Colors.Grey.Darken2);
                    }

                    column.Item().Text("Educational tool. Not financial advice.").SemiBold();
                    column.Item().Text("by Ulric studio").FontSize(9).FontColor(Colors.Grey.Darken2);
                });
            });
        }).GeneratePdf();
    }

    private static string Percent(double probability) => probability.ToString("0.0%", CultureInfo.InvariantCulture);

    private static string Money(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string ProviderName(string provider) => provider switch
    {
        "yahoo" => "Yahoo Finance",
        "stooq" => "Stooq",
        "seed" => "shipped sample",
        _ => provider
    };
}

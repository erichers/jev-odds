using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;

namespace JevOdds.Api;

public static class PublicLinks
{
    public static string? ResultPage(string? publicBaseUrl, OddsRequest request, string ticker)
    {
        var root = (publicBaseUrl ?? "").Trim().TrimEnd('/');
        if (root.Length == 0)
        {
            return null;
        }

        var query = new Dictionary<string, string?>
        {
            ["ticker"] = ticker,
            ["percent"] = request.Percent.ToString("0.##", CultureInfo.InvariantCulture),
            ["direction"] = request.Direction,
            ["date"] = request.TargetDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["vol"] = string.IsNullOrWhiteSpace(request.VolWindow) ? "60" : request.VolWindow,
            ["drift"] = string.IsNullOrWhiteSpace(request.Drift) ? "zero" : request.Drift
        };
        if (request.VolOverridePercent is double over)
        {
            query["override"] = over.ToString("0.##", CultureInfo.InvariantCulture);
        }

        return QueryHelpers.AddQueryString(root + "/odds", query);
    }
}

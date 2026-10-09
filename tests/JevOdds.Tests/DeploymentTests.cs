using JevOdds.Api;
using JevOdds.Api.Data;

namespace JevOdds.Tests;

public class DeploymentTests
{
    [Fact]
    public void Public_result_link_uses_the_configured_base()
    {
        var request = new OddsRequest
        {
            Ticker = "nvda",
            Percent = 10,
            Direction = "up",
            TargetDate = new DateOnly(2027, 4, 16),
            VolWindow = "60",
            Drift = "zero"
        };

        var link = PublicLinks.ResultPage("http://localhost:8888/apps/jev-odds/", request, "NVDA");

        Assert.Equal(
            "http://localhost:8888/apps/jev-odds/odds?ticker=NVDA&percent=10&direction=up&date=2027-04-16&vol=60&drift=zero",
            link);
    }

    [Fact]
    public void Public_result_link_is_omitted_without_a_base()
    {
        var request = new OddsRequest { Ticker = "SPY", TargetDate = new DateOnly(2027, 1, 15) };
        Assert.Null(PublicLinks.ResultPage("  ", request, "SPY"));
    }

    [Fact]
    public void Server_version_honors_an_explicit_5_7_value()
    {
        var version = MySqlVersionResolver.Resolve("Server=127.0.0.1;Port=1;", "5.7.39-mysql");
        Assert.Equal("5.7.39-mysql", version.ToString());
    }

    [Fact]
    public void Server_version_falls_back_to_5_7_when_auto_detect_cannot_connect()
    {
        var version = MySqlVersionResolver.Resolve(
            "Server=127.0.0.1;Port=1;User=jev;Password=CHANGE_ME;Connection Timeout=1;",
            "auto");
        Assert.Equal("5.7.39-mysql", version.ToString());
    }

    [Fact]
    public void MySql_connection_string_sets_utf8mb4()
    {
        var withCharset = MySqlVersionResolver.WithUtf8Mb4("Server=127.0.0.1;Database=jev_odds;");
        Assert.Contains("CharSet=utf8mb4", withCharset, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "Server=127.0.0.1;Database=jev_odds;CharSet=utf8mb4;",
            MySqlVersionResolver.WithUtf8Mb4("Server=127.0.0.1;Database=jev_odds;CharSet=utf8mb4;"));
    }
}

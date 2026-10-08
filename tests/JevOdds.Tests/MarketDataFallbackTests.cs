namespace JevOdds.Tests;

public class MarketDataFallbackTests
{
    [Fact]
    public void Timeout_falls_back_when_the_caller_did_not_cancel()
    {
        using var timeout = new CancellationTokenSource();
        timeout.Cancel();
        var exception = new TaskCanceledException("The request timed out.");
        Assert.True(JevOdds.Api.Services.MarketDataService.ShouldFallBack(exception, CancellationToken.None));
        Assert.False(JevOdds.Api.Services.MarketDataService.ShouldFallBack(exception, timeout.Token));
    }
}

using JevOdds.Api.Pricing;

namespace JevOdds.Tests;

public class TradingCalendarTests
{
    [Fact]
    public void Easter_2026_is_April_5_and_Good_Friday_is_closed()
    {
        Assert.Equal(new DateOnly(2026, 4, 5), TradingCalendar.EasterSunday(2026));
        Assert.False(TradingCalendar.IsTradingDay(new DateOnly(2026, 4, 3)));
    }

    [Theory]
    [InlineData(2026, 1, 1)]
    [InlineData(2026, 1, 19)]
    [InlineData(2026, 2, 16)]
    [InlineData(2026, 4, 3)]
    [InlineData(2026, 5, 25)]
    [InlineData(2026, 6, 19)]
    [InlineData(2026, 7, 3)]
    [InlineData(2026, 9, 7)]
    [InlineData(2026, 11, 26)]
    [InlineData(2026, 12, 25)]
    public void Full_day_holidays_in_2026_are_closed(int year, int month, int day)
    {
        var date = new DateOnly(year, month, day);
        Assert.True(TradingCalendar.IsHoliday(date));
        Assert.False(TradingCalendar.IsTradingDay(date));
    }

    [Fact]
    public void Independence_Day_2026_is_observed_on_Friday()
    {
        Assert.True(TradingCalendar.IsHoliday(new DateOnly(2026, 7, 3)));
        Assert.False(TradingCalendar.IsHoliday(new DateOnly(2026, 7, 4)));
        Assert.True(TradingCalendar.IsWeekend(new DateOnly(2026, 7, 4)));
    }

    [Theory]
    [InlineData("2026-10-08", "2026-10-08", 0)]
    [InlineData("2026-10-08", "2026-10-09", 1)]
    [InlineData("2026-10-08", "2026-10-12", 2)]
    [InlineData("2026-10-05", "2026-10-16", 9)]
    [InlineData("2026-12-24", "2026-12-28", 1)]
    [InlineData("2026-07-02", "2026-07-06", 1)]
    public void Counts_sessions_after_the_start_through_the_target(string from, string to, int expected)
    {
        var count = TradingCalendar.CountTradingDays(DateOnly.Parse(from), DateOnly.Parse(to));
        Assert.Equal(expected, count);
    }

    [Fact]
    public void Weekend_target_uses_the_previous_session()
    {
        var from = new DateOnly(2026, 10, 8);
        var saturday = new DateOnly(2026, 10, 10);
        Assert.Equal(new DateOnly(2026, 10, 9), TradingCalendar.EffectiveSession(from, saturday));
        Assert.Equal(1, TradingCalendar.CountTradingDays(from, saturday));
    }
}

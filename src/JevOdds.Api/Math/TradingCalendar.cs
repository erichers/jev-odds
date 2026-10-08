namespace JevOdds.Api.Pricing;

/// <summary>
/// NYSE full-day sessions. Weekends and standard full-day holidays are excluded.
/// Early closes still count as sessions.
/// </summary>
public static class TradingCalendar
{
    private static readonly HashSet<DateOnly> Holidays = BuildHolidays(1990, 2100);

    public static bool IsWeekend(DateOnly date)
    {
        return date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
    }

    public static bool IsHoliday(DateOnly date)
    {
        return Holidays.Contains(date);
    }

    public static bool IsTradingDay(DateOnly date)
    {
        return !IsWeekend(date) && !IsHoliday(date);
    }

    /// <summary>
    /// Sessions after <paramref name="fromExclusive"/> through <paramref name="toInclusive"/>.
    /// </summary>
    public static int CountTradingDays(DateOnly fromExclusive, DateOnly toInclusive)
    {
        if (toInclusive <= fromExclusive)
        {
            return 0;
        }

        var count = 0;
        for (var day = fromExclusive.AddDays(1); day <= toInclusive; day = day.AddDays(1))
        {
            if (IsTradingDay(day))
            {
                count++;
            }
        }

        return count;
    }

    public static DateOnly? EffectiveSession(DateOnly fromExclusive, DateOnly toInclusive)
    {
        if (toInclusive <= fromExclusive)
        {
            return null;
        }

        var day = toInclusive;
        while (day > fromExclusive && !IsTradingDay(day))
        {
            day = day.AddDays(-1);
        }

        return day > fromExclusive && IsTradingDay(day) ? day : null;
    }

    private static HashSet<DateOnly> BuildHolidays(int firstYear, int lastYear)
    {
        var set = new HashSet<DateOnly>();
        for (var year = firstYear; year <= lastYear; year++)
        {
            AddObserved(set, new DateOnly(year, 1, 1));
            AddObserved(set, new DateOnly(year, 6, 19));
            AddObserved(set, new DateOnly(year, 7, 4));
            AddObserved(set, new DateOnly(year, 12, 25));
            set.Add(NthWeekday(year, 1, DayOfWeek.Monday, 3));
            set.Add(NthWeekday(year, 2, DayOfWeek.Monday, 3));
            set.Add(LastWeekday(year, 5, DayOfWeek.Monday));
            set.Add(NthWeekday(year, 9, DayOfWeek.Monday, 1));
            set.Add(NthWeekday(year, 11, DayOfWeek.Thursday, 4));
            set.Add(EasterSunday(year).AddDays(-2));
        }

        return set;
    }

    private static void AddObserved(HashSet<DateOnly> set, DateOnly day)
    {
        if (day.DayOfWeek == DayOfWeek.Saturday)
        {
            set.Add(day.AddDays(-1));
        }
        else if (day.DayOfWeek == DayOfWeek.Sunday)
        {
            set.Add(day.AddDays(1));
        }
        else
        {
            set.Add(day);
        }
    }

    private static DateOnly NthWeekday(int year, int month, DayOfWeek weekday, int n)
    {
        var first = new DateOnly(year, month, 1);
        var shift = ((int)weekday - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(shift + (n - 1) * 7);
    }

    private static DateOnly LastWeekday(int year, int month, DayOfWeek weekday)
    {
        var last = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var shift = ((int)last.DayOfWeek - (int)weekday + 7) % 7;
        return last.AddDays(-shift);
    }

    public static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = ((h + l - 7 * m + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }
}

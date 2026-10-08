using DTIOneLink.Services;

namespace DTIOneLink.Tests;

public class TimeZoneHelperTests
{
    // Philippine time is UTC+8, no daylight saving, year-round.
    [Fact]
    public void ToPhilippineTime_AddsEightHours()
    {
        var utcNoon = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);

        var local = TimeZoneHelper.ToPhilippineTime(utcNoon);

        Assert.Equal(new DateTime(2026, 6, 15, 20, 0, 0), local);
    }

    [Fact]
    public void ToPhilippineTime_CrossesIntoTheNextCalendarDay()
    {
        // 11 PM UTC is 7 AM the next day in Manila.
        var lateUtc = new DateTime(2026, 6, 15, 23, 0, 0, DateTimeKind.Utc);

        var local = TimeZoneHelper.ToPhilippineTime(lateUtc);

        Assert.Equal(new DateTime(2026, 6, 16, 7, 0, 0), local);
    }

    [Fact]
    public void ToPhilippineTime_TreatsUnspecifiedKindAsUtc()
    {
        // Every real call site stores UTC; a DateTime that lost its Kind
        // (e.g. round-tripped through some serializers) must still be
        // treated as UTC rather than the machine's local time.
        var unspecified = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Unspecified);
        var explicitUtc = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(TimeZoneHelper.ToPhilippineTime(explicitUtc), TimeZoneHelper.ToPhilippineTime(unspecified));
    }

    [Fact]
    public void PhilippineDateStartUtc_RoundTripsBackToTheSamePhilippineDate()
    {
        var philippineDate = new DateTime(2026, 3, 10);

        var utcStart = TimeZoneHelper.PhilippineDateStartUtc(philippineDate);

        // Midnight in Manila is 16:00 UTC the previous day (UTC+8).
        Assert.Equal(new DateTime(2026, 3, 9, 16, 0, 0, DateTimeKind.Utc), utcStart);
        Assert.Equal(philippineDate.Date, TimeZoneHelper.ToPhilippineTime(utcStart).Date);
    }

    [Fact]
    public void PhilippineToday_MatchesManualConversionOfUtcNow()
    {
        var expected = TimeZoneHelper.ToPhilippineTime(DateTime.UtcNow).Date;

        Assert.Equal(expected, TimeZoneHelper.PhilippineToday);
    }
}

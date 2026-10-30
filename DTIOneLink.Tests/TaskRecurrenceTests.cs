using DTIOneLink.Services;

namespace DTIOneLink.Tests;

public class TaskRecurrenceTests
{
    [Theory]
    [InlineData(TaskRecurrence.Daily, true)]
    [InlineData(TaskRecurrence.Weekly, true)]
    [InlineData(TaskRecurrence.Monthly, true)]
    [InlineData(TaskRecurrence.Quarterly, true)]
    [InlineData(null, false)]
    [InlineData("yearly", false)]
    [InlineData("", false)]
    public void IsValid(string? value, bool expected)
    {
        Assert.Equal(expected, TaskRecurrence.IsValid(value));
    }

    [Theory]
    [InlineData(TaskRecurrence.Daily, "Every day")]
    [InlineData(TaskRecurrence.Weekly, "Every week")]
    [InlineData(TaskRecurrence.Monthly, "Every month")]
    [InlineData(TaskRecurrence.Quarterly, "Every quarter")]
    [InlineData(null, "Does not repeat")]
    [InlineData("bogus", "Does not repeat")]
    public void Label(string? value, string expected)
    {
        Assert.Equal(expected, TaskRecurrence.Label(value));
    }

    [Fact]
    public void Advance_Daily_AddsDays()
    {
        Assert.Equal(new DateTime(2026, 1, 5), TaskRecurrence.Advance(new DateTime(2026, 1, 1), TaskRecurrence.Daily, 4));
    }

    [Fact]
    public void Advance_Weekly_AddsSevenDaysPerStep()
    {
        Assert.Equal(new DateTime(2026, 1, 15), TaskRecurrence.Advance(new DateTime(2026, 1, 1), TaskRecurrence.Weekly, 2));
    }

    [Fact]
    public void Advance_Monthly_FromThe31st_ClampsInShorterMonthsThenReturnsTo31st()
    {
        var jan31 = new DateTime(2026, 1, 31);

        // Feb 2026 has 28 days.
        Assert.Equal(new DateTime(2026, 2, 28), TaskRecurrence.Advance(jan31, TaskRecurrence.Monthly, 1));
        // March has 31, but .NET's AddMonths is always computed from the
        // ORIGINAL date (Jan 31), so three months out lands back on the 30th
        // (April has 30 days) rather than drifting from the clamped Feb 28.
        Assert.Equal(new DateTime(2026, 4, 30), TaskRecurrence.Advance(jan31, TaskRecurrence.Monthly, 3));
        // Back to a 31-day month: returns to the 31st.
        Assert.Equal(new DateTime(2026, 7, 31), TaskRecurrence.Advance(jan31, TaskRecurrence.Monthly, 6));
    }

    [Fact]
    public void Advance_Quarterly_AddsThreeMonthsPerStep()
    {
        Assert.Equal(new DateTime(2026, 10, 1), TaskRecurrence.Advance(new DateTime(2026, 1, 1), TaskRecurrence.Quarterly, 3));
    }

    [Fact]
    public void Advance_UnknownFrequency_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TaskRecurrence.Advance(DateTime.Today, "yearly"));
    }

    [Fact]
    public void NextDueDate_WhenPreviousDueDateIsStillInTheFuture_AdvancesExactlyOneInterval()
    {
        var previousDue = new DateTime(2026, 5, 1);
        var today = new DateTime(2026, 4, 20);

        var next = TaskRecurrence.NextDueDate(previousDue, TaskRecurrence.Weekly, today);

        Assert.Equal(new DateTime(2026, 5, 8), next);
    }

    [Fact]
    public void NextDueDate_WhenAppWasOfflineForMultipleIntervals_SkipsAheadPastToday()
    {
        // Daily task, previous due date is far in the past (app offline for
        // a while): must skip ahead to the first daily due date strictly
        // after "today", never create an already-overdue copy.
        var previousDue = new DateTime(2026, 1, 1);
        var today = new DateTime(2026, 1, 10);

        var next = TaskRecurrence.NextDueDate(previousDue, TaskRecurrence.Daily, today);

        Assert.Equal(new DateTime(2026, 1, 11), next);
        Assert.True(next > today);
    }

    [Fact]
    public void NextDueDate_NeverReturnsADateOnOrBeforeToday()
    {
        var previousDue = new DateTime(2026, 1, 1);
        var today = new DateTime(2026, 3, 15); // several months of missed quarters

        var next = TaskRecurrence.NextDueDate(previousDue, TaskRecurrence.Monthly, today);

        Assert.True(next > today);
    }

    // Panelist item T8 (2026-10-31): regenerated recurring copies used to
    // call previousDueDate.Date, discarding the time-of-day on every single
    // repeat — a daily task due at 2:00 PM would come back due at midnight.
    [Theory]
    [InlineData(TaskRecurrence.Daily)]
    [InlineData(TaskRecurrence.Weekly)]
    [InlineData(TaskRecurrence.Monthly)]
    [InlineData(TaskRecurrence.Quarterly)]
    public void NextDueDate_PreservesThePreviousDueDatesTimeOfDay(string frequency)
    {
        var previousDue = new DateTime(2026, 1, 1, 14, 30, 0); // 2:30 PM
        var today = new DateTime(2026, 1, 1, 0, 0, 0);

        var next = TaskRecurrence.NextDueDate(previousDue, frequency, today);

        Assert.Equal(new TimeSpan(14, 30, 0), next.TimeOfDay);
    }

    [Fact]
    public void NextDueDate_WithTimeOfDay_CrossesMonthBoundaryCorrectly()
    {
        // Jan 31, 11:00 PM, monthly repeat -> Feb has only 28 days in 2026.
        var previousDue = new DateTime(2026, 1, 31, 23, 0, 0);
        var today = new DateTime(2026, 2, 1);

        var next = TaskRecurrence.NextDueDate(previousDue, TaskRecurrence.Monthly, today);

        Assert.Equal(new DateTime(2026, 2, 28, 23, 0, 0), next);
    }

    [Fact]
    public void NextDueDate_WithTimeOfDay_CrossesYearBoundaryCorrectly()
    {
        // Dec 20, 9:15 AM, weekly repeat -> lands in January of the next year.
        var previousDue = new DateTime(2026, 12, 20, 9, 15, 0);
        var today = new DateTime(2026, 12, 20);

        var next = TaskRecurrence.NextDueDate(previousDue, TaskRecurrence.Weekly, today);

        Assert.Equal(new DateTime(2026, 12, 27, 9, 15, 0), next);
    }

    [Fact]
    public void NextDueDate_WithTimeOfDay_StillSkipsAheadPastTodayWhenAppWasOffline()
    {
        // Daily task due 6:00 AM, app offline for several days — must still
        // land strictly after "today" (not just after today's date, but
        // after today's actual moment) while keeping the 6:00 AM time.
        var previousDue = new DateTime(2026, 1, 1, 6, 0, 0);
        var today = new DateTime(2026, 1, 10, 7, 0, 0); // past this run's 6 AM slot already

        var next = TaskRecurrence.NextDueDate(previousDue, TaskRecurrence.Daily, today);

        Assert.Equal(new DateTime(2026, 1, 11, 6, 0, 0), next);
        Assert.True(next > today);
    }

    [Fact]
    public void NextDueDate_WithNoTimeOfDay_StaysBackwardCompatible()
    {
        // Existing tasks created before this fix (or without a meaningful
        // time component) have DueDate at midnight — must behave exactly as
        // before: next due date also lands at midnight.
        var previousDue = new DateTime(2026, 5, 1); // midnight
        var today = new DateTime(2026, 4, 20);

        var next = TaskRecurrence.NextDueDate(previousDue, TaskRecurrence.Weekly, today);

        Assert.Equal(new DateTime(2026, 5, 8), next);
        Assert.Equal(TimeSpan.Zero, next.TimeOfDay);
    }
}

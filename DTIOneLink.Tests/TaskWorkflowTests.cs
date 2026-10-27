using DTIOneLink.Services;

namespace DTIOneLink.Tests;

public class TaskWorkflowTests
{
    [Theory]
    [InlineData(null, TaskWorkflow.Pending)]
    [InlineData("", TaskWorkflow.Pending)]
    [InlineData("   ", TaskWorkflow.Pending)]
    [InlineData("In-Progress", TaskWorkflow.InProgress)]
    [InlineData("  completed  ", TaskWorkflow.Completed)]
    public void Normalize_HandlesNullBlankAndCasing(string? input, string expected)
    {
        Assert.Equal(expected, TaskWorkflow.Normalize(input));
    }

    [Theory]
    // Legal forward edges.
    [InlineData(TaskWorkflow.Pending, TaskWorkflow.InProgress, true)]
    [InlineData(TaskWorkflow.Pending, TaskWorkflow.ForReview, true)]
    [InlineData(TaskWorkflow.InProgress, TaskWorkflow.ForReview, true)]
    [InlineData(TaskWorkflow.ForReview, TaskWorkflow.Completed, true)]
    [InlineData(TaskWorkflow.ForReview, TaskWorkflow.ReturnedForCorrection, true)]
    [InlineData(TaskWorkflow.ReturnedForCorrection, TaskWorkflow.ForReview, true)]
    // Same-state "transition" (progress save) is always allowed.
    [InlineData(TaskWorkflow.InProgress, TaskWorkflow.InProgress, true)]
    [InlineData(TaskWorkflow.Completed, TaskWorkflow.Completed, true)]
    // Illegal edges: terminal state, skipping review, going backwards.
    [InlineData(TaskWorkflow.Completed, TaskWorkflow.InProgress, false)]
    [InlineData(TaskWorkflow.Pending, TaskWorkflow.Completed, false)]
    [InlineData(TaskWorkflow.InProgress, TaskWorkflow.Pending, false)]
    [InlineData(TaskWorkflow.ForReview, TaskWorkflow.InProgress, false)]
    [InlineData(TaskWorkflow.ReturnedForCorrection, TaskWorkflow.Completed, false)]
    public void CanTransition_OnlyAllowsLegalEdges(string current, string next, bool expected)
    {
        Assert.Equal(expected, TaskWorkflow.CanTransition(current, next));
    }

    [Fact]
    public void IsOverdue_PastDueAndNotCompleted_IsTrue()
    {
        var yesterday = TimeZoneHelper.PhilippineToday.AddDays(-1);
        Assert.True(TaskWorkflow.IsOverdue(TaskWorkflow.InProgress, yesterday));
    }

    [Fact]
    public void IsOverdue_PastDueButCompleted_IsFalse()
    {
        var yesterday = TimeZoneHelper.PhilippineToday.AddDays(-1);
        Assert.False(TaskWorkflow.IsOverdue(TaskWorkflow.Completed, yesterday));
    }

    [Fact]
    public void IsOverdue_DueToday_IsNotYetOverdue()
    {
        Assert.False(TaskWorkflow.IsOverdue(TaskWorkflow.InProgress, TimeZoneHelper.PhilippineToday));
    }

    [Theory]
    [InlineData(0, true)]  // due today
    [InlineData(3, true)]  // boundary: within DueSoonDays
    [InlineData(4, false)] // just outside the window
    public void IsDueSoonOrOverdue_RespectsTheWindow(int daysFromToday, bool expected)
    {
        var due = TimeZoneHelper.PhilippineToday.AddDays(daysFromToday);
        Assert.Equal(expected, TaskWorkflow.IsDueSoonOrOverdue(TaskWorkflow.InProgress, due));
    }

    [Fact]
    public void IsDueSoonOrOverdue_CompletedTask_NeverFlagged()
    {
        var due = TimeZoneHelper.PhilippineToday; // due today, would otherwise be flagged
        Assert.False(TaskWorkflow.IsDueSoonOrOverdue(TaskWorkflow.Completed, due));
    }

    [Fact]
    public void DisplayStatus_OverlaysOverdueWithoutChangingStoredStatus()
    {
        var yesterday = TimeZoneHelper.PhilippineToday.AddDays(-1);
        Assert.Equal("overdue", TaskWorkflow.DisplayStatus(TaskWorkflow.InProgress, yesterday));
        Assert.Equal(TaskWorkflow.InProgress, TaskWorkflow.Normalize(TaskWorkflow.InProgress));
    }

    [Fact]
    public void DelayRisk_NullForCompletedAndAlreadyOverdue()
    {
        var future = TimeZoneHelper.PhilippineToday.AddDays(10);
        var past = TimeZoneHelper.PhilippineToday.AddDays(-1);
        var created = DateTime.UtcNow.AddDays(-5);

        Assert.Null(TaskWorkflow.DelayRisk(TaskWorkflow.Completed, 100, created, future, null));
        Assert.Null(TaskWorkflow.DelayRisk(TaskWorkflow.InProgress, 10, created, past, null)); // already overdue
    }

    [Fact]
    public void DelayRisk_ForReviewWithNoReviewDueDate_IsNull()
    {
        // A ForReview row from before ReviewDueDate existed (or any other
        // way it never got set) — fail safe rather than guess.
        var future = TimeZoneHelper.PhilippineToday.AddDays(10);
        var created = DateTime.UtcNow.AddDays(-5);

        Assert.Null(TaskWorkflow.DelayRisk(TaskWorkflow.ForReview, 80, created, future, reviewDueDate: null));
    }

    [Fact]
    public void DelayRisk_ForReviewPastItsReviewDueDate_IsFlaggedAtRisk()
    {
        // The previously-invisible gap this feature closes: a submission
        // sitting past its review window must show up as at-risk, judged
        // against ReviewDueDate — not the task's own (possibly still far
        // off) due date.
        var created = DateTime.UtcNow.AddDays(-5);
        var dueDate = TimeZoneHelper.PhilippineToday.AddDays(30); // irrelevant once ForReview
        var reviewDueDate = DateTime.UtcNow.AddDays(-1); // review window already passed

        var result = TaskWorkflow.DelayRisk(TaskWorkflow.ForReview, 100, created, dueDate, reviewDueDate);

        Assert.NotNull(result);
        Assert.True(result!.Value.AtRisk);
    }

    [Fact]
    public void DelayRisk_ForReviewStillWithinItsReviewWindow_IsNotAtRisk()
    {
        var created = DateTime.UtcNow.AddDays(-5);
        var dueDate = TimeZoneHelper.PhilippineToday.AddDays(30);
        var reviewDueDate = DateTime.UtcNow.AddDays(2); // review window still open

        var result = TaskWorkflow.DelayRisk(TaskWorkflow.ForReview, 100, created, dueDate, reviewDueDate);

        Assert.NotNull(result);
        Assert.False(result!.Value.AtRisk);
    }

    [Fact]
    public void DelayRisk_LowProgressNearDueDate_IsFlaggedAtRisk()
    {
        var created = DateTime.UtcNow.AddDays(-5);
        var dueSoon = TimeZoneHelper.PhilippineToday.AddDays(1); // within DueSoonDays

        var result = TaskWorkflow.DelayRisk(TaskWorkflow.InProgress, progress: 10, created, dueSoon, null);

        Assert.NotNull(result);
        Assert.True(result!.Value.AtRisk);
    }

    [Fact]
    public void DelayRisk_HighProgressWellWithinSchedule_IsNotAtRisk()
    {
        var created = DateTime.UtcNow.AddDays(-2);
        var farOut = TimeZoneHelper.PhilippineToday.AddDays(30);

        var result = TaskWorkflow.DelayRisk(TaskWorkflow.InProgress, progress: 90, created, farOut, null);

        Assert.NotNull(result);
        Assert.False(result!.Value.AtRisk);
    }

    [Theory]
    [InlineData(0)]   // exactly midnight: treated as "by end of day"
    [InlineData(13)]  // 1 PM: has a real time-of-day
    [InlineData(23)]  // 11 PM: has a real time-of-day
    public void EffectiveDueMoment_MidnightMeansEndOfDay_OtherwiseUnchanged(int hour)
    {
        var due = TimeZoneHelper.PhilippineToday.AddHours(hour);
        var expected = hour == 0 ? due.AddDays(1) : due;

        Assert.Equal(expected, TaskWorkflow.EffectiveDueMoment(due));
    }

    [Fact]
    public void IsOverdue_DueAtASpecificTimeToday_BecomesOverdueOnlyAfterThatTime()
    {
        var anHourAgo = TimeZoneHelper.PhilippineNow.AddHours(-1);
        var inAnHour = TimeZoneHelper.PhilippineNow.AddHours(1);

        Assert.True(TaskWorkflow.IsOverdue(TaskWorkflow.InProgress, anHourAgo));
        Assert.False(TaskWorkflow.IsOverdue(TaskWorkflow.InProgress, inAnHour));
    }

    [Theory]
    [InlineData(TaskWorkflow.InProgress, "In Progress")]
    [InlineData(TaskWorkflow.ForReview, "For Review")]
    [InlineData(TaskWorkflow.ReturnedForCorrection, "Returned for Correction")]
    [InlineData(TaskWorkflow.Completed, "Completed")]
    [InlineData(TaskWorkflow.Pending, "To Do")]
    public void DisplayLabel_MapsEachStatusToItsOwnLabel(string status, string expectedLabel)
    {
        var future = TimeZoneHelper.PhilippineToday.AddDays(5);
        Assert.Equal(expectedLabel, TaskWorkflow.DisplayLabel(status, future));
    }

    [Fact]
    public void DisplayLabel_OverdueTakesPriorityOverStoredStatus()
    {
        var yesterday = TimeZoneHelper.PhilippineToday.AddDays(-1);
        Assert.Equal("Overdue", TaskWorkflow.DisplayLabel(TaskWorkflow.InProgress, yesterday));
    }

    [Theory]
    [InlineData(TaskWorkflow.Pending, 0, false)]
    [InlineData(TaskWorkflow.Pending, 79, false)]
    [InlineData(TaskWorkflow.Pending, 80, true)]
    [InlineData(TaskWorkflow.InProgress, 80, true)]
    [InlineData(TaskWorkflow.InProgress, 100, true)]
    // Returning for correction never resets progress, so resubmission is exempt.
    [InlineData(TaskWorkflow.ReturnedForCorrection, 0, true)]
    public void CanSubmitProof_RequiresMinimumProgressUnlessResubmitting(string status, int progress, bool expected)
    {
        Assert.Equal(expected, TaskWorkflow.CanSubmitProof(status, progress));
    }
}

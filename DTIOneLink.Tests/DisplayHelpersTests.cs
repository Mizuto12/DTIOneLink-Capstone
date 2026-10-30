using DTIOneLink.Helpers;
using DTIOneLink.Services;

namespace DTIOneLink.Tests;

// Regression coverage for DueText/DisplayStatus, extracted into
// Helpers/DisplayHelpers.cs during the 2026-10-30 cross-role consistency
// audit from three (DueText) and two (the status-normalization fallback
// chain inside StatusOf) byte-identical copies across Views/Employee/
// Index.cshtml, Views/Shared/_DashboardContent.cshtml, and
// Views/SuperAdmin.cshtml. These tests lock in the exact pre-extraction
// behavior so the refactor can't silently drift from what three dashboards
// used to compute independently.
public class DisplayHelpersTests
{
    private static readonly DateTime Today = new(2026, 10, 30);

    [Theory]
    [InlineData(2026, 10, 25, "5 days late")] // < -1
    [InlineData(2026, 10, 29, "1 day late")]  // -1
    [InlineData(2026, 10, 30, "Due today")]   // 0
    [InlineData(2026, 10, 31, "Due tomorrow")] // 1
    public void DueText_NearDates_MatchesOriginalWording(int y, int m, int d, string expected)
    {
        Assert.Equal(expected, DisplayHelpers.DueText(new DateTime(y, m, d), Today));
    }

    [Fact]
    public void DueText_FartherFutureDate_FallsBackToFormalMonthDay()
    {
        var due = new DateTime(2026, 12, 25);
        Assert.Equal("Due " + DisplayHelpers.FormalMonthDay(due), DisplayHelpers.DueText(due, Today));
    }

    [Theory]
    [InlineData(TaskWorkflow.Pending)]
    [InlineData(TaskWorkflow.InProgress)]
    [InlineData(TaskWorkflow.ForReview)]
    [InlineData(TaskWorkflow.ReturnedForCorrection)]
    [InlineData(TaskWorkflow.Completed)]
    public void DisplayStatus_CanonicalValues_PassThroughUnchanged(string status)
    {
        Assert.Equal(status, DisplayHelpers.DisplayStatus(status));
    }

    [Theory]
    [InlineData("done", TaskWorkflow.Completed)]
    [InlineData("Completed Fully", TaskWorkflow.Completed)] // Contains("complet")
    [InlineData("under review", TaskWorkflow.ForReview)]
    [InlineData("returned to sender", TaskWorkflow.ReturnedForCorrection)]
    [InlineData("ongoing", TaskWorkflow.InProgress)]
    [InlineData("in progress still", TaskWorkflow.InProgress)]
    public void DisplayStatus_LegacyFreeTextValues_MapByKeyword(string legacy, string expected)
    {
        Assert.Equal(expected, DisplayHelpers.DisplayStatus(legacy));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("something-unrecognized")]
    public void DisplayStatus_NullOrUnrecognized_FallsBackToPending(string? raw)
    {
        Assert.Equal(TaskWorkflow.Pending, DisplayHelpers.DisplayStatus(raw));
    }

    [Fact]
    public void DisplayStatus_IsCaseInsensitive()
    {
        Assert.Equal(TaskWorkflow.Completed, DisplayHelpers.DisplayStatus("COMPLETED"));
        Assert.Equal(TaskWorkflow.InProgress, DisplayHelpers.DisplayStatus("IN-PROGRESS"));
    }
}

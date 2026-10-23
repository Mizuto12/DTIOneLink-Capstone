using DTIOneLink.Services;

namespace DTIOneLink.Tests;

public class PrioritySuggestionServiceTests
{
    [Fact]
    public void Suggest_OverdueDueDate_SuggestsHighAndNamesDaysOverdue()
    {
        var overdueBy3 = TimeZoneHelper.PhilippineToday.AddDays(-3);

        var result = PrioritySuggestionService.Suggest(overdueBy3);

        Assert.Equal("high", result.Priority);
        Assert.Contains("already passed by 3 day(s)", result.Reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Suggest_WithinHighUrgencyWindow_SuggestsHigh(int daysRemaining)
    {
        var due = TimeZoneHelper.PhilippineToday.AddDays(daysRemaining);

        var result = PrioritySuggestionService.Suggest(due);

        Assert.Equal("high", result.Priority);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    public void Suggest_WithinMediumUrgencyWindow_SuggestsMedium(int daysRemaining)
    {
        var due = TimeZoneHelper.PhilippineToday.AddDays(daysRemaining);

        var result = PrioritySuggestionService.Suggest(due);

        Assert.Equal("medium", result.Priority);
    }

    [Fact]
    public void Suggest_MoreThanAWeekAway_SuggestsLow()
    {
        var due = TimeZoneHelper.PhilippineToday.AddDays(8);

        var result = PrioritySuggestionService.Suggest(due);

        Assert.Equal("low", result.Priority);
    }

    // ── Importance (task type) ───────────────────────────────────────────
    // A Whole Office or Department Directive task affects more than one
    // person's own work, so it raises a Low or Medium urgency tier by one
    // step — never lowers one, and never changes an already-High tier,
    // since urgency alone can already reach the ceiling on its own.

    [Theory]
    [InlineData(TaskTypes.WholeOffice)]
    [InlineData(TaskTypes.DepartmentDirective)]
    public void Suggest_LowUrgencyHighImportance_IsRaisedToMedium(string taskType)
    {
        var due = TimeZoneHelper.PhilippineToday.AddDays(8);

        var result = PrioritySuggestionService.Suggest(due, taskType);

        Assert.Equal("medium", result.Priority);
        Assert.Contains("raised a level", result.Reason);
    }

    [Theory]
    [InlineData(TaskTypes.WholeOffice)]
    [InlineData(TaskTypes.DepartmentDirective)]
    public void Suggest_MediumUrgencyHighImportance_IsRaisedToHigh(string taskType)
    {
        var due = TimeZoneHelper.PhilippineToday.AddDays(5);

        var result = PrioritySuggestionService.Suggest(due, taskType);

        Assert.Equal("high", result.Priority);
        Assert.Contains("raised a level", result.Reason);
    }

    [Fact]
    public void Suggest_HighUrgencyHighImportance_StaysHighWithoutClaimingARaise()
    {
        var due = TimeZoneHelper.PhilippineToday.AddDays(1);

        var result = PrioritySuggestionService.Suggest(due, TaskTypes.WholeOffice);

        Assert.Equal("high", result.Priority);
        Assert.DoesNotContain("raised a level", result.Reason);
    }

    [Fact]
    public void Suggest_DirectAdminTaskType_IsNotTreatedAsHighImportance()
    {
        // Direct Admin is OPD-assigned but still scoped to one Admin's own
        // work, unlike a Whole Office or Department Directive task — it
        // should not get the importance bump.
        var due = TimeZoneHelper.PhilippineToday.AddDays(8);

        var result = PrioritySuggestionService.Suggest(due, TaskTypes.DirectAdmin);

        Assert.Equal("low", result.Priority);
    }

    [Fact]
    public void Suggest_NoTaskType_BehavesExactlyAsBeforeImportanceExisted()
    {
        var due = TimeZoneHelper.PhilippineToday.AddDays(8);

        var result = PrioritySuggestionService.Suggest(due);

        Assert.Equal("low", result.Priority);
        Assert.DoesNotContain("raised a level", result.Reason);
    }
}

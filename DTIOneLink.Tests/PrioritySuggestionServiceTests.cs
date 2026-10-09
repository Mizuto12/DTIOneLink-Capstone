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
}

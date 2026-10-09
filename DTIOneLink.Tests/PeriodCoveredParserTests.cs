using DTIOneLink.Services;

namespace DTIOneLink.Tests;

public class PeriodCoveredParserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryGetEndDate_NullOrBlank_ReturnsNull(string? input)
    {
        Assert.Null(PeriodCoveredParser.TryGetEndDate(input));
    }

    [Theory]
    [InlineData("April 13, 2023 to April 30, 2025", "2025-04-30")]
    [InlineData("Jan. 30, 2025 - Apr. 30, 2025", "2025-04-30")]
    [InlineData("April 30, 2025", "2025-04-30")]
    [InlineData("4/30/2025", "2025-04-30")]
    [InlineData("2025-04-30", "2025-04-30")]
    public void TryGetEndDate_ExactDayShapes(string input, string expectedDate)
    {
        Assert.Equal(DateTime.Parse(expectedDate), PeriodCoveredParser.TryGetEndDate(input));
    }

    [Theory]
    [InlineData("January 2025 to April 2025", "2025-04-30")] // end of April (30 days)
    [InlineData("Jan 2024 to Feb 2024", "2024-02-29")]       // end of Feb in a leap year
    public void TryGetEndDate_MonthOnlyShapes_ResolveToEndOfMonth(string input, string expectedDate)
    {
        Assert.Equal(DateTime.Parse(expectedDate), PeriodCoveredParser.TryGetEndDate(input));
    }

    [Theory]
    [InlineData("2024", "2024-12-31")]
    [InlineData("2023-2024", "2024-12-31")]
    [InlineData(" 2023 - 2024 ", "2024-12-31")]
    public void TryGetEndDate_YearOrYearRange_ResolvesToEndOfYear(string input, string expectedDate)
    {
        Assert.Equal(DateTime.Parse(expectedDate), PeriodCoveredParser.TryGetEndDate(input));
    }

    [Theory]
    [InlineData("5 years after the contract ends")]
    [InlineData("permanent")]
    [InlineData("not a date at all")]
    [InlineData("1899")]  // outside the sensible range
    [InlineData("2101")]
    public void TryGetEndDate_AmbiguousOrOutOfRange_ReturnsNull(string input)
    {
        Assert.Null(PeriodCoveredParser.TryGetEndDate(input));
    }
}

using DTIOneLink.Services;

namespace DTIOneLink.Tests;

public class RetentionPeriodParserTests
{
    private static readonly DateTime RecordDate = new(2025, 1, 15);

    [Theory]
    [InlineData("5 years", "2030-01-15")]
    [InlineData("5 Years", "2030-01-15")]
    [InlineData("1 year", "2026-01-15")]
    [InlineData("6 months", "2025-07-15")]
    [InlineData(" 3  months ", "2025-04-15")]
    public void TryComputeDueDate_UnambiguousShapes_AddsToRecordDate(string retentionPeriod, string expectedDate)
    {
        Assert.Equal(DateTime.Parse(expectedDate), RetentionPeriodParser.TryComputeDueDate(RecordDate, retentionPeriod));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("permanent")]
    [InlineData("5 years after the contract")]
    [InlineData("5yrs")]
    [InlineData("0 years")]   // not a positive amount
    [InlineData("-1 years")]
    [InlineData("101 years")] // over MaxYears
    [InlineData("1201 months")] // over MaxMonths
    public void TryComputeDueDate_AmbiguousOrOutOfRange_ReturnsNull(string? retentionPeriod)
    {
        Assert.Null(RetentionPeriodParser.TryComputeDueDate(RecordDate, retentionPeriod));
    }

    [Fact]
    public void TryComputeDueDate_WithPeriodCovered_CountsFromEndOfPeriodNotRecordDate()
    {
        var result = RetentionPeriodParser.TryComputeDueDate(RecordDate, "April 13, 2023 to April 30, 2025", "3 years");

        Assert.Equal(new DateTime(2028, 4, 30), result);
    }

    [Fact]
    public void TryComputeDueDate_WithUnparseablePeriodCovered_FallsBackToRecordDate()
    {
        var result = RetentionPeriodParser.TryComputeDueDate(RecordDate, "not a real period", "2 years");

        Assert.Equal(new DateTime(2027, 1, 15), result);
    }

    // The Retention field is now a date picker: RecordsController stores
    // the chosen date itself as "yyyy-MM-dd" in RetentionPeriod. The
    // recalculation job (RecordRetentionReminder.RecalculateDueDatesAsync)
    // re-parses that stored text on every hourly run, so the parser must
    // recognize its own output as the due date, not a duration — otherwise
    // every record's RetentionDueDate gets silently wiped to null an hour
    // after it's created or edited.
    [Theory]
    [InlineData("2030-01-15", "2030-01-15")]
    [InlineData(" 2030-01-15 ", "2030-01-15")]
    public void TryComputeDueDate_IsoDateString_ReturnsThatDateDirectly(string retentionPeriod, string expectedDate)
    {
        Assert.Equal(DateTime.Parse(expectedDate), RetentionPeriodParser.TryComputeDueDate(RecordDate, retentionPeriod));
    }

    [Fact]
    public void TryComputeDueDate_IsoDateString_IgnoresPeriodCovered()
    {
        // An ISO date is already the absolute due date chosen on the form —
        // unlike the legacy duration grammar, it must NOT be re-anchored to
        // Period Covered's end date.
        var result = RetentionPeriodParser.TryComputeDueDate(RecordDate, "April 13, 2023 to April 30, 2025", "2030-01-15");

        Assert.Equal(new DateTime(2030, 1, 15), result);
    }
}

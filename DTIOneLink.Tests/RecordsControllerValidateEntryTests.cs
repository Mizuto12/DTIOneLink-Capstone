using System.Reflection;
using DTIOneLink.Controllers;
using DTIOneLink.Models;

namespace DTIOneLink.Tests;

// RecordsController.ValidateEntry is private static and has no DB
// dependency (Records isn't in the EF model, so none of this controller's
// unit-testable logic can go through the usual InMemoryDbContextFactory
// pattern) — invoked via reflection instead of changing its accessibility
// just to test it. Covers the retention-due-date plausibility bounds added
// for the 2026-10-29 audit's F6 finding (previously only the date FORMAT
// was checked, not the range — "1900-01-01" and "9999-12-31" both saved).
public class RecordsControllerValidateEntryTests
{
    private static (object? Fields, string? Error) Validate(RecordEntry entry)
    {
        var method = typeof(RecordsController).GetMethod("ValidateEntry", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("RecordsController.ValidateEntry not found — signature may have changed.");
        var result = method.Invoke(null, new object[] { entry })!;
        var type = result.GetType();
        var fields = type.GetField("Item1")!.GetValue(result);
        var error = (string?)type.GetField("Item2")!.GetValue(result);
        return (fields, error);
    }

    private static RecordEntry ValidEntry(string retentionPeriod) => new()
    {
        Code = "REC-001",
        Title = "Test Record",
        Medium = "Hard Copy",
        Location = "Cabinet 1",
        PeriodCovered = "2026",
        FilingSystem = "Chronological",
        AccessControl = "Public Record",
        RetentionPeriod = retentionPeriod,
    };

    [Fact]
    public void ValidateEntry_RetentionDateWithinBounds_Accepted()
    {
        var today = DateTime.UtcNow.Date;
        var (fields, error) = Validate(ValidEntry(today.AddYears(5).ToString("yyyy-MM-dd")));

        Assert.Null(error);
        Assert.NotNull(fields);
    }

    [Theory]
    [InlineData("1900-01-01")] // far in the past — a date-entry mistake, not a real retention span
    [InlineData("9999-12-31")] // far in the future — same
    public void ValidateEntry_RetentionDateOutOfPlausibleRange_Rejected(string retentionPeriod)
    {
        var (fields, error) = Validate(ValidEntry(retentionPeriod));

        Assert.Null(fields);
        Assert.NotNull(error);
        Assert.Contains("within", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateEntry_RetentionDateMalformed_StillRejectedAsBeforeTheBoundsFix()
    {
        var (fields, error) = Validate(ValidEntry("not-a-date"));

        Assert.Null(fields);
        Assert.Equal("Please choose a valid Retention Due Date.", error);
    }
}

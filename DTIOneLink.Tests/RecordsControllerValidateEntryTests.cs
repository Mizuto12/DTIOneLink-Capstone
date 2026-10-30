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

    // Validation-audit coverage (2026-10-30): the date pickers' own JS
    // already blocks an inverted Period Covered range on submit, but that's
    // client-only — a direct POST (or a crafted re-import) could still carry
    // "2026-12-31 to 2026-01-01". The date pickers always produce the exact
    // "yyyy-MM-dd to yyyy-MM-dd" shape this check looks for.
    private static RecordEntry EntryWithPeriod(string periodCovered)
    {
        var entry = ValidEntry(DateTime.UtcNow.Date.AddYears(1).ToString("yyyy-MM-dd"));
        entry.PeriodCovered = periodCovered;
        return entry;
    }

    [Fact]
    public void ValidateEntry_PeriodCoveredEndBeforeStart_IsRejected()
    {
        var (fields, error) = Validate(EntryWithPeriod("2026-12-31 to 2026-01-01"));

        Assert.Null(fields);
        Assert.Equal("Period Covered's end date must be on or after its start date.", error);
    }

    [Fact]
    public void ValidateEntry_PeriodCoveredSameDay_IsAccepted()
    {
        var (fields, error) = Validate(EntryWithPeriod("2026-06-15 to 2026-06-15"));

        Assert.Null(error);
        Assert.NotNull(fields);
    }

    [Fact]
    public void ValidateEntry_PeriodCoveredAscendingRange_IsAccepted()
    {
        var (fields, error) = Validate(EntryWithPeriod("2026-01-01 to 2026-12-31"));

        Assert.Null(error);
        Assert.NotNull(fields);
    }

    [Fact]
    public void ValidateEntry_PeriodCoveredFreeTextNotTwoDatePattern_IsLeftAlone()
    {
        // Older/imported records may hold free text here (e.g. "2024") that
        // doesn't match the date-picker's "X to Y" shape — the order check
        // must not apply to it, only CheckText's length/control-char rules.
        var (fields, error) = Validate(EntryWithPeriod("Calendar Year 2024"));

        Assert.Null(error);
        Assert.NotNull(fields);
    }
}

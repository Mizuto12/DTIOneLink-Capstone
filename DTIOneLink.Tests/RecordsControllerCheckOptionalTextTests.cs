using System.Reflection;
using DTIOneLink.Controllers;

namespace DTIOneLink.Tests;

// Validation-audit coverage (2026-10-30): MarkAuthorizedForDisposal's
// authorityReference/notes parameters previously had no length or
// control-character check at all, unlike every other free-text field in
// RecordsController (Code/Title/Location/PeriodCovered all go through
// CheckText). CheckOptionalText is private static and has no DB
// dependency, invoked via reflection the same way
// RecordsControllerValidateEntryTests invokes ValidateEntry.
public class RecordsControllerCheckOptionalTextTests
{
    private static string? Check(string? value, string label, int maxLength)
    {
        var method = typeof(RecordsController).GetMethod("CheckOptionalText", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("RecordsController.CheckOptionalText not found — signature may have changed.");
        return (string?)method.Invoke(null, new object?[] { value, label, maxLength });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CheckOptionalText_BlankOrNull_IsAccepted(string? value)
    {
        // Both fields are optional (window.prompt() can be cancelled/empty).
        Assert.Null(Check(value, "Authority reference", 200));
    }

    [Fact]
    public void CheckOptionalText_WithinLimit_IsAccepted()
    {
        Assert.Null(Check("NAP-2026-00123", "Authority reference", 200));
    }

    [Fact]
    public void CheckOptionalText_OverMaxLength_IsRejected()
    {
        var error = Check(new string('a', 201), "Authority reference", 200);

        Assert.NotNull(error);
        Assert.Contains("too long", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CheckOptionalText_ExactlyMaxLength_IsAccepted()
    {
        Assert.Null(Check(new string('a', 200), "Authority reference", 200));
    }

    [Fact]
    public void CheckOptionalText_ContainsControlCharacters_IsRejected()
    {
        var error = Check("line one\x01line two", "Notes", 1000);

        Assert.NotNull(error);
        Assert.Contains("aren't allowed", error, StringComparison.OrdinalIgnoreCase);
    }
}

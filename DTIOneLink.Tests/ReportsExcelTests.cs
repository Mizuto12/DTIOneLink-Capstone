using ClosedXML.Excel;
using DTIOneLink.Services;

namespace DTIOneLink.Tests;

// Structural verification of the .xlsx workbook Services/ReportsExcel.cs
// builds — per the testing brief's own rule, this inspects the generated
// workbook programmatically (worksheet names, header text, AutoFilter,
// frozen panes, cell types) rather than only claiming it "looks right" in
// Excel, which nothing in this environment can actually open and screenshot.
public class ReportsExcelTests
{
    private static ReportsExcel.Sheet SampleSheet(IReadOnlyList<ReportsExcel.Row>? rows = null) => new(
        GeneratedAtUtc: new DateTime(2026, 10, 30, 4, 0, 0, DateTimeKind.Utc), // noon Philippine time
        GeneratedBy: "Juan Dela Cruz",
        Scope: "Office-wide (all departments)",
        PeriodLabel: "2026-01-01 to 2026-12-31",
        CategoryFilter: null,
        SearchTerm: null,
        Rows: rows ?? new List<ReportsExcel.Row>
        {
            new("TASK-0001", "Active Tasks", "Quarterly Report", "Juan Dela Cruz", "In Progress · 40% · due Nov 5", null, new DateTime(2026, 10, 29, 1, 0, 0, DateTimeKind.Utc)),
            new("TASK-0002", "Completed Tasks", "Inventory Count", "Maria Santos", "Completed", null, new DateTime(2026, 10, 20, 1, 0, 0, DateTimeKind.Utc)),
            new("REC-001", "Retained", "Payroll Records 2025", "Admin User", "Retain 5 Years · until Dec 31, 2030", "Disposal due soon", new DateTime(2026, 10, 15, 1, 0, 0, DateTimeKind.Utc)),
        });

    private static XLWorkbook Reopen(byte[] bytes) => new(new MemoryStream(bytes));

    [Fact]
    public void Build_ProducesTwoWorksheetsWithExpectedNames()
    {
        var bytes = ReportsExcel.Build(SampleSheet());
        using var wb = Reopen(bytes);

        Assert.Equal(2, wb.Worksheets.Count);
        Assert.True(wb.Worksheets.TryGetWorksheet("Summary", out _));
        Assert.True(wb.Worksheets.TryGetWorksheet("Report Items", out _));
    }

    [Fact]
    public void SummarySheet_ContainsOfficeHeaderAndMetadata()
    {
        var bytes = ReportsExcel.Build(SampleSheet());
        using var wb = Reopen(bytes);
        var ws = wb.Worksheet("Summary");

        Assert.Equal(ReportsExcel.OfficeName, ws.Cell("A1").GetString());
        Assert.Equal("DTI OneLink", ws.Cell("A2").GetString());
        Assert.Equal("Reports Export", ws.Cell("A3").GetString());
        Assert.Contains("2026-01-01 to 2026-12-31", ws.CellsUsed().Select(c => c.GetString()));
        Assert.Contains("Office-wide (all departments)", ws.CellsUsed().Select(c => c.GetString()));
        Assert.Contains("Juan Dela Cruz", ws.CellsUsed().Select(c => c.GetString()));
    }

    [Fact]
    public void SummarySheet_CountsMatchExportedRowsExactly()
    {
        // Phase 3's rule: summary totals must correspond to the exact
        // exported dataset, never a wider/office-wide total.
        var bytes = ReportsExcel.Build(SampleSheet());
        using var wb = Reopen(bytes);
        var ws = wb.Worksheet("Summary");

        var totalRow = ws.CellsUsed().First(c => c.GetString() == "Total").Address.RowNumber;
        Assert.Equal(3, ws.Cell(totalRow, 2).GetValue<int>());

        var activeRow = ws.CellsUsed().First(c => c.GetString() == "Active Tasks").Address.RowNumber;
        Assert.Equal(1, ws.Cell(activeRow, 2).GetValue<int>());
    }

    [Fact]
    public void DetailSheet_HasBoldHeaderRowWithExpectedColumns()
    {
        var bytes = ReportsExcel.Build(SampleSheet());
        using var wb = Reopen(bytes);
        var ws = wb.Worksheet("Report Items");

        Assert.Equal("No.", ws.Cell(1, 1).GetString());
        Assert.Equal("Category", ws.Cell(1, 2).GetString());
        Assert.Equal("ID", ws.Cell(1, 3).GetString());
        Assert.Equal("Title", ws.Cell(1, 4).GetString());
        Assert.Equal("Owner", ws.Cell(1, 5).GetString());
        Assert.Equal("Status / Details", ws.Cell(1, 6).GetString());
        Assert.Equal("Flag", ws.Cell(1, 7).GetString());
        Assert.Equal("Last Activity", ws.Cell(1, 8).GetString());
        Assert.True(ws.Cell(1, 1).Style.Font.Bold);
    }

    [Fact]
    public void DetailSheet_RowCountMatchesInputExactly()
    {
        var bytes = ReportsExcel.Build(SampleSheet());
        using var wb = Reopen(bytes);
        var ws = wb.Worksheet("Report Items");

        // Header row + 3 data rows, no more, no less.
        Assert.Equal(4, ws.RowsUsed().Count());
        Assert.Equal("TASK-0001", ws.Cell(2, 3).GetString());
        Assert.Equal("REC-001", ws.Cell(4, 3).GetString());
    }

    [Fact]
    public void DetailSheet_LastActivityIsARealDateCellNotText()
    {
        // Phase 3's rule: dates remain actual Excel date values, not the
        // page's relative "3 days ago" text.
        var bytes = ReportsExcel.Build(SampleSheet());
        using var wb = Reopen(bytes);
        var ws = wb.Worksheet("Report Items");

        var cell = ws.Cell(2, 8);
        Assert.Equal(XLDataType.DateTime, cell.DataType);
        // 2026-10-29T01:00:00Z -> 2026-10-29 09:00 Philippine time (UTC+8).
        Assert.Equal(new DateTime(2026, 10, 29, 9, 0, 0), cell.GetDateTime());
    }

    [Fact]
    public void DetailSheet_HasAutoFilterAndFrozenHeaderRow()
    {
        var bytes = ReportsExcel.Build(SampleSheet());
        using var wb = Reopen(bytes);
        var ws = wb.Worksheet("Report Items");

        Assert.True(ws.AutoFilter.IsEnabled);
        Assert.Equal(1, ws.SheetView.SplitRow);
    }

    [Fact]
    public void DetailSheet_PrintSetupIsLandscapeWithRepeatingHeader()
    {
        var bytes = ReportsExcel.Build(SampleSheet());
        using var wb = Reopen(bytes);
        var ws = wb.Worksheet("Report Items");

        Assert.Equal(XLPageOrientation.Landscape, ws.PageSetup.PageOrientation);
        Assert.Equal(1, ws.PageSetup.FirstRowToRepeatAtTop);
        Assert.Equal(1, ws.PageSetup.LastRowToRepeatAtTop);
    }

    [Fact]
    public void Build_EmptyDataset_StillProducesAValidWorkbookWithJustTheHeaderRow()
    {
        var bytes = ReportsExcel.Build(SampleSheet(new List<ReportsExcel.Row>()));
        using var wb = Reopen(bytes);
        var ws = wb.Worksheet("Report Items");

        Assert.Single(ws.RowsUsed()); // header only
        var summary = wb.Worksheet("Summary");
        var totalRow = summary.CellsUsed().First(c => c.GetString() == "Total").Address.RowNumber;
        Assert.Equal(0, summary.Cell(totalRow, 2).GetValue<int>());
    }

    [Theory]
    [InlineData("=SUM(A1:A99)")]
    [InlineData("+CMD|'/c calc'!A0")]
    [InlineData("-2+3")]
    [InlineData("@SUM(1,2)")]
    public void DetailSheet_TitleStartingWithFormulaTriggerCharacter_IsStoredAsLiteralText(string dangerousTitle)
    {
        var rows = new List<ReportsExcel.Row>
        {
            new("TASK-0001", "Active Tasks", dangerousTitle, "Someone", "tag", null, DateTime.UtcNow),
        };
        var bytes = ReportsExcel.Build(SampleSheet(rows));
        using var wb = Reopen(bytes);
        var ws = wb.Worksheet("Report Items");

        var cell = ws.Cell(2, 4);
        // Must never be stored/interpreted as a formula. The leading
        // apostrophe SafeText prepends is the standard Excel "force text"
        // marker — ClosedXML (like Excel itself) consumes it on write and
        // the readback value comes back as plain text with no quote, which
        // is the correct outcome, not a defect in the sanitization.
        Assert.False(cell.HasFormula);
        Assert.Equal(XLDataType.Text, cell.DataType);
        Assert.Equal(dangerousTitle, cell.GetString());
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://evil\")")]
    [InlineData("+1")]
    public void DetailSheet_RecordIdStartingWithFormulaTriggerCharacter_IsStoredAsLiteralText(string dangerousId)
    {
        // A task's ID is always system-built ("TASK-0001"), but a record's
        // ID is its user-typed Code field — must get the same guard.
        var rows = new List<ReportsExcel.Row>
        {
            new(dangerousId, "Retained", "Some Record", "Someone", "tag", null, DateTime.UtcNow),
        };
        var bytes = ReportsExcel.Build(SampleSheet(rows));
        using var wb = Reopen(bytes);
        var ws = wb.Worksheet("Report Items");

        var cell = ws.Cell(2, 3);
        Assert.False(cell.HasFormula);
        Assert.Equal(XLDataType.Text, cell.DataType);
        Assert.Equal(dangerousId, cell.GetString());
    }

    [Fact]
    public void DetailSheet_OrdinaryTitle_IsUnchanged()
    {
        var rows = new List<ReportsExcel.Row>
        {
            new("TASK-0001", "Active Tasks", "Ordinary Task Title", "Someone", "tag", null, DateTime.UtcNow),
        };
        var bytes = ReportsExcel.Build(SampleSheet(rows));
        using var wb = Reopen(bytes);
        var ws = wb.Worksheet("Report Items");

        Assert.Equal("Ordinary Task Title", ws.Cell(2, 4).GetString());
    }
}

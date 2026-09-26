using ClosedXML.Excel;

namespace DTIOneLink.Services
{
    // Builds the DTI "Masterlist of Records" Excel file: the same title block,
    // eight columns, grey bordered header row and Prepared/Reviewed/Noted by
    // signature block as the office's paper form.
    public static class RecordMasterlistExcel
    {
        public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        // Shown on the OFFICE/BUREAU line of every masterlist.
        public const string OfficeName = "DEPARTMENT OF TRADE AND INDUSTRY - LAGUNA PROVINCIAL OFFICE";

        public sealed record Row(
            string Code, string Title, string Medium, string Location, string PeriodCovered,
            string FilingSystem, string AccessControl, string RetentionPeriod);

        public sealed record Signatory(string Name, string Position);

        public sealed record Sheet(
            string? Division, DateTime LastUpdate,
            Signatory PreparedBy, Signatory ReviewedBy, Signatory NotedBy,
            IReadOnlyList<Row> Rows);

        private static readonly string[] Headers =
        {
            "CODE", "TITLE OF RECORD", "MEDIUM", "LOCATION", "PERIOD COVERED",
            "FILING SYSTEM", "ACCESS CONTROL", "RETENTION PERIOD"
        };

        // Column widths (Excel character units) and alignment, A..H.
        private static readonly double[] Widths = { 22, 46, 15, 20, 16, 14, 15, 21 };
        private static readonly XLAlignmentHorizontalValues[] Align =
        {
            XLAlignmentHorizontalValues.Left, XLAlignmentHorizontalValues.Left,
            XLAlignmentHorizontalValues.Left, XLAlignmentHorizontalValues.Center,
            XLAlignmentHorizontalValues.Center, XLAlignmentHorizontalValues.Center,
            XLAlignmentHorizontalValues.Center, XLAlignmentHorizontalValues.Left
        };

        private const int HeaderRow = 8;
        private const int BlankRowsAfterData = 3; // empty lines left on the form for handwritten entries
        private const double DataFontSize = 10;

        public static byte[] Build(Sheet sheet)
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Masterlist of Records");
            ws.Style.Font.FontName = "Calibri";
            ws.Style.Font.FontSize = 11;

            for (var c = 0; c < Widths.Length; c++)
            {
                ws.Column(c + 1).Width = Widths[c];
            }

            // ── Title block ─────────────────────────────────────────
            ws.Cell("H1").Value = "SF NO.";
            ws.Cell("H1").Style.Font.FontSize = DataFontSize;
            ws.Cell("A2").Value = "DEPARTMENT OF TRADE AND INDUSTRY";
            ws.Cell("A3").Value = "MASTERLIST OF RECORDS";
            ws.Cell("A3").Style.Font.Bold = true;
            ws.Cell("A3").Style.Font.FontSize = 18;
            ws.Row(3).Height = 26;

            ws.Cell("A5").Value = "OFFICE/BUREAU:";
            ws.Cell("B5").Value = OfficeName;
            ws.Cell("A6").Value = "DIVISION:";
            ws.Cell("B6").Value = (sheet.Division ?? "").ToUpperInvariant();
            ws.Range("B5:B6").Style.Font.FontSize = 9;
            ws.Cell("E5").Value = "LAST UPDATE:";
            ws.Cell("E5").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            ws.Cell("F5").Value = sheet.LastUpdate.ToString("MMMM d, yyyy");

            // ── Header row ──────────────────────────────────────────
            for (var c = 0; c < Headers.Length; c++)
            {
                ws.Cell(HeaderRow, c + 1).Value = Headers[c];
            }
            var header = ws.Range(HeaderRow, 1, HeaderRow, Headers.Length);
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#BFBFBF");
            header.Style.Font.FontSize = DataFontSize;
            header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            header.Style.Alignment.WrapText = true;
            ws.Row(HeaderRow).Height = 30;

            // ── Records ─────────────────────────────────────────────
            var r = HeaderRow + 1;
            foreach (var row in sheet.Rows)
            {
                string[] values =
                {
                    row.Code, row.Title, row.Medium, row.Location, row.PeriodCovered,
                    row.FilingSystem, row.AccessControl, row.RetentionPeriod
                };
                var lines = 1;
                for (var c = 0; c < values.Length; c++)
                {
                    var cell = ws.Cell(r, c + 1);
                    // SetValue with a string keeps codes/dates exactly as typed
                    // (Excel would otherwise turn "2023-2024" or "01" into numbers).
                    cell.SetValue(values[c]);
                    cell.Style.Alignment.Horizontal = Align[c];
                    lines = Math.Max(lines, EstimateLines(values[c], Widths[c]));
                }
                // Excel doesn't auto-size wrapped rows when the file is
                // generated, so size each row from its longest cell.
                ws.Row(r).Height = Math.Max(30, lines * 13 + 4);
                r++;
            }

            for (var i = 0; i < BlankRowsAfterData; i++, r++)
            {
                ws.Row(r).Height = 18;
            }
            var lastTableRow = r - 1;

            var body = ws.Range(HeaderRow + 1, 1, lastTableRow, Headers.Length);
            body.Style.Font.FontSize = DataFontSize;
            body.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            body.Style.Alignment.WrapText = true;

            var table = ws.Range(HeaderRow, 1, lastTableRow, Headers.Length);
            table.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            table.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            header.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

            // ── Signature block ─────────────────────────────────────
            var sigRow = lastTableRow + 2;
            WriteSignatory(ws, sigRow, 1, "Prepared by:", sheet.PreparedBy);
            WriteSignatory(ws, sigRow, 3, "Reviewed by:", sheet.ReviewedBy);
            WriteSignatory(ws, sigRow, 5, "Noted by:", sheet.NotedBy);
            var lastRow = sigRow + 4;

            // ── Printing: landscape, one page wide, header row repeats ──
            ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
            ws.PageSetup.FitToPages(1, 0);
            ws.PageSetup.Margins.Left = 0.4;
            ws.PageSetup.Margins.Right = 0.4;
            ws.PageSetup.Margins.Top = 0.5;
            ws.PageSetup.Margins.Bottom = 0.5;
            ws.PageSetup.SetRowsToRepeatAtTop(HeaderRow, HeaderRow);
            ws.PageSetup.PrintAreas.Add(1, 1, lastRow, Headers.Length);

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        // "Prepared by:" label, then the name in bold three rows down and the
        // position under it — the gap is where the signature goes.
        private static void WriteSignatory(IXLWorksheet ws, int row, int column, string label, Signatory signatory)
        {
            ws.Cell(row, column).Value = label;
            ws.Cell(row + 3, column).Value = signatory.Name.ToUpperInvariant();
            ws.Cell(row + 3, column).Style.Font.Bold = true;
            ws.Cell(row + 4, column).Value = signatory.Position;
        }

        // Rough line count for wrapped text at the data font size.
        private static int EstimateLines(string value, double columnWidth)
        {
            var charsPerLine = Math.Max(1, (int)(columnWidth * 1.15));
            return value.Split('\n').Sum(part => Math.Max(1, (int)Math.Ceiling(part.Length / (double)charsPerLine)));
        }
    }
}

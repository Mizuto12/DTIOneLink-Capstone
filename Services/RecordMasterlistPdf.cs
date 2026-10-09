using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace DTIOneLink.Services
{
    // Builds a PDF of the DTI "Masterlist of Records" that mirrors
    // RecordMasterlistExcel.cs's layout exactly (same title block, columns,
    // grey header row and signature block), so Print Log opens something a
    // browser can actually render pixel-for-pixel instead of approximating
    // the Excel file in HTML/CSS.
    public static class RecordMasterlistPdf
    {
        public const string ContentType = "application/pdf";

        private static readonly string[] Headers =
        {
            "CODE", "TITLE OF RECORD", "MEDIUM", "LOCATION", "PERIOD COVERED",
            "FILING SYSTEM", "ACCESS CONTROL", "RETENTION PERIOD"
        };

        // Same relative proportions as RecordMasterlistExcel.Widths.
        private static readonly float[] Widths = { 22, 46, 15, 20, 16, 14, 15, 21 };
        // true = center-aligned, false = left-aligned (matches RecordMasterlistExcel.Align).
        private static readonly bool[] CenterAlign = { false, false, false, true, true, true, true, false };

        private const string BorderColor = "#000000";
        private const string HeaderFill = "#BFBFBF";
        private const int BlankRowsAfterData = 3;

        public static byte[] Build(RecordMasterlistExcel.Sheet sheet)
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(16);
                    // No FontFamily() call: QuestPDF only ships its bundled "Lato"
                    // font by default (as of 2026.9+, it refuses to render a family
                    // it doesn't have on disk rather than silently substituting one).
                    // Using the default keeps this working on any hosting environment
                    // without deploying extra font files.
                    page.DefaultTextStyle(t => t.FontSize(9));

                    page.Header().Column(col =>
                    {
                        col.Item().AlignRight().Text("SF NO.").FontSize(8);
                        col.Item().Text("DEPARTMENT OF TRADE AND INDUSTRY").FontSize(10);
                        col.Item().PaddingBottom(3).Text("MASTERLIST OF RECORDS").FontSize(18).Bold();

                        col.Item().Row(row =>
                        {
                            row.RelativeItem(2).Text(t =>
                            {
                                t.Span("OFFICE/BUREAU:  ").Bold();
                                t.Span(RecordMasterlistExcel.OfficeName);
                            });
                            row.RelativeItem(1).AlignRight().Text(t =>
                            {
                                t.Span("LAST UPDATE:  ").Bold();
                                t.Span(sheet.LastUpdate.ToString("MMMM d, yyyy"));
                            });
                        });
                        col.Item().PaddingBottom(4).Text(t =>
                        {
                            t.Span("DIVISION:  ").Bold();
                            t.Span((sheet.Division ?? "").ToUpperInvariant());
                        });
                    });

                    page.Content().Column(col =>
                    {
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                foreach (var w in Widths)
                                {
                                    cols.RelativeColumn(w);
                                }
                            });

                            table.Header(header =>
                            {
                                foreach (var h in Headers)
                                {
                                    header.Cell().Element(HeaderCell).AlignCenter().Text(h).Bold();
                                }
                            });

                            foreach (var r in sheet.Rows)
                            {
                                string[] values =
                                {
                                    r.Code, r.Title, r.Medium, r.Location, r.PeriodCovered,
                                    r.FilingSystem, r.AccessControl, r.RetentionPeriod
                                };
                                for (var i = 0; i < values.Length; i++)
                                {
                                    var cell = table.Cell().Element(DataCell);
                                    if (CenterAlign[i]) cell.AlignCenter().Text(values[i]);
                                    else cell.Text(values[i]);
                                }
                            }

                            for (var b = 0; b < BlankRowsAfterData; b++)
                            {
                                for (var i = 0; i < Headers.Length; i++)
                                {
                                    table.Cell().Element(DataCell).Text("");
                                }
                            }
                        });

                        col.Item().PaddingTop(16).Row(row =>
                        {
                            row.RelativeItem().Element(c => Signature(c, "Prepared by:", sheet.PreparedBy));
                            row.RelativeItem().Element(c => Signature(c, "Reviewed by:", sheet.ReviewedBy));
                            row.RelativeItem().Element(c => Signature(c, "Noted by:", sheet.NotedBy));
                        });
                    });
                });
            });

            return document.GeneratePdf();
        }

        private static IContainer HeaderCell(IContainer container) =>
            container.Background(HeaderFill).Border(1).BorderColor(BorderColor).Padding(3).MinHeight(18);

        private static IContainer DataCell(IContainer container) =>
            container.Border(1).BorderColor(BorderColor).Padding(3).MinHeight(15);

        private static void Signature(IContainer container, string label, RecordMasterlistExcel.Signatory signatory)
        {
            container.PaddingRight(20).Column(col =>
            {
                col.Item().Text(label);
                col.Item().PaddingTop(18).BorderBottom(1).BorderColor(BorderColor)
                    .Text(signatory.Name.ToUpperInvariant()).Bold();
                col.Item().PaddingTop(2).Text(signatory.Position);
            });
        }
    }
}

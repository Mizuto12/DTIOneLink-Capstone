using System.Globalization;
using System.Text.RegularExpressions;

namespace DTIOneLink.Services
{
    // Reads the END date of the free-text Period Covered on the Records form,
    // because DTI counts retention from the end of the period covered (e.g.
    // "April 13, 2023 to April 30, 2025" + 3 years = April 30, 2028).
    //
    // Only unambiguous shapes are read; anything else returns null and the
    // caller falls back to the date the record was logged:
    //   "April 13, 2023 to April 30, 2025"  -> April 30, 2025
    //   "Jan. 30, 2025 - Apr. 30, 2025"     -> April 30, 2025
    //   "April 30, 2025"                    -> April 30, 2025
    //   "January 2025 to April 2025"        -> April 30, 2025 (end of month)
    //   "2023-2024" / "2024"                -> December 31, 2024 (end of year)
    public static class PeriodCoveredParser
    {
        private static readonly Regex YearRange = new(
            "^ *(?:[0-9]{4} *[-–—] *)?([0-9]{4}) *$", RegexOptions.CultureInvariant);

        // Splits "<start> to <end>" / "<start> - <end>" (dash with spaces, so
        // dates like 2025-04-30 are not split).
        private static readonly Regex RangeSeparator = new(
            @"\s+(?:to|until|-|–|—)\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly string[] DayFormats =
        {
            "MMMM d, yyyy", "MMMM d yyyy", "MMM d, yyyy", "MMM d yyyy", "MMM. d, yyyy",
            "M/d/yyyy", "MM/dd/yyyy", "yyyy-MM-dd"
        };

        private static readonly string[] MonthFormats = { "MMMM yyyy", "MMM yyyy", "MMM. yyyy" };

        public static DateTime? TryGetEndDate(string? periodCovered)
        {
            if (string.IsNullOrWhiteSpace(periodCovered))
            {
                return null;
            }

            var text = periodCovered.Trim();

            var years = YearRange.Match(text);
            if (years.Success)
            {
                var year = int.Parse(years.Groups[1].Value, CultureInfo.InvariantCulture);
                return IsSensibleYear(year) ? new DateTime(year, 12, 31) : null;
            }

            var parts = RangeSeparator.Split(text);
            var end = parts[^1].Trim().TrimEnd('.');

            if (DateTime.TryParseExact(end, DayFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces, out var day) && IsSensibleYear(day.Year))
            {
                return day.Date;
            }

            if (DateTime.TryParseExact(end, MonthFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces, out var month) && IsSensibleYear(month.Year))
            {
                return new DateTime(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month));
            }

            return null;
        }

        private static bool IsSensibleYear(int year) => year >= 1900 && year <= 2100;
    }
}

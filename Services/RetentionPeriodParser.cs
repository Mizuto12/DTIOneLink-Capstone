using System.Text.RegularExpressions;

namespace DTIOneLink.Services
{
    // Turns the free-text Retention Period typed on the Records form into a
    // due date — but ONLY for the unambiguous shapes "N year(s)" and
    // "N month(s)" (e.g. "5 Years", "6 months"). Anything else ("Permanent",
    // "5 years after the contract", "5yrs") returns null and is left for
    // records personnel to review, rather than guessing a disposal date.
    //
    // Keep this grammar in sync with the SQL backfill in migration
    // 20260925000000_AddRecordSystemFields, which applies the same rules to
    // rows logged before this parser existed.
    public static class RetentionPeriodParser
    {
        // Literal spaces and [0-9] (not \s / \d) on purpose: .NET's \d also
        // matches non-ASCII digits, and the SQL backfill only trims spaces.
        private static readonly Regex Pattern = new(
            "^ *([0-9]{1,4}) +(years?|months?) *$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public const int MaxYears = 100;
        public const int MaxMonths = 1200;

        // Retention counts from the END of the Period Covered when that can be
        // read (how DTI counts it), otherwise from the date the record was
        // logged. Used for new records and to recalculate existing ones.
        public static DateTime? TryComputeDueDate(DateTime recordDate, string? periodCovered, string? retentionPeriod)
        {
            var startDate = PeriodCoveredParser.TryGetEndDate(periodCovered) ?? recordDate;
            return TryComputeDueDate(startDate, retentionPeriod);
        }

        public static DateTime? TryComputeDueDate(DateTime recordDate, string? retentionPeriod)
        {
            if (string.IsNullOrWhiteSpace(retentionPeriod))
            {
                return null;
            }

            var match = Pattern.Match(retentionPeriod);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out var amount) || amount <= 0)
            {
                return null;
            }

            var isYears = match.Groups[2].Value.StartsWith("y", StringComparison.OrdinalIgnoreCase);
            if (isYears ? amount > MaxYears : amount > MaxMonths)
            {
                return null;
            }

            return isYears ? recordDate.Date.AddYears(amount) : recordDate.Date.AddMonths(amount);
        }
    }
}

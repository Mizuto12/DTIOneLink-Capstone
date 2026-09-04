namespace DTIOneLink.Services
{
    // All timestamps are stored in UTC (OccurredAt, SubmittedAt, DecidedAt,
    // CreatedAt all use DateTime.UtcNow) — that's intentional and correct
    // for storage. This only converts for display, so the DB stays
    // unambiguous regardless of where the app is hosted.
    public static class TimeZoneHelper
    {
        private static readonly TimeZoneInfo PhilippineTimeZone = ResolvePhilippineTimeZone();

        private static TimeZoneInfo ResolvePhilippineTimeZone()
        {
            // "Asia/Manila" is the IANA id (works on Linux, and on Windows
            // with .NET 6+/ICU). "Singapore Standard Time" is the Windows-only
            // fallback id for the same UTC+8, no-DST offset, in case ICU
            // isn't available in this environment.
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");
            }
        }

        public static DateTime ToPhilippineTime(DateTime utcDateTime)
        {
            // Guard against accidentally passing an already-local or
            // Unspecified-kind DateTime — treat anything not explicitly
            // UTC as UTC, since that's what every call site actually stores.
            var utc = utcDateTime.Kind == DateTimeKind.Utc
                ? utcDateTime
                : DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);

            return TimeZoneInfo.ConvertTimeFromUtc(utc, PhilippineTimeZone);
        }
    }
}
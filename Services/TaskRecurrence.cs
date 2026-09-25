namespace DTIOneLink.Services
{
    // How often an OPD task repeats. Stored on TaskItem.Recurrence of the
    // LATEST copy only (null = doesn't repeat). Each new copy is due one
    // interval after the previous copy's due date.
    public static class TaskRecurrence
    {
        public const string Daily = "daily";
        public const string Weekly = "weekly";
        public const string Monthly = "monthly";
        public const string Quarterly = "quarterly";

        public static readonly string[] All = { Daily, Weekly, Monthly, Quarterly };

        public static bool IsValid(string? value) => value != null && All.Contains(value);

        public static string Label(string? value) => value switch
        {
            Daily => "Every day",
            Weekly => "Every week",
            Monthly => "Every month",
            Quarterly => "Every quarter",
            _ => "Does not repeat"
        };

        // The date `steps` intervals after `date`. Always counted from the
        // original date (not step by step), so a task due on the 31st goes
        // to the 30th/28th only in shorter months and returns to the 31st.
        public static DateTime Advance(DateTime date, string frequency, int steps = 1) => frequency switch
        {
            Daily => date.AddDays(steps),
            Weekly => date.AddDays(7 * steps),
            Monthly => date.AddMonths(steps),
            Quarterly => date.AddMonths(3 * steps),
            _ => throw new ArgumentOutOfRangeException(nameof(frequency), frequency, "Unknown repeat frequency.")
        };

        // Next copy's due date: one interval after the previous due date,
        // skipping ahead if the app was offline, so only ONE copy is ever
        // created per run and it is never already overdue.
        public static DateTime NextDueDate(DateTime previousDueDate, string frequency, DateTime today)
        {
            var steps = 1;
            var next = Advance(previousDueDate.Date, frequency, steps);
            while (next <= today)
            {
                steps++;
                next = Advance(previousDueDate.Date, frequency, steps);
            }
            return next;
        }
    }
}

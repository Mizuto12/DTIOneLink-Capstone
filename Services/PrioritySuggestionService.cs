namespace DTIOneLink.Services
{
    // Rule-based, not learned: every suggestion is fully explainable by
    // days-until-due alone. This is a suggestion only — nothing here ever
    // writes to TaskItem.Priority directly; Admin/Supervisor always confirms
    // or overrides via the existing Priority radio buttons in the form.
    public static class PrioritySuggestionService
    {
        public record Suggestion(string Priority, string Reason);

        public static Suggestion Suggest(DateTime dueDate)
        {
            var daysRemaining = (dueDate.Date - DateTime.UtcNow.Date).TotalDays;

            if (daysRemaining < 0)
            {
                var overdueBy = Math.Abs((int)daysRemaining);
                return new Suggestion("high",
                    $"Suggested High — this task's due date has already passed by {overdueBy} day(s).");
            }

            if (daysRemaining <= 2)
            {
                return new Suggestion("high",
                    $"Suggested High — due in {(int)daysRemaining} day(s), within the high-urgency window (0–2 days).");
            }

            if (daysRemaining <= 7)
            {
                return new Suggestion("medium",
                    $"Suggested Medium — due in {(int)daysRemaining} day(s), within the medium-urgency window (3–7 days).");
            }

            return new Suggestion("low",
                $"Suggested Low — due in {(int)daysRemaining} day(s), more than a week away.");
        }
    }
}
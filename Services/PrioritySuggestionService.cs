namespace DTIOneLink.Services
{
    // Rule-based, not learned: every suggestion is fully explainable by
    // days-until-due plus task type alone. This is a suggestion only —
    // nothing here ever writes to TaskItem.Priority directly; Admin/Supervisor
    // always confirms or overrides via the existing Priority radio buttons in
    // the form.
    public static class PrioritySuggestionService
    {
        public record Suggestion(string Priority, string Reason);

        // Importance: a Whole Office or Department Directive task affects
        // more than one person's own work (the whole office, or an entire
        // department), unlike an ordinary task or even a Direct Admin task
        // (OPD-assigned, but still scoped to one Admin's own work). That
        // breadth of impact is "importance" as a second, independent input
        // alongside urgency — not a stand-in for it: urgency alone can
        // already reach High on its own, importance only ever raises a
        // lower urgency tier by one step, never lowers one.
        private static bool IsHighImportance(string? taskType) =>
            taskType == TaskTypes.WholeOffice || taskType == TaskTypes.DepartmentDirective;

        private static string ImportanceLabel(string? taskType) => taskType switch
        {
            TaskTypes.WholeOffice => "a whole-office task",
            TaskTypes.DepartmentDirective => "a department directive",
            _ => "a task"
        };

        public static Suggestion Suggest(DateTime dueDate, string? taskType = null)
        {
            // Time-aware, not just calendar-date-aware: a task due in 20
            // hours is more urgent than one due in 2 days, even though a
            // date-only diff would put them in the same bucket. Deliberately
            // NOT TaskWorkflow.EffectiveDueMoment's "midnight means end of
            // day" adjustment — that's specifically for the Overdue boolean
            // threshold; shifting it here would throw off the exact
            // days-overdue count this suggestion's own reason text names.
            var daysRemaining = (dueDate - TimeZoneHelper.PhilippineNow).TotalDays;

            string tier;
            string urgencyReason;
            if (daysRemaining < 0)
            {
                var overdueBy = Math.Abs((int)daysRemaining);
                tier = "high";
                urgencyReason = $"this task's due date has already passed by {overdueBy} day(s)";
            }
            else if (daysRemaining <= 2)
            {
                tier = "high";
                urgencyReason = $"due in {(int)daysRemaining} day(s), within the high-urgency window (0–2 days)";
            }
            else if (daysRemaining <= 7)
            {
                tier = "medium";
                urgencyReason = $"due in {(int)daysRemaining} day(s), within the medium-urgency window (3–7 days)";
            }
            else
            {
                tier = "low";
                urgencyReason = $"due in {(int)daysRemaining} day(s), more than a week away";
            }

            var raisedByImportance = false;
            if (tier != "high" && IsHighImportance(taskType))
            {
                tier = tier == "low" ? "medium" : "high";
                raisedByImportance = true;
            }

            var label = tier switch { "high" => "High", "medium" => "Medium", _ => "Low" };
            var reason = raisedByImportance
                ? $"Suggested {label} — {urgencyReason}; raised a level because this is {ImportanceLabel(taskType)}, which affects more than one person's own work."
                : $"Suggested {label} — {urgencyReason}.";

            return new Suggestion(tier, reason);
        }
    }
}

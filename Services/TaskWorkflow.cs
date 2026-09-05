    namespace DTIOneLink.Services
    {
        // Single source of truth for status values, legal transitions, and the
        // Overdue calculation. Nothing about Overdue is ever stored on TaskItem —
        // it's derived at read time everywhere, same as Details.cshtml already
        // did; this just makes that one calculation shared instead of duplicated.
        public static class TaskWorkflow
        {
            public const string Pending = "pending";
            public const string InProgress = "in-progress";
            public const string ForReview = "for-review";
            public const string ReturnedForCorrection = "returned-for-correction";
            public const string Completed = "completed";

            // Terminal state has no outgoing edges. ForReview can only be
            // resolved by an Admin decision (see TasksController.Review), never
            // by the employee directly.
            private static readonly Dictionary<string, string[]> AllowedTransitions = new()
            {
                [Pending] = new[] { InProgress, ForReview },
                [InProgress] = new[] { ForReview },
                [ForReview] = new[] { Completed, ReturnedForCorrection },
                [ReturnedForCorrection] = new[] { ForReview },
                [Completed] = Array.Empty<string>()
            };

            public static string Normalize(string? status) =>
                string.IsNullOrWhiteSpace(status) ? Pending : status.Trim().ToLowerInvariant();

            // A no-op "transition" (saving progress without a status change) is
            // always fine — this only guards actual state changes.
            public static bool CanTransition(string? currentStatus, string? nextStatus)
            {
                var current = Normalize(currentStatus);
                var next = Normalize(nextStatus);
                if (current == next) return true;
                return AllowedTransitions.TryGetValue(current, out var allowed) && allowed.Contains(next);
            }

            public static bool IsOverdue(string? status, DateTime dueDate) =>
                Normalize(status) != Completed && dueDate.Date < DateTime.UtcNow.Date;

            // "overdue" overlays the real status for display purposes only —
            // the stored Status column is never set to "overdue".
            public static string DisplayStatus(string? status, DateTime dueDate) =>
                IsOverdue(status, dueDate) ? "overdue" : Normalize(status);

            public static string DisplayLabel(string? status, DateTime dueDate) =>
                DisplayStatus(status, dueDate) switch
                {
                    "overdue" => "Overdue",
                    InProgress => "In Progress",
                    ForReview => "For Review",
                    ReturnedForCorrection => "Returned for Correction",
                    Completed => "Completed",
                    _ => "To Do"
                };
        }
    }
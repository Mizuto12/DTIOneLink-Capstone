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

            // Delay-risk indicator (rule-based: time used vs. progress made).
            // Returns null when no badge applies: completed, already overdue
            // (the status itself says so), or waiting for review.
            public static (bool AtRisk, string Reason)? DelayRisk(string? status, int progress, DateTime createdAt, DateTime dueDate)
            {
                var current = Normalize(status);
                if (current == Completed || current == ForReview || IsOverdue(status, dueDate))
                {
                    return null;
                }

                var today = DateTime.UtcNow.Date;
                var start = createdAt.Date;
                var totalDays = Math.Max(1, (dueDate.Date - start).Days);
                var usedDays = Math.Clamp((today - start).Days, 0, totalDays);
                var daysLeft = (dueDate.Date - today).Days;
                var timeUsedPct = usedDays * 100 / totalDays;

                var dueSoon = daysLeft <= 3;
                var atRisk =
                    (usedDays >= 2 && timeUsedPct - progress >= 25) ||
                    (dueSoon && progress < 50) ||
                    (dueSoon && current == ReturnedForCorrection);

                var when = daysLeft == 0 ? "Due today" : daysLeft == 1 ? "Due tomorrow" : $"Due in {daysLeft} days";
                var reason = dueSoon
                    ? $"{when}, progress {progress}%."
                    : $"{usedDays} of {totalDays} days used, progress {progress}%.";
                return (atRisk, reason);
            }

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
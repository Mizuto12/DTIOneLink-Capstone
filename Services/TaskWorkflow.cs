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
            // by the employee directly — except InProgress -> Completed,
            // reached only by EmployeeController.SubmitProof when
            // TaskItem.RequiresReview is false (bypass review entirely).
            // Deliberately NOT added from ReturnedForCorrection: once an
            // Admin has already engaged with a submission once, a
            // resubmission always goes back through review, regardless of
            // RequiresReview.
            private static readonly Dictionary<string, string[]> AllowedTransitions = new()
            {
                [Pending] = new[] { InProgress, ForReview },
                [InProgress] = new[] { ForReview, Completed },
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

            // An employee must make real progress before proof can be
            // submitted — otherwise a task can jump straight from 0% to
            // "for review". Resubmission after a Return is exempt: progress
            // was already sufficient the first time, and Return never resets it.
            public const int MinProgressToSubmitProof = 80;

            public static bool CanSubmitProof(string? status, int progress) =>
                Normalize(status) == ReturnedForCorrection || progress >= MinProgressToSubmitProof;

            // Decision 9: max checklist items on a single assignment.
            public const int MaxChecklistItemsPerAssignment = 10;

            // A due date with no time-of-day (every task created before
            // this feature, and any new one left without a specific time)
            // means "by the end of that day", not "at its very start" —
            // otherwise every pre-existing task would flip to overdue the
            // instant its due day began, a silent shift nothing asked for.
            public static DateTime EffectiveDueMoment(DateTime dueDate) =>
                dueDate.TimeOfDay == TimeSpan.Zero ? dueDate.Date.AddDays(1) : dueDate;

            public static bool IsOverdue(string? status, DateTime dueDate) =>
                Normalize(status) != Completed && EffectiveDueMoment(dueDate) <= TimeZoneHelper.PhilippineNow;

            // Decision 5: how long a reviewer has to act once proof is
            // submitted, before DelayRisk's review-stage branch (below)
            // considers the review itself overdue.
            public static readonly TimeSpan ReviewWindow = TimeSpan.FromDays(3);

            // "Due soon" = due within this many days (today counts as 0).
            public const int DueSoonDays = 3;

            // Unfinished and due within DueSoonDays, or already overdue. An
            // Admin may then move an OPD task's due date past the OPD's own
            // deadline (TasksController.Edit).
            public static bool IsDueSoonOrOverdue(string? status, DateTime dueDate) =>
                Normalize(status) != Completed && (dueDate.Date - TimeZoneHelper.PhilippineToday).Days <= DueSoonDays;

            // "overdue" overlays the real status for display purposes only —
            // the stored Status column is never set to "overdue".
            public static string DisplayStatus(string? status, DateTime dueDate) =>
                IsOverdue(status, dueDate) ? "overdue" : Normalize(status);

            // Delay-risk indicator (rule-based: time used vs. progress made).
            // Returns null when no badge applies: completed, already overdue
            // by its own due date (the status itself says so), or a ForReview
            // row from before ReviewDueDate existed (reviewDueDate null).
            public static (bool AtRisk, string Reason)? DelayRisk(string? status, int progress, DateTime createdAt, DateTime dueDate, DateTime? reviewDueDate)
            {
                var current = Normalize(status);
                if (current == Completed)
                {
                    return null;
                }

                if (current == ForReview)
                {
                    // Judged against the review deadline, not the task's own
                    // due date — the employee already submitted, so that
                    // deadline no longer applies to them. Previously this
                    // branch always returned null, making an overdue-for-
                    // review task invisible everywhere this is shown.
                    if (reviewDueDate == null)
                    {
                        return null;
                    }

                    var overdue = DateTime.UtcNow > reviewDueDate.Value; // both UTC — see TaskItem.ReviewDueDate
                    var reviewDueLocal = TimeZoneHelper.ToPhilippineTime(reviewDueDate.Value);
                    var reviewReason = overdue
                        ? $"Review was due {reviewDueLocal:MMM d, h:mm tt} and is still pending."
                        : $"Awaiting review, due {reviewDueLocal:MMM d, h:mm tt}.";
                    return (overdue, reviewReason);
                }

                if (IsOverdue(status, dueDate))
                {
                    return null;
                }

                var today = TimeZoneHelper.PhilippineToday;
                var start = TimeZoneHelper.ToPhilippineTime(createdAt).Date; // CreatedAt is stored in UTC
                var totalDays = Math.Max(1, (dueDate.Date - start).Days);
                var usedDays = Math.Clamp((today - start).Days, 0, totalDays);
                var daysLeft = (dueDate.Date - today).Days;
                var timeUsedPct = usedDays * 100 / totalDays;

                var dueSoon = daysLeft <= DueSoonDays;
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
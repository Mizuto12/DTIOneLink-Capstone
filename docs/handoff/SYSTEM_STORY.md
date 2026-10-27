# SYSTEM_STORY.md

A narrative walkthrough of the system from each role's perspective, grounded
in the controller/service logic actually read in this pass (see
`TECHNICAL_REFERENCE.md` for exact file/line anchors). Names are fictional.

---

## Story 1 — Juan, a new Employee in the Business Development Division

**Day 1.** Juan's account was just created by his division's Admin via
`UserManagementController.Create`. He's given the default password
(`AccountDefaults.DefaultPassword`) and told to sign in at `/`.

He types his email and the default password into `Login.cshtml`. On POST,
`AccountController.Login` finds his `User` row, hashes/verifies the password
(`PasswordHasher<User>`), and since `MustChangePassword` was set `true` at
account creation, it does **not** establish a session yet — it stashes his
user id in `Session["PendingUserId"]` and redirects him to `VerifyEmail`
(because `EmailConfirmed` is still `false`).

A 6-digit code is emailed via Brevo (or, if Juan were testing locally with no
Brevo key configured, printed to the developer's console instead —
`UnconfiguredEmailSender`). He types the code into `EnterCode.cshtml`;
`AccountController.VerifyEmail` (POST) validates it against
`OneTimeCodeService.VerifyAsync`, sets `EmailConfirmed = true`, and routes him
to `SetPassword`. He picks a new password; `NewPasswordProblem` rejects it if
it has whitespace/emoji, is too short, matches the banned default, is a common
password, or is unchanged from before. Once accepted,
`SavePasswordAsync` hashes it, clears `MustChangePassword`, rotates his
`SecurityStamp` (so no one else holding an old session for his account stays
signed in), and emails him a "password changed" notice.

`ContinuePendingSignIn` now finds nothing left pending, so it calls
`SignIn(user)`: a session is established (`AccountSessionService.EstablishSession`),
his `LastLoginAtUtc`/`LastSeenAtUtc` are stamped, any stale open `TimeLog` row
is closed defensively, and a fresh `TimeLog` row is opened for this session.
He lands on `/Employee/Index` (`RoleHomeUrl` routes Employee there).

**Working a task.** His division's Admin assigned him to a subtask under a
Department Directive. `NotificationService.NotifyTaskAssignedAsync` already
fired when that happened, so Juan sees a bell notification linking to
`/Employee/Details/{taskId}`. Opening it calls
`EmployeeController.GetAccessibleTaskAsync`, which for a plain Employee only
ever matches a task where he personally holds a `TaskAssignment` row — he
cannot reach a task he isn't on by guessing its id (`NotFound`, not `403`, so
existence isn't leaked either).

He drags his progress slider to 60% (`POST /Employee/UpdateProgress`).
`ApplyProgressUpdateAsync` checks his assignment's current status isn't
already `completed` or `for-review` (both frozen), allows the implicit
`pending → in-progress` transition since progress moved off zero, saves, and
recalculates the task's aggregate `Status`/`Progress`
(`TaskAssignmentService.RecalculateOverallStatus`) — on a shared task, his
contribution and his co-assignee's add together (capped at 100), rather than
averaging, matching the "stockpile" rule in `TaskAssignmentService`'s header
comment. Because this subtask belongs to a Main Task,
`PropagateToParentMainTaskAsync` also recomputes the Directive's own rollup.

At 80%+ he can submit proof. He uploads a photo via
`POST /Employee/SubmitProof`; `ProofFileValidator.Validate` checks it, the
file is saved to `App_Data/ProofUploads` under a random GUID filename (never
his original filename, so nothing about the stored path leaks what he
uploaded), and a new `TaskSubmission` row is appended — his submission
history, never overwritten. His assignment flips to `for-review` and is now
frozen until a decision comes back. Because this isn't a Direct Admin/OPD
task, his division's Admins are notified
(`NotifyAdminsProofSubmittedAsync`), linking them straight to
`/Tasks/Review/{submissionId}`.

**Returned for correction.** His Admin reviews it and returns it with
required remarks ("re-take the photo, the form isn't legible"). Juan's
assignment status becomes `returned-for-correction`
(`TasksController.Review`, POST), he's notified
(`NotifyTaskReturnedAsync`, which also emails him), and when he resubmits,
`TaskWorkflow.CanSubmitProof` exempts him from the 80%-progress gate this time
(he already cleared it once) — he can resubmit at any progress level.

**Completed.** Next time it's approved, his assignment becomes `completed`
(`Progress` forced to 100 for display consistency), and —
because this subtask belongs to a Directive — the service re-checks whether
*every* subtask under that Directive is now Completed/ForReview; if the whole
Directive just finished, the OPD (SuperAdmin) is notified
(`NotifyOpdDirectiveCompletedAsync`). Juan can later delete this one task from
his own list (`POST /Employee/Delete`) since it's his, it's Completed, and
nothing depends on it — permanently removing it, its proof file, comments,
and activity history in one transaction.

---

## Story 2 — Liza, Admin (Division Chief) of the Consumer Protection Division

Liza's dashboard (`GET /Dashboard/AdminDashboard`) shows only tasks whose
effective department is hers — her own `OwningDepartment`, or inherited from
a parent Main Task, or (for legacy pre-department tasks only) any assignee's
department — plus anything personally assigned to her regardless of
department (e.g. a Whole Office task). This same effective-department rule
is re-implemented, not shared, across `TasksController`, `EmployeeController`,
`DashboardController`, and `ReportsController` — confirmed independently in
each file.

**Receiving a Directive.** The OPD (SuperAdmin) created a Department
Directive targeting her division and named her the Responsible Admin
(`ValidateResponsibleAdminAsync` enforced, server-side, that she's an active
Admin actually in that division). She's notified
(`NotifyAdminDirectiveReceivedAsync`, emailed) and sees it on
`/Tasks/ReceivedTasks`. Opening `/Tasks/MainTaskDetails/{id}` shows the
Directive's own rolled-up status/progress — computed purely from its
subtasks (`RecalculateMainTaskFromSubtasks`), never set directly.

She adds subtasks — wait, actually she **can't**: `AddSubtask` is gated to
`IsOfficeWideTaskManager()` only, meaning only the OPD adds subtasks to a
Directive, even though the Directive was handed to her. What she *can* do is
assign existing subtasks (created by the OPD, or ones that already had zero
subtasks) to her employees via `/Tasks/Edit/{subtaskId}`, restricted to
employees in her own division (`PopulateEmployeesAsync`,
`ValidateAssigneeIdsAsync`).

**Reviewing work.** When Juan (Story 1) submits proof, she sees it on
`/Tasks/Review/{submissionId}`. `CanReviewSubmission` confirms she's allowed:
this isn't a Direct Admin Task (those go to OPD only), she's department-scoped
(not office-wide), and the task's effective department matches hers. She
approves or returns it; returning requires remarks.

**Managing her roster.** On `/UserManagement`, she sees all users (the query
itself isn't department-filtered at the SQL level — confirmed in
`UserManagementController.Index`), but her *actions* are scoped:
`CanSetActive` only lets her deactivate/reset-password/edit-email for
Employees in her own division, never another Admin, never herself, and never
anyone outside her division. If she tries to deactivate someone with
unfinished work, `CountOpenWorkAsync` blocks it and hands her a direct link
to that person's filtered task list (`Url.Action("Index", "Tasks", new {
employeeId = id })`).

**Logging a record.** On `/Records`, she logs a new record. The form's eight
visible fields are validated server-side (`ValidateEntry`); the system stamps
`CreatedByUserId` (her), `OwningDepartment` (her division), `RecordDate`
(today, Philippine time), and computes `RetentionDueDate` from
`RetentionPeriodParser`. Because she holds `ManageRecords`, she can also see
and edit *every* record office-wide (`VisibilityClause` returns `1=1` for
her) — not just her own — which is what lets her actually do disposal
validation across her whole division's records, not just the ones she
personally typed in.

When a record's retention period nears its end, she (as the owner, or any
manager) sees the yellow "due-soon" badge, and gets an in-app + emailed
reminder (`RecordRetentionReminder` via the hourly background service, or
immediately the next time she opens her notification bell). She can manually
flag it into `Disposal` status early, or wait for the background job to do it
automatically once due. Once the physical/electronic record is actually
disposed of outside the system, she confirms it with `MarkDisposed` — a
one-way action; there's no "un-dispose."

---

## Story 3 — The OPD (SuperAdmin), Office-Wide

The OPD user holds `ManageOfficeWideTasks`, `ViewOfficeWideSummaries`,
`ViewConfidentialRecords`, `ApproveRecords`, and `ManageUserAccounts` — but
deliberately **not** `ManageTasks` or `ManageRecords` themselves (see
`Security/RolePermissions.cs`'s inline comments: office-wide task access is
intentionally a separate permission from the department-scoped one, so
department scoping elsewhere is never accidentally bypassed by also granting
the department-scoped permission to SuperAdmin).

**Creating work.** Only the OPD can create a task at all
(`TasksController.Create` checks `IsOfficeWideTaskManager()`). They pick a
type:
- **Whole Office** — ignores any department/Responsible Admin field; every
  active Admin and Employee gets their own `TaskAssignment` row.
- **Direct Admin Task** — assigned straight to one named Admin, who then
  works it through the ordinary Employee-style workflow (progress, proof,
  comments) — but it is *reviewed only by the OPD*, never by that Admin's
  department peers, and never by the Admin themself.
- **Department Directive** — no individual assignee yet; goes to a named
  Responsible Admin who distributes it via subtasks.

They can set it to recur (daily/weekly/monthly/quarterly); the recurrence
setting travels forward onto each new copy the background job creates, never
staying on an old, already-superseded copy.

**Office-wide oversight.** `/Dashboard/SuperAdminDashboard` (gated by
`ViewOfficeWideSummaries`) shows every `TaskAssignment` row office-wide,
grouped into workload summaries per employee with overdue/at-risk flags
computed from the same `TaskWorkflow` rules every other view uses — never a
separately invented metric.

**Covering for an absent Admin.** If a Division Chief goes on leave mid-
Directive, the OPD uses `ReassignResponsibleAdmin` to hand the whole
Directive (and, for a Direct Admin Task specifically, the underlying
`TaskAssignment` itself) to a different Admin — who must still belong to the
Directive's own `OwningDepartment`. This is the one deliberate exception to
strict department scoping: a Responsible Admin reassigned across departments
keeps full access to that one Directive and its subtasks, confirmed
consistently in `IsWithinTaskScope`, `IsWithinMainTaskScope`,
`CanReviewSubmission`, and `GetDepartmentAdminIdsAsync`.

**Approving a record.** The OPD visits `/Records` and sees every record
office-wide (their `ApproveRecords` permission also satisfies
`VisibilityClause`'s `1=1` branch). They click Approve on one that's been
flagged for disposal review — this doesn't block or change the record's
workflow at all; it just stamps `ApprovedByUserId`/`ApprovedAt` as a
sign-off trail, matching the stated real-office process ("PD approves, then
it's submitted to the records officer for disposal validation" — code
comment in `Security/RolePermissions.cs`). If anyone edits the record's
content afterward, the approval is automatically cleared (`Update` nulls both
fields) — an approval never silently survives a content change.

---

## Story 4 — What happens with no human involved (background jobs)

Every hour, four singleton `BackgroundService`s wake up, each opening its own
DI scope (because `AppDbContext`/`NotificationService` are request-scoped,
not singletons):

- `TaskReminderService` walks every incomplete `TaskAssignment`. Overdue ones
  get the employee notified (in-app + email, as of commit `42efeba`) and —
  once per task per run — the relevant department Admins and/or OPD too,
  resolved via `NotificationService.ResolveEffectiveDepartment`/
  `ResolveOpdRecipients`, never from anything client-supplied. Due-within-2-
  days (but not yet overdue) assignments get an in-app-only "due soon" nudge.
  Department Directive Main Tasks (which have no `TaskAssignment` rows of
  their own) are checked separately in the same run.
- `RecordRetentionReminderService` runs `RecordRetentionReminder` for every
  record owner, so an owner who never opens the app still eventually gets
  their reminder (a signed-in user's own records are also checked
  immediately whenever they open their notification bell, via
  `NotificationsController.List`).
- `EmailDispatchService` drains the `EmailOutbox` table (every notice above
  writes there in the same save as the in-app notification, never as a
  separate round trip) and actually calls Brevo.
- `LiveChangeBroadcaster` watches a per-entity "fingerprint" and pushes a
  SignalR refresh signal to any open page whose data moved, so (for example)
  a second Admin reviewing a task sees Juan's new submission appear without
  manually reloading.

An external uptime monitor (per `Program.cs` comment, UptimeRobot) pings
`/health` every few minutes specifically so the hosting provider never puts
the idle app to sleep — which would otherwise silently stop all four of these
jobs between pings.

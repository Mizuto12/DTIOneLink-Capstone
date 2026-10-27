# FEATURE_INVENTORY.md

Status categories used below (Phase 6 of the discovery brief):
**Verified implemented** · **Partially implemented** · **UI only/placeholder**
· **Apparently missing** · **Runtime unverified** · **Unclear**

All line references are to files read directly in this pass; see
`TECHNICAL_REFERENCE.md` for the fuller file/symbol index.

---

## Module: Accounts & Authentication (`AccountController`, `Security/`, `Services/OneTimeCodeService.cs`, `Services/AccountSessionService.cs`)

### 1.1 Password sign-in with lockout
- **Purpose:** Authenticate via username/email + password; lock out after
  repeated failures.
- **Access:** Anyone (unauthenticated).
- **Entry point:** `GET/POST /Account/Login`.
- **Backend:** `AccountController.Login` — `PasswordHasher<User>`,
  `AccountDefaults.MaxFailedLogins`/`LockoutDuration`. Verifies against a
  precomputed `DummyHash` when the username doesn't exist, specifically to
  keep response timing from revealing whether an account exists.
- **DB:** `Users.FailedLoginCount`, `Users.LockoutEndUtc`.
- **Status:** **Verified implemented.**

### 1.2 Forced password change + email OTP (first sign-in / admin reset)
- **Purpose:** New accounts (default password) and admin-issued temporary
  passwords must be replaced before use; email must be confirmed via a code
  first if not yet confirmed.
- **Entry point:** `GET/POST /Account/VerifyEmail`, `/Account/SetPassword`.
- **Backend:** `AccountController` pending-session keys (`PendingUserIdKey`
  family), `OneTimeCodeService.IssueAsync/VerifyAsync`.
- **Status:** **Verified implemented.**

### 1.3 Login OTP (second factor) + "remember this device"
- **Purpose:** After a correct password (and no forced change needed), a
  6-digit code is emailed before the session is established — unless the
  browser already holds a valid "remember device" cookie.
- **Entry point:** `GET/POST /Account/VerifyLoginCode`, `/Account/ResendLoginCode`.
- **Backend:** `AccountController` (`PendingLoginUserIdKey` family),
  `AccountSessionService.HasTrustedDevice/IssueTrustedDeviceCookieAsync`.
- **Note:** Skipped entirely in the `Testing` ASP.NET environment (no real
  inbox to read a code from) — confirmed in `AccountController.Login`.
- **Status:** **Verified implemented.**

### 1.4 Forgot password (emailed link) / Change password (signed-in)
- **Entry points:** `GET/POST /Account/ForgotPassword`, `/Account/ResetLink`,
  `/Account/ResetPassword`, `GET/POST /Account/ChangePassword`.
- **Backend:** `AccountController`; same response regardless of whether the
  email matches an account (anti-enumeration, by design per inline comment).
- **Password rule:** ASCII-printable, no whitespace, minimum length
  (`AccountDefaults.MinPasswordLength`), rejects the shared default password
  and a common-password list, rejects reuse of the current password.
- **Status:** **Verified implemented.**

### 1.5 Security stamp / forced sign-out on role, password, or email change
- **Purpose:** Any session started before the account's last password/email
  change is signed out automatically on its next request.
- **Backend:** `User.SecurityStamp`, middleware in `Program.cs`.
- **Status:** **Verified implemented.**

### 1.6 "Signed in / Signed out / Not signed in yet" status
- **Purpose:** User Management shows live-ish sign-in status.
- **Backend:** `User.LastLoginAtUtc/LastLogoutAtUtc/LastSeenAtUtc`, written by
  `AccountController.SignIn`/`Logout` and the `Program.cs` middleware.
- **Status:** **Verified implemented** (display logic itself lives in a
  Razor view not opened in this pass — **runtime unverified** for the exact
  on-screen wording/thresholds beyond what `SYSTEM_OVERVIEW.md` states).

---

## Module: Roles & Permissions (`Security/`, `Filters/`)

### 2.1 Permission-string authorization model
- **Backend:** `Security/Permissions.cs` (constants),
  `Security/RolePermissions.cs` (role→permission map),
  `Filters/RequirePermissionAttribute.cs`, `Filters/RequireLoginAttribute.cs`.
- **Status:** **Verified implemented.** Several controllers additionally
  layer their own, more granular department-scoping logic beyond the
  attribute (e.g. `TasksController.IsWithinTaskScope`) — the attribute alone
  does not express department boundaries.

### 2.2 Role/division change, deactivation, password reset, email correction (admin tools)
- **Entry point:** `UserManagementController` — `Create`, `ChangeStanding`,
  `SetActive`, `ResetPassword`, `EditEmail`.
- **Business rules confirmed in code:**
  - Only SuperAdmin can create Admin/SuperAdmin accounts or change anyone's
    role/division (`ChangeStanding`'s explicit SuperAdmin-only check).
  - An Admin may only create Employees inside their own department.
  - A person with unfinished tasks (including being the `ResponsibleAdmin` of
    a still-repeating task) cannot be deactivated or have their role/division
    changed until that work is reassigned or finished
    (`CountOpenWorkAsync`).
  - Nobody can deactivate or change themselves.
  - Admin-issued password reset generates a random one-time temporary
    password shown once to the admin (never the shared default).
- **Status:** **Verified implemented.**

---

## Module: Tasks (`TasksController`, `EmployeeController`, `Controllers/Admin/DashboardController.cs`, `Services/Task*.cs`)

### 3.1 Task data model: Main Task vs. Subtask, three Main Task types
- **Model:** `TaskItem.TaskLevel` (`Services/TaskLevels.cs`: `main`/`subtask`),
  `TaskItem.TaskType` (`Services/TaskTypes.cs`: `direct-admin`,
  `department-directive`, `whole-office`), self-referencing `ParentTaskId`.
- **Status:** **Verified implemented.**

### 3.2 Task creation (OPD/SuperAdmin only)
- **Entry point:** `GET/POST /Tasks/Create`.
- **Backend:** `TasksController.Create` → `OpdTaskService.CreateAsync`.
- **Rules confirmed:** Only `ManageOfficeWideTasks` holders (SuperAdmin) can
  create a task at all; a "Whole Office" task is auto-assigned to every
  active Admin+Employee and ignores any posted department/Responsible Admin;
  a "Direct Admin" task requires and validates a Responsible Admin belonging
  to the chosen department; a "Department Directive" may optionally include
  inline subtask names at creation.
- **Status:** **Verified implemented.**

### 3.3 Recurring tasks
- **Backend:** `Services/TaskRecurrence.cs` (daily/weekly/monthly/quarterly
  validity), `BackgroundService/RecurringTaskService.cs` (not opened line-by-
  line this pass, but referenced consistently across `TasksController` and
  `UserManagementController`'s `CountOpenWorkAsync` comments), `StopRepeating`
  action.
- **Status:** **Verified implemented** (creation/validation and the stop
  action are directly confirmed; the background job's exact firing logic is
  **runtime unverified** — not opened this pass, see Limitations).

### 3.4 Task editing, multi-assignee assignment, reassignment
- **Entry point:** `GET/POST /Tasks/Edit/{id}`.
- **Backend:** `TasksController.Edit`, `Services/TaskAssignmentService`
  (`SyncAssignmentsAsync`, `Reassign`, `RecalculateOverallStatus`,
  `RecalculateMainTaskFromSubtasks`/`PropagateToParentMainTaskAsync`).
- **Confirmed rules:** Nobody is silently removed from a task — only
  reassigned (progress carries to the new person); a person with existing
  submissions cannot be dropped from a task's assignee list at all (blocked,
  surfaced as a validation error); reassignment is blocked once an
  assignment is `for-review` or `completed`; a Main Task with subtasks has no
  assignment model of its own for *any* role, including SuperAdmin — work
  (and so assignment) happens only at the subtask level once subtasks exist.
- **Status:** **Verified implemented.**

### 3.5 Department scoping for Admin/Supervisor
- **Confirmed pattern (repeated identically across `TasksController`,
  `EmployeeController`, `DashboardController`, `ReportsController`):**
  effective department = task's own `OwningDepartment`, else parent Main
  Task's, else (legacy-only fallback) any assignee's own `Department`. A
  directive's Responsible Admin can access/review it even cross-department
  (deliberate, for covering an absent Admin).
- **Status:** **Verified implemented**, and consistently re-implemented
  (not shared via one helper) across at least 4 controllers — see
  `LIMITATIONS_AND_OPEN_QUESTIONS.md` for the duplication risk this creates.

### 3.6 Task workflow status machine
- **Backend:** `Services/TaskWorkflow.cs` — states `pending → in-progress →
  for-review → (completed | returned-for-correction → for-review)`.
  `Completed` is terminal; `for-review` can only be resolved by an admin
  decision, never by the employee.
- **Status:** **Verified implemented.**

### 3.7 Priority auto-suggestion
- **Backend:** `Services/PrioritySuggestionService.cs` (not opened this pass
  in full, but its contract — `Suggest(dueDate, taskType)` returning
  `{Priority, Reason}` — is used identically by `TasksController.Create`,
  `Edit`, and `SuggestPriority` endpoint). Rule per
  `docs/requirements-documentation.md`: overdue/≤2 days → High, 3–7 days →
  Medium, >7 days → Low.
- **Status:** **Verified implemented** for the integration points;
  **runtime unverified** for the exact threshold constants (file itself not
  opened).

### 3.8 Proof-of-completion submission (employee)
- **Entry point:** `GET/POST /Employee/SubmitProof/{id}`.
- **Backend:** `EmployeeController.SubmitProof`, `Services/ProofFileValidator.cs`
  (type/size validation — not opened this pass, **runtime unverified** for
  exact rules), stores the file under `App_Data/ProofUploads` with a GUID
  filename (never the original name) and inserts a new `TaskSubmission` row
  per attempt (append-only submission history, never overwritten).
- **Gate:** requires ≥80% progress (`TaskWorkflow.MinProgressToSubmitProof`)
  unless resubmitting after a Return.
- **Status:** **Verified implemented.**

### 3.9 Admin review (approve / return for correction)
- **Entry point:** `GET/POST /Tasks/Review/{submissionId}`.
- **Backend:** `TasksController.Review`, `CanReviewSubmission` (SuperAdmin
  reviews Direct Admin Task submissions only; department Admin reviews
  everything else, department-scoped; an Admin can never review their own
  Direct Admin Task submission).
- **Confirmed:** Only two outcomes — approve or return; remarks are
  *required* on Return, optional on Approve. Matches
  `docs/requirements-documentation.md`'s correction (manuscript originally
  implied three outcomes).
- **Status:** **Verified implemented.**

### 3.10 Threaded comments + activity log
- **Entry point:** `POST /Employee/AddComment`; viewed inline on task detail
  pages.
- **Backend:** `Models/TaskComment.cs`, `Models/TaskActivity.cs`,
  `Services/TaskActivityLogger.cs` (append-only).
- **Status:** **Verified implemented.** The requirement that a comment can be
  "marked as a formal revision note" (`docs/requirements-documentation.md`
  §3) was **not found** anywhere in `TaskComment.cs` or `AddComment` — no
  boolean/flag field for this exists on the model read in this pass.
  **Apparently missing** (or implemented elsewhere not yet located — flagged
  in Limitations rather than asserted missing with full confidence).

### 3.11 Task deletion (employee's own completed work; OPD's whole directives)
- **Entry points:** `POST /Employee/Delete`, `/Employee/DeleteAllCompleted`,
  `POST /Tasks/Delete/{id}`.
- **Rules confirmed:** An employee may only delete a task they're assigned to
  once it is fully `Completed`, and only if nothing depends on it (no
  subtasks reference it). OPD may delete an entire Main Task + its subtask
  tree, but only if *zero* real work has occurred anywhere in that tree
  (no progress > 0, no status beyond Pending, no submissions ever). Deletion
  cascades to assignments, submissions (+ their files on disk, removed after
  DB commit), comments, activity history, and related notifications, inside
  one DB transaction (`TaskAssignmentService.PermanentlyDeleteTasksAsync`).
- **Status:** **Verified implemented.**

### 3.12 Workflow Monitoring list (filter/sort/paginate)
- **Entry point:** `GET /Tasks/Index`.
- **Backend:** `TasksController.BuildIndexModelAsync` — search, status,
  priority, department (SuperAdmin only), employee, due-date range, sort,
  with live-computed status counts (tiles) and pagination.
- **Status:** **Verified implemented.**

### 3.13 Responsible Admin reassignment (directive handoff)
- **Entry point:** `POST /Tasks/ReassignResponsibleAdmin/{id}`.
- **Status:** **Verified implemented** (OPD-only; validates the new Admin is
  active, in-role, and in the task's own department).

### 3.14 Kanban board view
- Referenced in `docs/requirements-documentation.md` §9 and view file names
  (`Views/Employee/TaskManagement.cshtml`). **Not opened in this pass** — the
  view markup/JS that renders a Kanban board was not read.
- **Status:** **Runtime unverified** (controller-side data is confirmed
  present via `AccessibleTasksQuery`; the board rendering itself is not).

---

## Module: Records (`RecordsController`, `Services/RecordMasterlist*.cs`, `Services/Record­Retention*.cs`)

### 4.1 Log / edit / delete a record
- **Entry points:** `GET /Records`, `POST /Records/Save`, `/Records/Update`,
  `/Records/Delete/{id}`.
- **Fields confirmed:** Code, Title, Medium, Location, PeriodCovered,
  FilingSystem, AccessControl, RetentionPeriod — each with standard
  dropdown choices plus a free-typed "Other/Specify" fallback
  (`NormalizeChoice`). Server-side length/control-character validation on
  every text field (`CheckText`).
- **Ownership rule:** A record can be edited/deleted by its own creator
  (`CreatedByUserId`) or anyone with `ManageRecords` (Admin) — including
  after it has been saved into a Masterlist.
- **Status:** **Verified implemented.**

### 4.2 Row-level visibility
- **Rule confirmed (`VisibilityClause`):** `ManageRecords` (Admin) or
  `ApproveRecords` (SuperAdmin) holders see every record office-wide;
  everyone else sees only the records they personally logged
  (`CreatedByUserId`). A record logged before the owner column existed
  (legacy) is visible to nobody but managers/approvers.
- **Status:** **Verified implemented.**

### 4.3 Retention due-date computation and color-coded status
- **Backend:** `Services/RetentionPeriodParser.TryComputeDueDate` (not opened
  in full this pass), `RecordsController.RetentionState` — "due-soon"
  (yellow) within `RecordRetentionReminder.ReminderMonths`, "ended" (red)
  once past due. Status only computed/shown for records still in
  `Retention` status.
- **Status:** **Verified implemented** for the integration;
  **runtime unverified** for the exact date-math inside
  `RetentionPeriodParser` (file not opened).

### 4.4 Disposal-status lifecycle: Retention → Disposal → Disposed, and
    → Archived / Restore-to-Retention
- **Entry points:** `POST /Records/MarkForDisposal`, `/MarkDisposed`,
  `/MarkArchived/{id}`, `/RestoreToRetention/{id}`.
- **Confirmed semantics:**
  - `MarkForDisposal` is a manual early flag; the background reminder also
    auto-promotes a record to `Disposal` once due (see 4.6).
  - `MarkDisposed` is a manual, one-way confirmation — no code path reverses
    it (deliberately: the system can't know whether reversing it would be a
    lie about something that already physically happened).
  - `MarkArchived` is a distinct terminal-ish outcome for permanent-value
    records (RA 9470/NAP framing in the code comment) — **not** a synonym for
    Disposed, and raises no disposal notification.
  - `RestoreToRetention` is the **only** reversible transition, and only
    `FROM Archived specifically` (enforced server-side via
    `requireCurrentStatus`, with a 409 Conflict if the record moved on since
    the page loaded).
  - Authorization: the record's own creator or a records manager (Admin) —
    same ownership rule as Edit/Delete. Explicitly **not** gated by
    confidentiality, since visibility already limits a non-manager to their
    own records.
- **Status:** **Verified implemented.** This directly supersedes the root
  `SYSTEM_OVERVIEW.md`/`PROJECT-STATUS.md` claim ("Records only notify for
  disposal; no disposal workflow" / "Record archiving ... ruled out") —
  those notes are dated 2026-10-06/07, and `Archived` status plus
  `RestoreToRetention` exist in the current code (migration
  `20261020000000_RenameRecordStatusValues.cs` and the controller actions
  above). See `LIMITATIONS_AND_OPEN_QUESTIONS.md`.

### 4.5 PD (SuperAdmin) Approval sign-off
- **Entry point:** `POST /Records/Approve/{id}`.
- **Confirmed:** `ApproveRecords`-only (SuperAdmin); does not block or alter
  any other record behavior — purely an audit/sign-off field
  (`ApprovedByUserId`, `ApprovedAt`); cleared automatically if the record is
  subsequently edited (`Update` sets both to `NULL` on save, so an approval
  never silently survives a content change).
- **Status:** **Verified implemented.**

### 4.6 Retention/disposal reminders (in-app + email)
- **Backend:** `Services/RecordRetentionReminder.cs` (not opened in full —
  **runtime unverified** for its exact promotion logic), invoked both by
  `BackgroundService/RecordRetentionReminderService` (hourly, every owner)
  and on-demand inside `NotificationsController.List` (checks the current
  viewer's own records on every bell-list load, so they don't wait for the
  hourly run). `NotificationService.NotifyRecordDisposalDueAsync` /
  `NotifyRecordMarkedForDisposalAsync` / `NotifyRecordDisposedAsync` send the
  in-app + emailed notice; emails deliberately omit the record's title
  (confidentiality — stated in a code comment).
- **Status:** **Verified implemented.**

### 4.7 Search/filter record list
- **Entry point:** `GET /Records/GetAll`.
- **Confirmed filters:** free-text (`Code`/`Title`/`Location`/`PeriodCovered`,
  SQL `LIKE` with manual escaping via `EscapeLike`), Medium, Access Control
  ("other" matching anything outside the standard list too), retention state
  (managers only), RecordStatus.
- **Status:** **Verified implemented.**

### 4.8 Masterlist save / reopen / Excel export
- **Entry points:** referenced via `MasterlistId` column, `OpenMasterlistSessionKey`
  session key, `Services/RecordMasterlistExcel.cs`/`RecordMasterlistPdf.cs`.
- **Status:** **Partially verified** — the schema/session plumbing and the
  "records not yet saved into a masterlist stay on the working table" rule
  are confirmed in `RecordsController.LoadRecords`; the actual
  save/download actions and the Excel/PDF generation code were **not opened**
  in this pass. **Runtime unverified** for the generation details.

### 4.9 Dashboard Records Summary widget
- **Entry point:** `GET /Records/Overview`.
- **Confirmed:** total active records, due-soon count, overdue count, saved-
  masterlist count — scoped per-user for non-approvers, office-wide (but
  disposal-stage-only) for SuperAdmin. Matches
  `docs/requirements-documentation.md` §14's "Records Summary ... Admin
  dashboard" claim; this pass additionally found the same `Overview` endpoint
  reachable regardless of role (any `RecordsRoles` member), so it is not
  strictly Admin-dashboard-exclusive at the controller level — **the view(s)
  that call it were not opened**, so whether SuperAdmin's/Employee's own
  dashboard actually displays this card is **runtime unverified**.

---

## Module: Reports (`ReportsController`)

### 5.1 Unified activity feed with period filter
- **Entry point:** `GET /Reports/Data?from=&to=`.
- **Confirmed:** Combines Active/Completed tasks, Task History
  (`TaskActivity`), and Records (Retained/Archived/Disposed) into one
  category-tagged list, scoped per role exactly like the Tasks/Records
  modules (Employee: own only; Admin: own department; office-wide holders:
  everything). Period filter narrows to items with activity (created or
  changed) inside an inclusive Philippine-date range.
- **Status:** **Verified implemented.**

### 5.2 CSV export
- Referenced only in `docs/requirements-documentation.md` ("downloadable as a
  CSV file generated in the browser") and `PROJECT-STATUS.md` ("Export Data on
  Reports does nothing (no handler)" — dated 2026-10-06, listed as an open
  gap). **No server-side export handler exists** in `ReportsController` (confirmed
  by reading the full file) — any CSV export, if present at all, would have to
  be pure client-side JS not inspected in this pass.
- **Status:** **Unclear / Partially implemented** — contradicts itself across
  the two existing docs; not resolved in this pass. See Limitations.

---

## Module: Notifications (`NotificationsController`, `NotificationService`)

### 6.1 In-app notification bell: list, mark read, mark all read, dismiss
- **Entry points:** `GET /Notifications/List`,
  `POST /Notifications/MarkRead`, `/MarkAllRead`, `/Dismiss`.
- **Confirmed:** Strictly scoped to the session's own `CurrentUserId`; no
  path found for viewing another user's notifications. `List` also
  opportunistically runs the current user's own record-retention check
  inline (see 4.6) before returning results, inside a try/catch that never
  breaks the bell on failure.
- **Status:** **Verified implemented.**

### 6.2 Event-driven notifications (task + record events), with selective email
- **Backend:** `Services/NotificationService.cs` — ~20 distinct notify
  methods, each constructing both the in-app `Notification` row and
  (for the subset that pass `emailSubject`) an `EmailOutboxMessage` row in
  the same `SaveChangesAsync`.
- **Confirmed to trigger an email:** task assigned/reassigned, submission
  approved/returned, OPD directive received, task **overdue** (added per
  commit `42efeba`, "same as Returned and record-disposal notices already
  do" — **this is a change from `docs/requirements-documentation.md` §13's
  "Due-date and overdue task reminders are delivered in-app only, not by
  email," which is now stale**), password changed, record disposal
  due/marked/disposed.
- **Confirmed in-app-only:** due-soon reminders, due-date/priority changes,
  reassignment-away notices, comments, OPD due-date-changed-by-admin notices.
- **Status:** **Verified implemented.**

---

## Module: Dashboards (`Controllers/Admin/DashboardController.cs`)

### 7.1 Admin Dashboard
- **Entry point:** `GET /Dashboard/AdminDashboard`. Department-scoped task
  list (or office-wide for SuperAdmin via `ManageOfficeWideTasks` — note the
  action name is shared, but a SuperAdmin reaching it sees everything).
- **Status:** **Verified implemented.**

### 7.2 Completion Trend widget
- **Entry point:** `GET /Dashboard/GetCompletionTrend` — 8-week approved-
  submission counts, scoped identically to the dashboard itself.
- **Status:** **Verified implemented.**

### 7.3 Super Admin Dashboard
- **Entry point:** `GET /Dashboard/SuperAdminDashboard` (requires
  `ViewOfficeWideSummaries`). Builds office-wide workload summaries per
  employee (To-Do/In-Progress/For-Review/Returned/Completed/Overdue/At-Risk
  counts), rendered by `Views/SuperAdmin.cshtml` (explicit non-conventional
  path).
- **Status:** **Verified implemented.**

### 7.4 Task Details (role-aware single-task view reused from Employee/Details)
- **Entry point:** `GET /Dashboard/Details/{id}`.
- **Status:** **Verified implemented.**

### 7.5 "Delay Risk Indicator"
- **Backend:** `TaskWorkflow.DelayRisk` — rule-based (time elapsed vs.
  progress made, or due-soon with low progress, or due-soon while still
  Returned-for-Correction). Used by both `ReportsController` and
  `DashboardController`/`SuperAdminDashboard`.
- **Status:** **Verified implemented.**

---

## Module: Time Logs (`TimeLogsController`)

### 8.1 Personal Time In/Out history
- **Entry point:** `GET /TimeLogs/Index?from=&to=&page=`.
- **Confirmed:** Strictly the signed-in user's own rows; day-grouped
  pagination (not row-count pagination, to avoid splitting a day across
  pages); rows created/closed by `AccountController.SignIn`/`Logout`.
- **Explicitly not** an attendance system — confirmed by
  `PROJECT-STATUS.md`'s scope ruling and by the absence of any approval,
  export, or HR-facing aggregation code for this data.
- **Status:** **Verified implemented.**

---

## Module: Live Updates (`LiveController`, `LiveHub`, `LiveChangeBroadcaster`, `LiveVersionService`)

### 9.1 SignalR-pushed + poll-fallback page refresh signal
- **Entry points:** SignalR hub `/hubs/live`; polling fallback
  `GET /Live/Version`.
- **Confirmed:** Hub rejects unauthenticated connections (401) before
  `UseAuthorization` even runs, via an early `Program.cs` middleware check.
  `LiveController.Version` returns a per-user data fingerprint plus any
  pending notification flag, with `Cache-Control: no-store`.
- **Status:** **Verified implemented** for the signaling plumbing;
  **runtime unverified** for exactly which entities' changes are tracked
  (`LiveVersionService`/`LiveChangeBroadcaster` internals not opened this
  pass).

---

## Module: PWA / Offline (`wwwroot/manifest.json`, `wwwroot/sw.js`, `wwwroot/offline.html`, "offline-sync.js" per commit message)

### 10.1 Offline status indicator + sync queue
- Referenced by commit `9096cf5`'s message ("`.offline-status-pill` ...
  should stay invisible until offline-sync.js actually has something to
  report — Offline, Syncing, a conflict, or briefly after a queued change
  syncs") and by the presence of a service worker and manifest.
- **Status:** **Runtime unverified** — no controller or JS file for this was
  opened in this pass; existence of *some* offline-handling UI is confirmed
  only via the commit message and static file presence, not by reading the
  actual implementation. Flagged for follow-up in Limitations.

---

## Small / Supporting Features Noted in Passing

- **`/health` endpoint** — public uptime check (`Program.cs`). **Verified implemented.**
- **Data Protection key persistence** (`App_Data/keys`) to keep antiforgery
  cookies stable across host restarts. **Verified implemented** (with a
  detailed first-party rationale comment in `Program.cs` about a prior
  production incident).
- **Rate limiting on auth endpoints** (`Security/AuthRateLimit.cs`,
  `[EnableRateLimiting(AuthRateLimit.Policy)]`) — **Verified implemented**
  at the attribute/wiring level; the policy's exact limits were **not opened**
  this pass (**runtime unverified** for specifics).
- **CSRF protection** — `[ValidateAntiForgeryToken]` on essentially every
  state-changing POST action across every controller read in this pass, plus
  a custom header name (`X-CSRF-TOKEN`) configured in `Program.cs`.
  **Verified implemented.**

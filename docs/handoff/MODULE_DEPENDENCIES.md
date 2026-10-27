# MODULE_DEPENDENCIES.md

Cross-module interactions, as traced through the actual code read in this
pass. "Expected outcome" states what the code is written to do; "Verified
behavior" states what was actually confirmed by reading the implementation
(not by running it — see `LIMITATIONS_AND_OPEN_QUESTIONS.md` for what still
needs runtime verification).

---

## 1. Accounts → everything else (session is the integration point)

- **Initiating action:** `AccountController.SignIn` establishes
  `HttpContext.Session["UserId"/"UserRole"/"UserDepartment"/"FullName"]` (via
  `AccountSessionService.EstablishSession` — session-key names confirmed from
  `PROJECT-STATUS.md` and consistent with every controller's
  `HttpContext.Session.GetString("UserRole")` calls).
- **Destination:** Every other controller (`TasksController`,
  `EmployeeController`, `RecordsController`, `ReportsController`,
  `DashboardController`, `NotificationsController`, `TimeLogsController`,
  `UserManagementController`) reads these same keys directly — there is no
  claims-based `User.Identity`/`ClaimsPrincipal` layer; role/department
  checks are plain session-string comparisons or calls into
  `RolePermissions.Has(role, permission)`.
- **Shared data:** the session itself. No `ClaimsPrincipal`.
- **Expected outcome:** a promotion/demotion or department change by
  `UserManagementController.ChangeStanding` should apply on the affected
  user's *next page load*, not require them to sign out.
- **Verified behavior:** confirmed — `Program.cs`'s per-request middleware
  re-reads `Role`/`Department`/`IsActive`/`SecurityStamp` from the database on
  every request and rewrites the session values if they changed, or clears
  the session entirely if the account is now inactive or the security stamp
  no longer matches (password/email changed elsewhere).
- **Risk:** every controller independently re-implements its own
  role/department check rather than going through one shared authorization
  policy — confirmed by near-identical `IsOfficeWideTaskManager()`/
  `IsDepartmentTaskManager()` private methods duplicated verbatim across
  `TasksController` and `EmployeeController`. A future change to the
  authorization rule must be applied in every copy, or scoping silently
  diverges between modules. (Already happened once per `DashboardController`
  comments: "department scoping ... was previously missing entirely.")

## 2. Tasks → Notifications → Email

- **Initiating action:** almost every task state change in
  `TasksController`/`EmployeeController` (assignment, reassignment, due-date/
  priority edit, comment, proof submission, review decision) calls a
  specific `NotificationService.Notify*Async` method immediately after
  `SaveChangesAsync` for the task-side change.
- **Destination:** `Notifications` table (always) and, for a specific subset
  of notify methods that pass a non-null `emailSubject`, the `EmailOutbox`
  table in the *same* `SaveChangesAsync` call — confirmed in
  `NotificationService.CreateAsync`/`CreateManyAsync`.
- **Order of processing:** task mutation → recalculate aggregate status
  (`TaskAssignmentService`) → `SaveChangesAsync` (task side) →
  `Notify*Async` call(s) (separate `SaveChangesAsync` inside
  `NotificationService`) → (asynchronously, on the next
  `EmailDispatchService` tick) actual Brevo send.
- **Verified behavior:** confirmed the notification write is **not** atomic
  with the task-state save — they are two separate `SaveChangesAsync` calls.
  If the process crashes between them, the task change persists but the
  notification does not. Not confirmed whether this has ever actually
  happened (no error-handling/retry code was found wrapping these calls) —
  flagged as a risk, not a confirmed bug.
- **Shared data:** `TaskItem.Id`, recipient `User.Id`, effective department
  (computed once per call via `ResolveEffectiveDepartment`/
  `GetDepartmentAdminIdsAsync`, never trusted from the caller).

## 3. Records → Notifications → Email (same pattern, independent of Tasks)

- **Initiating action:** `RecordsController`'s disposal-status actions
  (`SetDisposalStatus`) and the background `RecordRetentionReminder` both
  call into the Records-specific notify methods
  (`NotifyRecordDisposalDueAsync`, `NotifyRecordMarkedForDisposalAsync`,
  `NotifyRecordDisposedAsync`).
- **Verified behavior:** the notification recipient is always resolved as
  the record's `CreatedByUserId` (the owner) — never a records manager by
  default, even though managers can see/act on every record. A manager who
  disposes of someone *else's* record on their behalf still notifies the
  original owner, not themselves. Confirmed by reading `SetDisposalStatus`
  and all three `NotifyRecord*` method signatures (`ownerUserId` parameter
  throughout).
- **Deliberate omission:** the emailed version of every Records notice
  strips the record's `Title` — confirmed by comparing the in-app `message`
  text (includes title) against the `emailMessage` text (doesn't) in
  `NotifyRecordDisposalDueAsync`/`NotifyRecordMarkedForDisposalAsync`. This
  is a privacy choice (confidential records), not an oversight — stated
  explicitly in a code comment.

## 4. Tasks (Subtask) → Tasks (Main Task) rollup propagation

- **Initiating action:** any save to a subtask's `TaskAssignment.Status`/
  `Progress` (via `EmployeeController.ApplyProgressUpdateAsync`,
  `SubmitProof`, or `TasksController.Review`/`Edit`).
- **Destination:** the subtask's own `TaskItem` row
  (`RecalculateOverallStatus`), then — if `ParentTaskId` is set —
  the parent Main Task's row (`PropagateToParentMainTaskAsync` →
  `RecalculateMainTaskAsync` → `RecalculateMainTaskFromSubtasks`), all inside
  the same request/transaction before the final `SaveChangesAsync`.
- **Expected outcome:** a Main Task's `Status`/`Progress` is *never* written
  directly by any controller action — it is purely a derived rollup from its
  subtasks (confirmed: no `mainTask.Status =` assignment found anywhere
  outside `TaskAssignmentService`).
- **Verified behavior:** confirmed for every mutation path read in this pass
  (`Edit`, `Review`, `ApplyProgressUpdateAsync`, `SubmitProof`, task
  deletion's directive recompute). **Not independently verified**: whether
  `RecurringTaskService` (not opened this pass) correctly re-derives a fresh
  copy's rollup when it creates the next occurrence of a recurring Main
  Task — flagged in Limitations.
- **Risk:** this two-level rollup (`Assignment → Task`, then
  `Subtask → MainTask`) is called from many different places by hand
  (`RecalculateOverallStatus(task); await PropagateToParentMainTaskAsync(task);`
  appears nearly verbatim in `EmployeeController.ApplyProgressUpdateAsync`,
  `SubmitProof`, and `TasksController.Review`/`Edit`). Missing this call pair
  in a future new mutation path would silently leave a stale rollup — there
  is no automatic trigger (e.g. a DB trigger or EF `SaveChanges` override)
  enforcing it.

## 5. Tasks → User Management (deactivation/role-change blocking)

- **Initiating action:** `UserManagementController.ChangeStanding`/`SetActive`.
- **Destination:** reads `TaskItems`/`TaskAssignments` directly via raw SQL
  (`CountOpenWorkAsync`), **not** through `TaskAssignmentService` or EF.
- **Shared data/rule:** "open work" = any non-completed assignment, or being
  the `ResponsibleAdminUserId` of *any* task (even a completed one) that still
  has `Recurrence IS NOT NULL` — because the recurrence only advances when the
  job runs, not when the current copy is completed early, so a prematurely-
  reassigned/deactivated Responsible Admin would otherwise silently "own" (and
  the job would warn-and-skip) the next copy forever.
- **Verified behavior:** this rule is duplicated — once inline in `Index` (for
  the warning badge shown before a change is attempted) and once in the
  private `CountOpenWorkAsync` helper (for the actual enforcement at save
  time). Confirmed both use the identical SQL predicate, so they should never
  disagree, but this is two copies of the same business rule in raw SQL
  strings, not one shared query — a future edit to one and not the other
  would silently desync the warning from the actual enforcement.
- **Risk:** this check uses raw SQL against `TaskItems`/`TaskAssignments`
  rather than EF's `TaskAssignmentService`/`TaskWorkflow` constants — the
  literal string `'completed'` is hardcoded in the SQL rather than referencing
  `TaskWorkflow.Completed`. If that constant's string value ever changed, this
  SQL would silently stop matching.

## 6. Records ↔ Reports (shared visibility rule, independently implemented)

- **Shared data:** `Records` table, `CreatedByUserId`, `RecordStatus`.
- **Expected outcome (per code comment in `ReportsController.OwnRecordsAsync`):**
  the same visibility rule as `RecordsController.VisibilityClause` — a
  manager/approver sees every record, everyone else only their own.
- **Verified behavior:** confirmed the *rule* is replicated correctly
  (`seesAllRecords = RolePermissions.Has(role, ManageRecords) ||
  RolePermissions.Has(role, ApproveRecords)` matches `VisibilityClause`'s
  condition), but the *mechanism* is different: `RecordsController` uses a
  parameterized raw `SqlCommand` with a `SchemaRecheckInterval`-cached column-
  existence probe (`HasSystemColumns`); `ReportsController.OwnRecordsAsync`
  uses its own separate raw `SqlCommand` against `Services/DatabaseHelper.cs`'s
  connection, with **no** column-existence probing at all — it assumes
  `OwningDepartment`/`RecordStatus`/`RecordDate`/`RetentionDueDate`/`UpdatedAt`
  already exist. If a fresh database were ever missing those optional
  columns (the scenario `RecordsController`'s `HasSystemColumns` exists to
  tolerate), `ReportsController.Data` would likely throw a SQL error reading
  Records — caught generically (`catch (SqlException ex)`) and logged, with
  tasks still loading; **not independently reproduced/confirmed** in this
  pass, since no database was queried.

## 7. Dashboards ↔ Tasks (read-only consumer, no independent business logic)

- **Verified behavior:** `DashboardController` performs no task-state writes
  at all (every action in the file is read-only — confirmed by reading the
  full file: no `SaveChangesAsync` call exists anywhere in it). It reuses
  `TaskWorkflow.Normalize/IsOverdue/DelayRisk` for every computed field,
  rather than re-deriving overdue/at-risk logic itself. Low coupling risk —
  this module cannot desync from Tasks' own state machine since it never
  writes to it.

## 8. Live Updates ↔ (not independently traced this pass)

- `LiveChangeBroadcaster`/`LiveVersionService` are the stated mechanism by
  which Tasks/Records/Notifications changes propagate to open browser tabs
  without a manual refresh (per `SYSTEM_OVERVIEW.md` root doc and the
  `Program.cs` SignalR wiring). **Not independently verified** in this pass —
  neither `LiveVersionService.cs` nor `LiveChangeBroadcaster.cs` was opened,
  so which specific entities feed the "fingerprint" (all of Tasks? only
  counts? Records too?) is **unclear** and listed in Limitations.

## 9. Record lifecycle, traced end to end (concrete example)

1. Liza (Admin, CPD) logs a record → `RecordsController.Save` →
   `CreatedByUserId = Liza`, `OwningDepartment = CPD`, `RecordStatus =
   Retention`, `RetentionDueDate` computed from `RetentionPeriodParser`.
2. Six months pass. `RecordRetentionReminderService` (hourly) finds it inside
   the reminder window → `RecordRetentionReminder.SendDueRemindersAsync`
   (not opened in full, but its public contract and dedup via
   `RecordNotificationExistsAsync` were confirmed through
   `NotificationService`) → `NotifyRecordDisposalDueAsync(Liza, ...)`.
3. Liza manually flags it `MarkForDisposal` → `RecordStatus = Disposal` →
   `NotifyRecordMarkedForDisposalAsync(Liza, ...)`.
4. Liza physically disposes of the paper record, then confirms
   `MarkDisposed` → `RecordStatus = Disposed` → `NotifyRecordDisposedAsync`.
   This is final — no code path transitions *out of* `Disposed`.
5. Alternatively, at step 2 the OPD could instead decide it's a permanent-
   value record and call `MarkArchived` → `RecordStatus = Archived` (no
   notification fires — archiving isn't a disposal event). Liza or a manager
   can later `RestoreToRetention` if that was a mistake — the **only**
   disposal-status transition with a way back, and only from `Archived`
   specifically (enforced via `requireCurrentStatus` + a 409 on conflict).
6. At any point, the OPD (SuperAdmin) can `Approve` the record — purely a
   sign-off field, with no effect on any of the above transitions, cleared
   automatically if Liza subsequently edits the record's content.
7. `ReportsController.Data` and `RecordsController.GetAll`/`Overview` both
   reflect whichever `RecordStatus` the record currently holds, each
   independently re-querying the same `Records` table through its own raw
   SQL rather than a shared repository.

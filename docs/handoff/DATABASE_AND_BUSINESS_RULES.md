# DATABASE_AND_BUSINESS_RULES.md

Schema facts below come from `Data/AppDbContext.cs` (EF-mapped entities) plus
the `Migrations/*.cs` file names (confirming history, not individually opened
except `AppDbContext.cs` itself) and direct SQL text inside
`RecordsController`/`UserManagementController`/`ReportsController` (for the
`Records`/`Users` columns accessed via raw SQL, which are **not** EF-mapped
entities and therefore have no `DbSet`). No credentials, keys, or real
personal data are included below.

---

## 1. EF-Mapped Entities (`Data/AppDbContext.cs`)

| DbSet | Model file | Notes |
|---|---|---|
| `TaskItems` | `Models/TaskItem.cs` | Self-referencing (`ParentTaskId` → `ParentTask`/`Subtasks`) |
| `UserItems` | `Models/UserItem.cs` | View-model-shaped read entity for User Management listing — **not** the login/credentials table |
| `Users` | `Models/User.cs` | The actual credentials/session table |
| `TaskAssignments` | `Models/TaskAssignment.cs` | Join entity: one row per (Task, User) assignment |
| `TaskSubmissions` | `Models/TaskSubmission.cs` | Append-only proof-submission history |
| `TaskActivities` | `Models/TaskActivity.cs` | Append-only activity log |
| `TaskComments` | `Models/TaskComment.cs` | Threaded comments |
| `Notifications` | `Models/Notification.cs` | In-app notification feed |
| `OneTimeCodes` | `Models/OneTimeCode.cs` | OTP codes (email confirmation, login 2FA, password reset link) |
| `EmailOutbox` | `Models/EmailOutboxMessage.cs` | Queue table `EmailOutbox` drained by `EmailDispatchService` |
| `TimeLogs` | `Models/TimeLog.cs` | One row per sign-in session |

**Not EF-mapped** (accessed only via raw `SqlCommand`, confirmed in
`RecordsController`): the `Records` table, and `dbo.RecordMasterlists`
(referenced in `RecordsController.Overview`'s masterlist-count query). These
have no corresponding `Models/*.cs` class read in this pass, and no `DbSet`
in `AppDbContext`.

## 2. Key Fields and Relationships (as mapped/configured)

### `TaskItem`
- `Id` (PK), `TaskName`, `DueDate`, `Priority` (`high`/`medium`/`low`
  strings, enforced by `TaskWorkflow`/UI, not a DB check constraint found),
  `Description`, `Status` (string; see §4), `Progress` (int 0–100).
- `AssigneeId` → `User` — **legacy/primary-assignee pointer only**; the real
  multi-assignee model is `TaskAssignments`. `OnDelete: Restrict` (changed
  from a prior `Cascade`, per inline comment, since `AssigneeId` is now
  nullable).
- `ParentTaskId` → `TaskItem` (self-reference). `OnDelete: Restrict`
  (SQL Server rejects cascade on a self-reference outright).
- `TaskLevel` (`main`/`subtask`), `TaskType` (`direct-admin`/
  `department-directive`/`whole-office`, null for ordinary subtasks),
  `OwningDepartment` (null for ordinary subtasks), `ResponsibleAdminUserId`
  → `User`, `OnDelete: Restrict`.
- `CreatedByUserId` → `User`, `OnDelete: Restrict`.
- `Recurrence` (nullable string, `MaxLength(20)`) — lives only on the
  *latest* copy of a repeating task.
- Collections: `Assignments`, `Submissions`, `Activities`, `Comments`.

### `User`
- `Id` (PK), `Username`, `PasswordHash`, `FullName`, `Email`, `Role`
  (plain string: `SuperAdmin`/`Admin`/`Employee`), `IsActive`, `Department`.
- Account security: `EmailConfirmed` (default `false`), `MustChangePassword`
  (default `false`), `FailedLoginCount` (default `0`), `LockoutEndUtc`,
  `SecurityStamp` (default `""`, `MaxLength(64)`).
- Sign-in status: `LastLoginAtUtc`, `LastLogoutAtUtc`, `LastSeenAtUtc`.
- "Remember me": `RememberTokenHash` (`MaxLength(64)`, a **hash**, not the
  raw token — the raw token lives only in the browser cookie, same pattern
  as `OneTimeCode.CodeHash` per its own comment), `RememberTokenExpiresUtc`.
- **No** unique constraint on `Email`/`Username` at the DB level — confirmed
  by `UserManagementController.Create`'s comment ("The Users table has no
  unique rule (older data already has duplicates)") and its own
  application-level duplicate check run before every insert.

### `TaskAssignment` (join entity — the real multi-assignee model)
- `TaskId` → `TaskItem` (default cascade — the only cascade path to this
  table, so no SQL Server multi-cascade conflict).
- `UserId` → `User`, `OnDelete: Restrict`.
- `AssignedByUserId` → `User`, `OnDelete: Restrict`.
- `ReassignedFromUserId` → `User`, `OnDelete: Restrict` — set when
  `Reassign()` hands this same row to a new owner; preserves the chain.
- `Progress`, `Status` (own copy of the task-workflow status string, per
  assignee), `IsPrimaryAssignee` (bool — exactly one per task, enforced in
  code, not a DB constraint).
- **Unique index** on `(TaskId, UserId)` — a person cannot hold two
  assignment rows on the same task.

### `TaskSubmission`
- `TaskId` → `TaskItem` (cascade, confirmed as the "existing cascade from
  Task" referenced by multiple other entities' comments).
- `TaskAssignmentId` → `TaskAssignment`, `OnDelete: Restrict` — a submission
  belongs to one specific assignee's assignment row, not the task generally.
- `ProofFileName` (original name, display only), `ProofStoredFileName`
  (GUID-based, the actual file on disk), `Remarks`, `SubmittedAt`.
- Review outcome fields: `Decision` (`"approved"`/`"returned"`, nullable
  until decided), `AdminRemarks`, `DecidedAt`, `ValidatedByUserId` → `User`.

### `TaskActivity`
- `TaskId` → `TaskItem` (cascade). `PerformedByUserId` → `User`,
  `OnDelete: Restrict` (second cascade path to Users would otherwise
  conflict with `TaskItem.AssigneeId`'s). `RelatedSubmissionId` →
  `TaskSubmission`, `OnDelete: Restrict` (same multi-cascade-path reasoning).
- `ActivityType` (string constants, see `Services/TaskActivityLogger.cs` /
  `TaskActivityType` — values confirmed in use: `Assigned`, `Reassigned`,
  `Edited`, `ProofSubmitted`, `Validated`, `Commented`, plus others
  referenced in `ReportsController.ActivityLabel`: `Created`,
  `ProgressUpdated`, `StatusChanged`, `Removed`). `Details` (free text),
  `OccurredAt`.
- **Append-only by design** — explicitly stated in a `TasksController`
  comment ("Old rows are never touched — TaskActivity is append-only").

### `TaskComment`
- `TaskId` → `TaskItem` (cascade, implied). `AuthorUserId` → `User`,
  `OnDelete: Restrict` (same multi-cascade reasoning as `TaskActivity`).
  `Text`, `CreatedAt`.

### `Notification`
- `RecipientUserId` → `User`, `OnDelete: Restrict`.
- `RelatedTaskId` → `TaskItem`, `OnDelete: SetNull` — a notification
  **survives** its task being deleted; it just loses the link (confirmed:
  `TaskAssignmentService.PermanentlyDeleteTasksAsync` explicitly
  load-then-removes related notifications itself instead, rather than
  relying on `SetNull`, specifically because a dangling-link notification
  pointing at a now-nonexistent task would be confusing — see its own
  comment).
- `RelatedRecordId` (plain int, **no FK constraint** — `Records` isn't an
  EF entity, so this can't be a real foreign key).
- `Type` (enum `NotificationType` — values seen in use: `Task`, `DueSoon`,
  `Overdue`, `Completed`, `Record`, `RecordDisposalDue`).
- `IsRead`, `IsDismissed`, `Link` (relative URL), `Message` (truncated to
  500 chars on write, confirmed in `NotificationService.CreateAsync`).
- Index on `(RecipientUserId, IsRead)`.

### `OneTimeCode`
- `UserId` → `User`, cascade (deleting a user deletes their codes).
- `Purpose` (enum/string — `ConfirmEmail`, `Login`, `PasswordReset`, per
  `OneTimeCodePurpose` usages in `AccountController`).
- Index on `(UserId, Purpose, CreatedAtUtc)`.
- Codes are **hashed**, never stored plaintext — confirmed by the
  `OneTimeCodeHasher` singleton and the comment pattern reused for
  `User.RememberTokenHash`. The hashing *key* itself lives only in memory
  and is regenerated on every app start, which is why `Program.cs`
  deliberately invalidates every outstanding code on startup.

### `EmailOutboxMessage` (table `EmailOutbox`)
- `ToEmail`, `ToName`, `Subject`, `TextBody`, `HtmlBody`, `SentAtUtc`
  (nullable — null = not yet sent), `NextAttemptAtUtc`.
- Index on `(SentAtUtc, NextAttemptAtUtc)` — clearly designed for
  `EmailDispatchService` to efficiently find "unsent, due now" rows; the
  retry logic itself was **not opened** this pass (file not read) —
  **runtime unverified** for backoff/retry specifics.

### `TimeLog`
- `UserId` → `User`, `OnDelete: Restrict` (explicitly chosen over the EF
  default `Cascade` — "a historical fact, not something that should vanish
  with the account").
- `TimeInUtc`, `TimeOutUtc` (nullable — null = session still open).
- Index on `(UserId, TimeInUtc)`.

## 3. The `Records` Table (raw SQL only — schema inferred, not EF-mapped)

Columns referenced directly in `RecordsController`'s SQL text:
`RecordId` (PK, identity), `Code` (unique — confirmed via SQL error codes
2627/2601 caught specifically as a duplicate-Code conflict), `Title`,
`Medium`, `Location`, `PeriodCovered`, `FilingSystem`, `AccessControl`,
`RetentionPeriod`, `CreatedAt`, and — **optional, schema-probed at runtime**
— `CreatedByUserId`, `OwningDepartment`, `RecordStatus`, `RecordDate`,
`RetentionDueDate`, `MasterlistId`, `ApprovedByUserId`, `ApprovedAt`,
`UpdatedAt` (referenced in `ReportsController`'s SQL but not in
`RecordsController`'s `HasSystemColumns` probe list — see
Limitations/inconsistency note below).

**Why "optional, schema-probed":** `RecordsController.HasSystemColumns`/
`HasMasterlists`/`HasApprovalColumns` query `sys.columns` at runtime (cached
5 minutes) specifically so the app degrades gracefully — falling back to a
`LegacyRecordsSource` subquery that fabricates the missing columns as SQL
`NULL` — if migrations `20260925000000_AddRecordSystemFields`,
`20260926000000_AddRecordMasterlists`, or `20261019000000_AddRecordApproval`
haven't actually been applied to a given database yet. This is a deliberate
defensive pattern, not an accident, matching the same philosophy as
`Data/SchemaRepair.cs` at the EF level.

`RecordStatus` values confirmed in code: `Retention`, `Disposal`, `Disposed`,
`Archived` (migration `20261020000000_RenameRecordStatusValues.cs` renamed
these from an earlier scheme — exact old names not inspected).

**Known check-constraint friction (code comment, `TryInsertRecord`):**
`CK_Records_Medium`/`CK_Records_Filing`/`CK_Records_Access` on the live
database may still only allow an *older* set of dropdown values than the
current code's standard lists — a SQL error 547 is caught and surfaced as
"The database does not accept one of the selected ... values yet," implying
this is a **known, not-yet-closed** schema/code mismatch risk, not
hypothetical. See `LIMITATIONS_AND_OPEN_QUESTIONS.md`.

## 4. Task Status State Machine (`Services/TaskWorkflow.cs`)

```
pending ──────► in-progress ──────► for-review ──┬──► completed (terminal)
   │                                               │
   └──────────────────────────────────────────────┤
                                                    └──► returned-for-correction ──► for-review
```

- `TaskWorkflow.CanTransition(current, next)` is the single enforcement
  point — a no-op (same→same) is always allowed; any other edge must be in
  `AllowedTransitions`.
- `for-review` can only be exited by an admin decision
  (`TasksController.Review`), never by the employee directly — confirmed:
  `EmployeeController.ApplyProgressUpdateAsync` explicitly refuses any
  transition that would land on `Completed`.
- This state machine is applied per-`TaskAssignment`, then aggregated to the
  task level by `TaskAssignmentService.RecalculateOverallStatus` (see
  `MODULE_DEPENDENCIES.md` §4), **not** the other way around.
- "Overdue" is **never** a stored status value — it's computed at read time
  everywhere (`TaskWorkflow.IsOverdue`/`DisplayStatus`), confirmed as a
  deliberate design choice stated in the file's own header comment.

## 5. Where Validation/Authorization Actually Lives

| Rule | Enforced where | Frontend-only or backend-enforced? |
|---|---|---|
| Password strength/reuse/default-password ban | `AccountController.NewPasswordProblem` | Backend (re-checked server-side even though a view may also validate client-side) |
| Email format, ASCII-only, no whitespace | `AccountController`/`UserManagementController` (`IsAsciiPrintableNoWhitespace`, duplicated verbatim in both files) | Backend |
| Role/permission gates | `Filters/RequireLoginAttribute.cs`, `Filters/RequirePermissionAttribute.cs`, plus many controller-local private methods (`IsOfficeWideTaskManager`, `CanAccessTaskManagement`, etc.) | Backend |
| Department scoping for tasks/records/reports | Re-implemented per-controller (see `MODULE_DEPENDENCIES.md` §1 risk note) | Backend — confirmed re-checked server-side on every POST, never trusted from a hidden form field (explicit comments to this effect throughout `TasksController.Edit`) |
| Assignee/Responsible-Admin eligibility (active, correct role, correct department) | `TasksController.ValidateAssigneeIdsAsync`/`ValidateResponsibleAdminAsync` | Backend (client-side JS narrows visible checkboxes per a code comment, but is explicitly stated as **not** the authorization boundary) |
| Record field length/control-character checks | `RecordsController.CheckText`/`ValidateEntry` | Backend |
| Record ownership (edit/delete/dispose) | `RecordsController.CanEditRow`/`CanDisposeRow` plus a fresh `LoadRecordState` read (never trusts client-sent ownership) | Backend |
| CSRF | `[ValidateAntiForgeryToken]` on every state-changing POST read in this pass | Backend (browser-cooperative, standard ASP.NET Core antiforgery) |
| Rate limiting on auth endpoints | `[EnableRateLimiting(AuthRateLimit.Policy)]` | Backend |

## 6. Transactions, Cascades, Soft Deletion, Audit Logging

- **No soft deletion found anywhere.** `RecordsController.Delete`,
  `EmployeeController.Delete`/`DeleteAllCompleted`, and
  `TasksController.Delete` all perform genuine SQL `DELETE`s (raw SQL for
  Records; EF `Remove`/`RemoveRange` for Tasks). "Deactivation" (`IsActive`)
  for `User` is the only soft-state pattern found — users are never hard-
  deleted anywhere in the code read in this pass.
- **Explicit transaction use:** only
  `TaskAssignmentService.PermanentlyDeleteTasksAsync` wraps its work in
  `_context.Database.BeginTransactionAsync()` — deleting a task tree's
  notifications, activities, comments, submissions, and assignments, then
  the task rows themselves, then (outside the transaction, deliberately,
  after commit) the on-disk proof files. Every other multi-step write
  observed (e.g. `TasksController.Edit`'s assignment sync + activity log +
  notifications) relies on a single `SaveChangesAsync` call's atomicity
  rather than an explicit transaction — fine for a single `DbContext`
  `SaveChanges`, but notification writes happen in a *separate*
  `SaveChangesAsync` inside `NotificationService` (see
  `MODULE_DEPENDENCIES.md` §2's non-atomicity note).
- **Audit logging:** `TaskActivity` is the only dedicated audit trail found,
  append-only by design, scoped to tasks. There is no equivalent table found
  for Records changes, user-account changes (beyond plain `ILogger`
  structured log lines in `UserManagementController`, which go to the
  application's log sink, not a queryable database table), or login
  attempts beyond the plain counters (`FailedLoginCount`) on `User` itself.
  `PROJECT-STATUS.md` references an unmerged `sign-in-activity` branch adding
  a `SignInEvents` table for this — **not present on the inspected branch**
  (confirmed: no `SignInEvents` migration or model file exists in
  `Migrations/` or `Models/` as read in this pass).

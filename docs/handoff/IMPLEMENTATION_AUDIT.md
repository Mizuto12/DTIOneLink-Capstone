# IMPLEMENTATION_AUDIT.md

A second, more rigorous pass focused specifically on the 11 confirmed
decisions for the Task Management and Records Management revisions. This
supplements (does not replace) the earlier `docs/handoff/*.md` discovery
package — several items below correct or sharpen claims made there, now
backed by files that pass did not open. No code was modified, no migration
was created, and no database was touched to produce this document.

---

## 1. Architecture map

DTI OneLink is an ASP.NET Core MVC app (C#, .NET 10 per
`DTIOneLink.Tests/obj/Debug/net10.0`), EF Core against SQL Server, Razor
views, vanilla JS (no SPA framework), SignalR for live updates.

**Authentication/session model — important and non-obvious:** this app does
**not** use ASP.NET Core's `AddAuthentication()`/`AddCookie()` identity
middleware. There is no `[Authorize]` anywhere. Instead:
- `builder.Services.AddSession(...)` (`Program.cs:96`) backed by
  `AddDistributedMemoryCache()` (in-memory, **not** persisted — a comment at
  `Program.cs:84-94` explains a SQL-backed session store was tried and
  reverted because the host couldn't serve the required SqlClient DLL
  version).
- `AccountSessionService.EstablishSession` (`AccountSessionService.cs:38`)
  writes `UserId`, `UserRole`, `UserDepartment`, etc. directly into
  `HttpContext.Session` after password+OTP verification.
  `Filters/RequireLoginAttribute.cs` / `RequirePermissionAttribute.cs` check
  those session keys, not a `ClaimsPrincipal`.
- A `Program.cs:224-260` middleware re-reads the DB on every request to keep
  session role/department fresh and to force sign-out when `SecurityStamp`
  no longer matches (password/email changed) or the account is deactivated.
- "Remember this device" (`AccountSessionService.cs`) is a separate,
  purpose-built cookie (`TrustedDevice`) that only skips the login-OTP step;
  it never bypasses the password and never touches `Session` itself.

This matters directly for Decision 2 — see §3.

**Modules** (controllers): Account, Employee, Tasks, Admin/Dashboard,
Records, Reports, Notifications, UserManagement, TimeLogs, Live.
**Core domain:** `TaskItem` (+ `TaskAssignment` per-assignee rows,
`TaskSubmission` proof history, `TaskActivity`/`TaskComment`), `User`,
`Records` (ADO.NET/raw SQL table, **not** an EF entity — see `RecordsController`
using `SqlConnection`/`SqlCommand` throughout, unlike every other module
which uses `AppDbContext`), `RecordMasterlists`.
**Background services:** `RecurringTaskService`, `TaskReminderService`,
`RecordRetentionReminderService`, `LiveChangeBroadcaster`.
**Real-time:** `LiveHub` (SignalR) + `LiveVersionService` (DB fingerprint
polling) + `LiveChangeBroadcaster` (1-minute broadcast loop).

---

## 2. File-by-file audit (files opened this pass, with what they establish)

| File | What it establishes |
|---|---|
| `Models/User.cs` | No activation-token fields exist. Has `Username`, `Email`, `EmailConfirmed`, `MustChangePassword`, `SecurityStamp`, `RememberTokenHash/ExpiresUtc`. No `IsActivated`/`ActivationTokenHash` equivalent. |
| `Models/TaskItem.cs` (re-confirmed) | `DueDate` is `[DataType(DataType.Date)]` on a `DateTime` column. No `RequiresReview`, no `ReviewDueDate`. `Progress`/`Status` are documented as computed-only ("Do not write to these directly for a task that has assignments", `TaskItem.cs:80-83`). |
| `Models/TaskAssignment.cs` | One row per (Task, User). Has `Progress`, `Status` **per assignee already**. This is the correct FK target for a per-assignee checklist (Decision 7) — `TaskAssignmentId`, not `TaskItemId`. |
| `Models/TaskSubmission.cs` | `SubmittedAt` **already exists** (`TaskSubmission.cs:36`, defaults to `DateTime.UtcNow`), as does `DecidedAt` (nullable, set when an Admin approves/returns). Decision 5 can compute a review deadline from `SubmittedAt` without a new column for the start time; only `ReviewDueDate` itself needs to be added (on `TaskItem` or `TaskSubmission` — see §4). |
| `Services/AccountSessionService.cs` | Confirms session-based auth (not cookie-claims). `EstablishSession` unconditionally calls `Session.Clear()` then sets new keys — **no check today for "is a different user already signed in on this browser."** |
| `Program.cs:83-109, 216-260` | Confirms in-memory (non-persisted) session store, 30-minute idle timeout, and the per-request re-validation middleware. No `AddAuthentication`/`AddCookie` call exists anywhere in the file. |
| `Controllers/AccountController.cs:93-170` (Login) | Confirms login matches `model.Username` against `User.Username` (`AccountController.cs:100-101`), not `Email`. Confirms the literal default-password check at line 139. Confirms lockout/anti-enumeration logic (`DummyHash`) is intact and unaffected by either decision. |
| `Services/TaskWorkflow.cs` | `IsOverdue` (`:49-50`) and `DelayRisk` (`:69-95`) both compare `dueDate.Date` only — confirmed date-only logic baked into the one place "overdue" is computed app-wide. No separate review-stage concept exists; `ForReview` is explicitly excluded from `DelayRisk` (`:72`, "waiting for review" returns null) — i.e. a task sitting in review today shows **no** delay indicator at all, for either the employee or an admin. This is worse than "counts against the employee" — it's currently invisible to everyone. |
| `Services/PrioritySuggestionService.cs` (opened in full) | `Suggest(DateTime dueDate, ...)` takes a single `DateTime`, compares `.Date` only (`:32`). Confirmed rule-based (days-remaining tiers at 0-2/3-7/>7, raised one tier for Whole-Office/Department-Directive tasks). Never writes `TaskItem.Priority` directly — comment at `:5-7` confirms Admin/Supervisor always confirms/overrides. Time-of-day would need `.TotalDays` instead of `.Date` subtraction for a true date+time version. |
| `Services/TaskAssignmentService.cs` (re-confirmed) | `RecalculateOverallStatus`/`CombinedProgress` (per-assignee rollup) and `RecalculateMainTaskFromSubtasks`/`PropagateToParentMainTaskAsync` (OPD main-task/child-subtask rollup) are two **separate, independent** rollup paths today. A new per-assignee-checklist rollup (Decision 7) must be a **third** method that writes into the same `TaskAssignment.Progress` the first path already reads — it must not touch `TaskItem.Progress` directly or it will fight with `RecalculateOverallStatus`. |
| `Controllers/EmployeeController.cs:287-457, 541-560` | `SubmitProof` stores the file under `App_Data/ProofUploads` with a GUID name (`ProofStoredFileName`); `DownloadProof` (`:440`) looks it up only via `GetAccessibleSubmissionAsync` (role/department-scoped, never a client-supplied path) then serves it as `application/octet-stream` (forced download, no inline preview). **No authorization gap found** — the file path is never built from client input, so there is no path-traversal risk; the only gap is the missing preview mode itself. |
| `Controllers/RecordsController.cs:1160-1244` (`SetDisposalStatus`) | Re-confirmed: single `UPDATE ... SET RecordStatus = @Status`, no timestamp per transition, `RecordStatuses` array (`:525`) is `{"Retention", "Disposal", "Disposed", "Archived"}` — the new authority states do not exist. |
| `Controllers/RecordsController.cs:1480-1650` (Masterlist save/reopen) | `RecordMasterlists.FileContent`/`FileName` hold the **auto-generated Excel file for the whole masterlist** (built by `RecordMasterlistExcel.Build`), not a per-entry attachment. There is no column or table for attaching an arbitrary PDF/supporting file to one masterlist entry — confirmed missing, not just unread. |
| `Controllers/ReportsController.cs` + `wwwroot/js/reports.js:332-349` | **Resolves the earlier contradiction.** CSV export **is implemented**, entirely client-side: `reports.js` builds CSV rows from the already-rendered table and triggers a `Blob` download (`reports.js:332-349`). `ReportsController` itself has no export action — correct, because none is needed. The earlier pass's "Unclear/Partially implemented" verdict is corrected to **Verified implemented (client-side)**. `PROJECT-STATUS.md`'s "Export Data on Reports does nothing" note is stale. |
| `BackgroundService/RecurringTaskService.cs` (opened in full) | Runs at startup then hourly (`CheckInterval = 1 hour`, `:16`). Finds Main-level tasks with `Recurrence != null` and `DueDate <= today`, re-creates the next copy via `OpdTaskService.CreateAsync` inside a per-task transaction, logs and skips (rather than crashing) a task with incomplete/invalid data. **Verified implemented**, confirms the earlier pass's integration-point-only verdict. |
| `Services/LiveVersionService.cs` (opened in full) | **Corrects the earlier pass.** The data fingerprint SQL (`:14-23`) covers `TaskItems`, `TaskAssignments`, `TaskSubmissions`, `TaskActivities` (count only), `TaskComments`, **`Records`**, **`RecordMasterlists`**, and a partial `Users` projection. Records changes **are** tracked for live updates, not Tasks-only as the earlier pass assumed (it had not opened this file). |
| `BackgroundService/LiveChangeBroadcaster.cs` (opened in full) | Polls `LiveVersionService` once a minute only while `LiveHub.AnyoneConnected`, broadcasts `dataChanged` to everyone when the fingerprint changes, and `notificationsChanged` only to the specific user whose notification fingerprint changed. Deliberately coalesced to once a minute, not per-save. **Verified implemented.** |
| `Migrations/20260922000000_RelaxRecordsChoiceConstraints.cs` | Confirms and dates the CHECK-constraint history. The three constraints (`CK_Records_Medium`/`Filing`/`Access`) **were** hard-coded to the original dropdown values (`Physical`/`Digital`, etc.) and **were already relaxed** by this migration (dated 2026-09-22) to just "non-blank" — they now only reject empty strings, not reject new choices. The earlier pass's comment about "still only allow the old dropdown choices on some deployed database" describes a database that has **not yet had this migration applied** (or whose migration history is out of sync — see `Program.cs`'s `SchemaRepair` fallback, which does not currently mirror this particular migration — confirmed by reading all of `Data/SchemaRepair.cs`, which has no `CK_Records_*` entry at all). **This is itself a latent risk**: if a hosted database's migration history ever gets out of sync badly enough to trigger the `SchemaRepair` fallback path, this specific constraint relaxation would not be reapplied, since `SchemaRepair.cs` only mirrors `AddTaskAssignmentReassignedFrom`, `AddAccountSecurityAndEmail`, `AddUserLastLogin`, `AddTimeLogs`, `AddRecordMasterlists`, and `AddUserRememberMeToken` — not this one. |
| `Data/SchemaRepair.cs` (opened in full) | Confirmed scope as above — six migrations mirrored, not all of them. Every new schema change proposed below must decide whether it also needs a `SchemaRepair` entry (see §6). |
| `Migrations/*.cs` (56 files listed, chronologically from `20260819084656_InitialCreate` to `20261020142124_AddUserRememberMeToken`) | No migration exists yet for any of the 11 decisions — confirms all are genuinely new work, not already-started features. Notable: `20261009120000_AddSessionCache` was added then reverted by `20261010000000_DropSessionCache` — this is the SQL-backed session store attempt mentioned in the `Program.cs:84-94` comment, now confirmed as an actual migration pair, not just a comment's claim. |
| `Security/Permissions.cs`, `Security/RolePermissions.cs`, `Controllers/UserManagementController.cs` (role-creation list) | Grep confirms `"Supervisor"` is referenced as a **department-scoped-admin-equivalent** role-string check in 6+ files (`DashboardController`, `EmployeeController`, `TasksController`, `NotificationService`, `TaskReminderService`), but `UserManagementController`'s account-creation role list never offers it. **This is confirmed intentional defensive code, not a bug** — `DTIOneLink.Tests/RolePermissionsTests.cs:49` has the explicit comment `// never an actual stored Role value (see UserManagementController.Roles)`, and a dedicated test (`ReportsControllerScopingTests.cs:205-209`, `IsAllowedRole_Supervisor_IsNotAllowed`) asserts this on purpose. No action needed for any of the 11 decisions; noted only because it's directly relevant if any new feature below adds its own role check and might copy the "Admin"-only pattern without remembering "Supervisor" exists as a parallel check elsewhere. |
| `git ls-files` (presence check only, no content read) | Confirms `publish/appsettings.json` **is still tracked** in the git working tree. Per the pasted instructions, its contents were not read and no values are reproduced here — see §9. |

---

## 3. Status of each of the 11 confirmed decisions

### 1. Account activation (link, not shared/temp password)
**Status: Missing.** No activation-token model, column, or controller action
exists. `User.cs` has no token field; `UserManagementController.Create`
(line 174, confirmed in the earlier pass and re-checked here) still hashes
`AccountDefaults.DefaultPassword` for every new account. Nothing to build
on except the existing `OneTimeCodeService`/`OneTimeCodes` table pattern,
which is architecturally the closest precedent (hashed token, `ExpiresAtUtc`,
`FailedAttempts`, `ConsumedAtUtc`) and could plausibly be reused or extended
rather than building an entirely separate activation-token table.

### 2. Browser sessions (log out before another account can sign in)
**Status: Missing**, and the architecture note in §1 matters here:
"browser session" in this app literally means the ASP.NET `Session` object
tied to the session cookie, backed by an **in-memory, non-persisted**
cache (`Program.cs:84-95`). `EstablishSession` (`AccountSessionService.cs:38`)
calls `Session.Clear()` unconditionally — there's no check today for an
existing different `UserId`. A fix is straightforward (check
`Session.GetInt32("UserId")` before clearing, in `AccountController.Login`
right before the call that signs the new user in) but has one real
consequence worth confirming: because the session store is in-memory and not
persisted, this only enforces "one identity at a time **in this server
process's memory for this cookie**" — it does not, and architecturally
cannot cheaply, enforce "one active login per account across different
browsers/devices" (that would need a server-side index of session-by-user,
which doesn't exist and wasn't asked for — this matches the earlier
scoping decision to keep this same-browser-only).

### 3. Email login (keep Username as display field)
**Status: Missing**, but trivial to confirm precisely: `AccountController.cs:100-101`
is the only place the lookup happens (`u.Username.ToLower() == username.ToLower()`).
`Email` already exists on `User` and is already unique enough to be used for
password-reset/OTP delivery elsewhere in the same controller — no new column
needed, just a different lookup field and a view-model/view change. One real
question: is `Email` currently enforced unique at the database level? Not
confirmed in this pass (no unique index/constraint found in the migrations
reviewed) — **flagged as a question for §8**, since login-by-email silently
breaks (picks an arbitrary match) if two accounts ever share an email.

### 4. Records disposition and event history
**Status: Missing** (confirmed, not just unread, this pass).
`SetDisposalStatus` (`RecordsController.cs:1160`) only ever writes
`RecordStatus`; no `RecordDispositionEvents`-equivalent table or any
per-transition timestamp exists anywhere in `Records`'s schema (checked
every `Records`-related migration). The proposed
`Retention review → Disposal Requested → Awaiting NAP Authority →
Authorized for Disposal → Disposed` pipeline and `Archived` status (kept
separate, never equated with `Disposed`, consistent with the existing
`RestoreToRetention`-only-from-`Archived` rule already in code) would be
entirely new.

### 5. Task review deadline (from submission time, not task deadline)
**Status: Missing**, but with a head start: `TaskSubmission.SubmittedAt`
**already exists** (`TaskSubmission.cs:36`). No `ReviewDueDate` field exists
anywhere. Critically, `TaskWorkflow.DelayRisk` (`:69-95`) explicitly treats
`ForReview` as a state with **no** delay indicator at all (returns `null`
at line 72) — so today, neither the employee nor the reviewer sees any
at-risk/overdue signal while a submission is sitting in review, regardless
of how long it sits there. This is a stronger finding than "the task
deadline unfairly penalizes the employee" — it's that **the review stage is
currently invisible to the overdue/at-risk system entirely**.

### 6. Date-and-time deadlines
**Status: Missing.** `TaskItem.DueDate` (`TaskItem.cs:21-24`) is
`[DataType(DataType.Date)]` on a `DateTime` column — the column itself can
already store a time component; only the attribute/view/validation force it
to midnight. `TaskWorkflow.IsOverdue`/`DelayRisk` and
`PrioritySuggestionService.Suggest` all compare `.Date` (confirmed by
reading both files in full this pass), so a time-aware fix is not just a UI
change — all three call sites need to switch from `.Date` comparison to a
full `DateTime` comparison (or an explicit "has the exact moment passed"
check) to actually change behavior.

### 7. Per-assignee checklist
**Status: Missing**, with the correct foreign key now confirmed:
**`TaskAssignmentId`**, not `TaskItemId`. `TaskAssignment.cs` already carries
its own `Progress`/`Status` per (Task, User) pair, and
`TaskAssignmentService.CombinedProgress` (re-confirmed) already aggregates
per-assignee `Progress` up to `TaskItem.Progress`. A checklist keyed to
`TaskAssignmentId` plugs into that existing aggregation point without
needing to touch `RecalculateOverallStatus`'s logic — only a new method is
needed to compute one assignee's `TaskAssignment.Progress` from their own
checklist items, which then flows into the existing rollup unchanged. The
separate OPD main-task/subtask rollup (`RecalculateMainTaskFromSubtasks`)
is architecturally independent of this and is not at risk of being broken
by this change, provided the new checklist method only ever writes
`TaskAssignment.Progress`, never `TaskItem.Progress` directly.

### 8. Optional task review (RequiresReview)
**Status: Missing.** No such field exists on `TaskItem`. The workflow
machine (`TaskWorkflow.cs:18-25`) currently has `ForReview` as a mandatory
link in every chain from `Pending`/`InProgress` to `Completed` — there is no
transition directly from `InProgress` to `Completed` today. Adding
`RequiresReview` requires both the new column and a new allowed transition
in `AllowedTransitions`, plus the actual branch in
`EmployeeController.SubmitProof` that decides which path to take.

### 9. Proof preview
**Status: Missing** (the preview itself), but the storage/authorization
foundation it would build on is sound. `DownloadProof`
(`EmployeeController.cs:438-457`) is correctly scoped via
`GetAccessibleSubmissionAsync` and never trusts a client-supplied filesystem
path (always resolves through the stored GUID filename on the already-
authorized `TaskSubmission` row) — no security defect found here. It simply
always serves `application/octet-stream`, forcing a download for every file
type; there is no inline-preview action or content-type sniffing for
PDFs/images today.

### 10. Masterlist attachments
**Status: Missing**, confirmed by reading the full masterlist save/reopen
flow (`RecordsController.cs:1480-1650`) and `RecordMasterlistExcel.cs`'s
usage. `RecordMasterlists.FileContent`/`FileName` store the one
auto-generated `.xlsx` file for the whole masterlist (built fresh from the
Excel-writer service on every save) — there is no column, table, or action
anywhere for attaching a separate, user-supplied PDF/data file to an
individual masterlist entry.

### 11. Online file browser / digital locker
**Status: Missing** as a feature, though the permission logic it needs
already exists correctly: `RecordsController.VisibilityClause` (confirmed
present in the earlier pass, re-confirmed here by its continued use
throughout `RecordsController`'s read queries) already scopes a non-manager
to their own records only, and managers/approvers to everything. No file
list view separate from the records data table exists, and there is
currently no per-record "attached file" concept at all distinct from the
masterlist export — meaning this decision and Decision 10 overlap: if
Decision 10 adds an attachment mechanism, Decision 11's file browser would
naturally list those same attachments, not a separate thing.

---

## 4. Files and database entities likely to require changes, per decision

| # | Decision | Files to change | New DB entities |
|---|---|---|---|
| 1 | Activation link | `Models/User.cs`, new `Services/AccountActivationService.cs` (or extend `OneTimeCodeService`), `Controllers/AccountController.cs` (new `Activate` action), `Controllers/UserManagementController.cs:174` (Create), activation email template | `User.ActivationTokenHash`, `ActivationTokenExpiresUtc` (or a new `AccountActivations` table, consistent with the `OneTimeCodes` precedent) |
| 2 | Single browser session | `Controllers/AccountController.cs` (Login, before `SignIn`/`EstablishSession`), `Services/AccountSessionService.cs` | None (session-only change) |
| 3 | Email login | `Controllers/AccountController.cs:100-101`, `Models/LoginViewModel.cs`, `Views/Account/Login.cshtml` | Possibly a unique index on `Users.Email` if not already present (unconfirmed — see §3.3, §8) |
| 4 | Disposal event history | `Controllers/RecordsController.cs` (`SetDisposalStatus` and its 4 callers, `RecordStatuses` array at `:525`), Records views | New `RecordDispositionEvents` table (`Id`, `RecordId`, `EventType`, `OccurredAt`, `ActorUserId`, `AuthorityReference`, `Notes`, nullable `AttachmentId`) |
| 5 | Review deadline from submission | `Models/TaskItem.cs` or `Models/TaskSubmission.cs` (field placement — see §8), `Services/TaskWorkflow.cs` (`DelayRisk`, currently returns null for `ForReview` — needs a review-specific branch), `Controllers/TasksController.cs` (Review action), `Services/NotificationService.cs` (overdue-review notification) | `ReviewDueDate` column (placement TBD, §8) |
| 6 | Date+time deadlines | `Models/TaskItem.cs:21-24`, `Services/TaskWorkflow.cs` (`IsOverdue`, `DelayRisk`), `Services/PrioritySuggestionService.cs:32`, `Views/Tasks/Create.cshtml`/`Edit.cshtml`, any JS date picker | No new entity — `DueDate` column type is already `DateTime`/`datetime2`, only app-layer logic changes |
| 7 | Per-assignee checklist | `Models/TaskAssignment.cs` (navigation property), new `Models/TaskChecklistItem.cs`, `Services/TaskAssignmentService.cs` (new rollup method), `Controllers/TasksController.cs`/`EmployeeController.cs` (toggle/add/remove item actions), checklist views | New `TaskChecklistItems` table (`Id`, `TaskAssignmentId` FK, `Label`, `Weight`, `IsDone`, `DoneAt`) |
| 8 | RequiresReview | `Models/TaskItem.cs`, `Services/TaskWorkflow.cs:18-25` (`AllowedTransitions` — add `InProgress → Completed`), `Controllers/EmployeeController.cs` (`SubmitProof`) | `TaskItem.RequiresReview` column, default `true` |
| 9 | Proof preview | `Controllers/EmployeeController.cs` (new `PreviewProof` action alongside existing `DownloadProof`), `Views/Tasks/Review.cshtml` or `Views/Employee/*` | None |
| 10 | Masterlist attachments | `Controllers/RecordsController.cs` (masterlist save action, `:1480` area), masterlist views | Reuse the existing `FileContent`/`FileName` pattern — either a new column pair on `RecordMasterlists` for a per-entry attachment, or (cleaner) a new `RecordAttachments` table keyed to `RecordId` if the intent is "attach a file to a record," not "attach a file to a masterlist" — **this needs clarifying, see §8** |
| 11 | Digital locker / file browser | New controller action/view (could live in `RecordsController` or a new `RecordFilesController`) | None beyond whatever Decision 10 adds |

---

## 5. Dependencies and implementation order

1. **Decision 3 (email login)** — no dependencies, smallest change, unblocks nothing else but is safe to do first and independently testable.
2. **Decision 2 (single session)** — no dependencies, independent of everything else.
3. **Decision 1 (activation link)** — depends on nothing else, but touches the same `AccountController`/account-creation code path as 2 and 3, so doing all three Security/Login items in one pass avoids re-testing login twice.
4. **Decision 6 (date+time) before Decision 5 (review deadline)** — Decision 5's review-deadline math is meaningless without time-of-day precision first; do 6, then 5.
5. **Decision 8 (RequiresReview) before Decision 7 (checklist)** — the locked-in rule "checklist completion must not bypass review when RequiresReview is true" means the checklist's completion handler needs `RequiresReview` to already exist and be checkable.
6. **Decision 7 (checklist)** depends on 8, and should land before Decision 9 (preview) only because both touch `EmployeeController`/`Review.cshtml` — ordering between 7 and 9 is otherwise not a hard dependency, just a merge-conflict-avoidance preference.
7. **Decision 4 (disposal events)** is independent of all Task items and can be built in parallel by a second contributor if applicable.
8. **Decision 10 (masterlist attachment) before Decision 11 (file browser)** — 11 should be built to read whatever storage mechanism 10 introduces, not duplicate it.

Suggested overall order: **3 → 2 → 1 → 6 → 5 → 8 → 7 → 9 → 4 → 10 → 11**,
with 4 able to run in parallel to the Task-side work (6/5/8/7/9) if more
than one person is implementing.

---

## 6. Migration risks and backward-compatibility requirements

### Users table (Decisions 1, 2, 3)
- **Existing model/table:** `Users` (`Models/User.cs`).
- **Proposed change:** add activation-token columns (Decision 1); no schema
  change for Decisions 2/3 (session-only and query-only, respectively).
- **Migration required:** yes, for Decision 1 only (new nullable columns —
  additive, safe).
- **Existing-data risk:** low. New columns must be nullable or defaulted so
  existing rows (which have no activation token, having been created under
  the old flow) remain valid. **Explicit instruction from the pasted prompt:
  preserve existing users during migration** — meaning existing accounts
  should not be forced through a new activation flow retroactively; the
  simplest safe rule is "activation applies only to accounts created after
  this change ships," with existing accounts' `MustChangePassword`/password
  flow left exactly as-is.
- **Backward compatibility:** the existing first-login
  password-change/email-OTP flow (`VerifyEmail`/`SetPassword`) must keep
  working for any account created before this ships, or for any account an
  Admin resets via the existing `ResetPassword` flow (which is a different
  user story from initial activation and was not asked to change).
- **Rollback:** dropping the new nullable columns is safe since nothing
  else depends on them yet.
- **SchemaRepair:** per §2's finding, `Data/SchemaRepair.cs` does not cover
  every historical migration — a new entry should be added for these
  columns, following the existing pattern, if the team wants the hosted
  database's documented resilience to a failed `dotnet ef database update`
  to extend to this change too (recommended, since Users already has three
  prior `SchemaRepair` entries for exactly this reason).

### TaskItems table (Decisions 5, 6, 8)
- **Existing model/table:** `TaskItems`.
- **Proposed change:** add `RequiresReview` (bool, default `true`), add
  `ReviewDueDate` (nullable `DateTime`), change `DueDate`'s *application-layer*
  handling to date+time (no column type change needed — already `datetime2`).
- **Migration required:** yes, for the two new columns.
- **Existing-data risk:** low for `RequiresReview` (default `true` preserves
  current behavior for every existing row, per the pasted prompt's explicit
  "Default it to true for backward compatibility"). Low for `ReviewDueDate`
  (nullable, computed going forward only — **do not backfill a fabricated
  value for existing `ForReview` tasks**, per the pasted prompt's "Do not
  fabricate historical timestamps"; an existing in-flight review simply has
  no `ReviewDueDate` until it's recomputed, or is left null and excluded from
  the new overdue-review check until then).
- **Backward compatibility:** `TaskWorkflow.AllowedTransitions` must gain
  `InProgress → Completed` without removing any existing transition —
  additive change, existing transitions keep working. Existing tasks with
  a date-only `DueDate` (literally stored as midnight) are not silently
  shifted — **per the pasted prompt's explicit instruction** — meaning the
  UI should display a time-aware input going forward without rewriting
  historical rows' stored midnight values.
- **Rollback:** dropping `RequiresReview` would silently revert every task
  to mandatory review (matches current behavior, safe). Dropping
  `ReviewDueDate` is safe; any UI depending on it must handle null gracefully
  regardless (it already will, since it's nullable from day one).
- **Effects on other code:** `PrioritySuggestionService.Suggest`,
  `TaskWorkflow.IsOverdue`/`DelayRisk`, `Services/NotificationService.cs`'s
  overdue-notification methods, `Reports` (uses `DelayRisk`, confirmed in
  the earlier pass), and the `Admin/DashboardController`'s workload summaries
  (also uses `DelayRisk`, confirmed in the earlier pass) all read these
  values and need re-testing, not just the task views themselves.

### New TaskAssignmentId-keyed checklist table (Decision 7)
- **Existing model/table:** none — wholly new `TaskChecklistItems` table.
- **Migration required:** yes, new table + FK to `TaskAssignments`.
- **Existing-data risk:** none — no existing data to migrate; an assignment
  with zero checklist items simply keeps using its current directly-set
  `Progress` value (manual-entry path), consistent with the locked-in rule
  "manual progress is permitted while no checklist has been changed."
- **Backward compatibility:** `TaskAssignmentService.RecalculateOverallStatus`
  and `CombinedProgress` must not change their signature or behavior for
  assignments with no checklist items — the new checklist-driven
  `TaskAssignment.Progress` write path must be purely additive (a new method
  that *also* writes `Progress`, called only when a checklist item changes),
  not a rewrite of the existing rollup.
- **Rollback:** dropping the table reverts every assignment to manual-only
  progress, which is today's exact behavior — safe.

### New RecordDispositionEvents table (Decision 4)
- **Existing model/table:** `Records` (raw-SQL table, not an EF entity —
  confirmed in §2; this is a real constraint: an EF migration can still
  create the new history table, but inserts into it from `RecordsController`
  will need to go through `SqlCommand`/`SqlConnection` like everything else
  in that controller, not `DbContext`, for consistency with the existing
  code style, or the team needs to decide whether to bring `Records` into
  EF at the same time — **flagged as an open question in §8**, since it
  changes the shape of this migration).
- **Proposed change:** new table; `Records.RecordStatus` gains new valid
  values (`Disposal Requested`, `Awaiting NAP Authority`,
  `Authorized for Disposal`) alongside the existing four.
- **Migration required:** yes.
- **Existing-data risk:** none for the new table (starts empty). For
  existing records currently sitting in `Disposal` or already `Disposed`,
  **per the pasted prompt's explicit instruction not to fabricate historical
  events**, no retroactive `RecordDispositionEvents` rows should be created
  for status changes that already happened before this ships — their
  history starts now, not retroactively reconstructed.
- **Backward compatibility:** the existing `RecordStatuses` array
  (`RecordsController.cs:525`) and the CHECK-constraint-free `RecordStatus`
  column (confirmed free-text after the 2026-09-22 relaxation migration, §2)
  mean adding new string values is low-risk — no CHECK constraint needs
  touching for `RecordStatus` itself, unlike `Medium`/`FilingSystem`/`AccessControl`
  which do still have (relaxed, non-blank-only) constraints.
- **Rollback:** dropping the new table loses the audit trail but does not
  break the existing four-state flow, provided the new in-between states are
  also removed from application logic on rollback (otherwise records stuck
  in e.g. `Awaiting NAP Authority` would have no code path forward).

### RecordMasterlists / new attachment storage (Decision 10)
- See §8 — the right schema change here depends on an unresolved question
  (attach to a record vs. attach to a masterlist entry) that changes which
  table gets the new column/table.

---

## 7. Acceptance criteria and test cases

### 1. Account activation
- New account is created inactive (cannot log in) until the activation link
  is used.
- Activation link expires after the defined window; using an expired link
  shows a clear error and offers to resend.
- Using a valid link lets the user set their own password and then sign in
  normally.
- Resending an activation link invalidates the previous one (only the
  latest link works) — mirrors the existing `OneTimeCodeService` "latest
  code wins" pattern.
- If the activation email fails to send, the admin sees that failure (not a
  silent success) and can retry.
- **Test file to extend:** `DTIOneLink.Tests/OneTimeCodeServiceTests.cs` (if
  activation reuses that service) or a new `AccountActivationServiceTests.cs`
  following its pattern; `Integration/AccountFlowIntegrationTests.cs` for
  the end-to-end flow.

### 2. Browser sessions
- Logging in as User A, then attempting to log in as User B in the same
  browser without logging out first, is rejected with a clear message.
- Logging out, then logging in as User B in the same browser, succeeds
  normally.
- Logging in as the same user again (re-authenticating) in the same browser
  is still allowed (not a "different account," so should not be blocked).
- **Test file to extend:** `DTIOneLink.Tests/AccountSessionServiceTests.cs`,
  `Integration/AccountFlowIntegrationTests.cs`.

### 3. Email login
- Logging in with the registered email + correct password succeeds.
- Logging in with the old username (if `Username` is kept only as a display
  field) no longer works for authentication.
- Anti-enumeration behavior (the `DummyHash` timing-safe check at
  `AccountController.cs:105`) still applies when the email doesn't exist.
- **Test file to extend:** whatever test currently exercises `Login` — check
  `Integration/AccountFlowIntegrationTests.cs` and
  `Integration/LoginOtpEmailConfirmedTests.cs` for existing username-based
  assertions that need updating, not just new tests added.

### 4. Records disposition and event history
- Marking a record for disposal creates a `DisposalRequested` event with the
  correct actor and timestamp; the record's status moves accordingly.
- A record cannot move to `Disposed` without first passing through
  `AwaitingNapAuthority`/`AuthorizedForDisposal` (the new intermediate
  states are enforced as real gates, not just labels — this needs an actual
  guard in `SetDisposalStatus`, not just new string values in the array).
- `Archived` and `Disposed` remain mutually exclusive and are never conflated
  in code or in any view.
- `RestoreToRetention` continues to work only from `Archived`, unchanged
  from today.
- Every transition is visible in a per-record timeline view, in order.
- **Test file to extend:** no existing `RecordsController`-specific test
  file was found in the `DTIOneLink.Tests` listing (unlike Tasks, which has
  several) — this is itself worth flagging: Records disposal logic
  currently appears to have **no dedicated unit tests at all**, only
  `RetentionPeriodParserTests.cs` and `RecordRetentionReminderTests.cs`,
  which test the retention-date math, not the disposal state machine. A new
  `RecordsDispositionTests.cs` is recommended rather than an extension of an
  existing file.

### 5. Task review deadline
- Submitting proof sets a `ReviewDueDate` computed from `SubmittedAt` plus
  the agreed window (duration TBD — see §8).
- A submission still sitting in `ForReview` past its `ReviewDueDate` shows an
  at-risk/overdue indicator **to the reviewer**, distinct from the
  employee's own (already-met) deadline.
- An employee who submits on time is never shown as "overdue" for a task
  still awaiting review.
- **Test file to extend:** `DTIOneLink.Tests/TaskWorkflowTests.cs`,
  `DTIOneLink.Tests/TasksControllerReviewTests.cs`.

### 6. Date+time deadlines
- Creating/editing a task accepts a specific time, not just a date.
- A task due at a specific time today is correctly flagged overdue only
  after that time passes, not at midnight.
- Existing tasks created before this change (date-only, stored as midnight)
  display and behave exactly as before — no silent shift.
- **Test file to extend:** `DTIOneLink.Tests/TaskWorkflowTests.cs`,
  `DTIOneLink.Tests/PrioritySuggestionServiceTests.cs`.

### 7. Per-assignee checklist
- Each assignee on a multi-assignee task has their own independent
  checklist; checking an item on one assignee's checklist never changes
  another assignee's `Progress`.
- Completing all of one assignee's checklist items sets their `Progress` to
  100% (or the weighted equivalent), which then flows into the existing
  `CombinedProgress` aggregation unchanged.
- Setting a manual progress value is allowed until any checklist item is
  touched; after that, progress is checklist-derived only (per the locked
  rule) — test both directions of this switch.
- 100% checklist completion on an assignment whose task has
  `RequiresReview = true` still requires review before `Completed`.
- The OPD main-task/subtask rollup (`RecalculateMainTaskFromSubtasks`)
  produces identical results before and after this change, for a task that
  has no per-assignee checklists — a regression test, not just a new-feature
  test.
- **Test file to extend:** `DTIOneLink.Tests/TaskAssignmentServiceTests.cs`
  (add new cases; also re-run existing cases unchanged to catch regressions).

### 8. Optional task review
- A task created with `RequiresReview = false`: valid proof submission
  (meeting the existing 80% progress gate, `TaskWorkflow.MinProgressToSubmitProof`)
  completes the task directly, with no `ForReview` state ever entered.
- A task created with `RequiresReview = true` (or omitted, defaulting true)
  behaves exactly as today.
- No alternate endpoint (e.g. directly posting a status update) can bypass
  `RequiresReview = true` — the gate must be enforced wherever a task can be
  marked `Completed`, not only in `SubmitProof`.
- **Test file to extend:** `DTIOneLink.Tests/TaskWorkflowTests.cs`,
  `DTIOneLink.Tests/EmployeeControllerSubmitProofTests.cs`.

### 9. Proof preview
- A PDF or image proof file opens inline in the browser via the new preview
  action, without triggering a file download.
- An unsupported file type falls back to the existing download behavior.
- Authorization on the preview action is identical to `DownloadProof`'s
  existing `GetAccessibleSubmissionAsync` scoping — a user who could not
  download a given submission also cannot preview it.
- **Test file to extend:** new cases in whatever test file already covers
  `DownloadProof`, if one exists (not found by name in the test listing —
  likely needs a new test, following `EmployeeControllerSubmitProofTests.cs`'s
  pattern for `EmployeeController` test setup).

### 10. Masterlist attachments
- An attached file is retrievable later, scoped by the same ownership rule
  as the record/masterlist it belongs to.
- Saving a masterlist without an attachment continues to work exactly as
  today (attachment is optional, not required).
- **Test file to extend:** none found specific to masterlists in the test
  listing — new test file recommended.

### 11. Digital locker / file browser
- The file browser shows only files the current user is authorized to see,
  using the existing `VisibilityClause` scoping rule, not a new/separate
  permission check.
- The visual locker presentation does not change any underlying access
  behavior — a record invisible in the table view is also absent from the
  file browser.
- **Test file to extend:** none exists for this yet (it's a new view); any
  new test should assert the same scoping as existing `RecordsController`
  read-path tests, if any exist (none were found by name in the test
  listing for Records specifically — see the note under Decision 4's
  acceptance criteria above; this is a broader test-coverage gap, not
  specific to this feature).

---

## 8. Unresolved decisions requiring approval or DTI confirmation

1. **Activation link expiration window** — no duration was specified. The
   existing `OneTimeCode` pattern uses short windows (minutes) for login
   OTPs; an activation link is typically longer-lived (hours to a few days)
   since it's emailed once and the person may not check their inbox
   immediately. Needs a specific number before implementation.
2. **Email uniqueness** — not confirmed whether `Users.Email` has a database
   unique constraint today. If not, email-login (Decision 3) needs either a
   new unique constraint (which requires resolving any existing duplicate
   emails first) or a defined tie-breaking rule. This should be checked
   before Decision 3 is implemented, not after.
3. **Decision 2's cross-device scope** — re-confirming the earlier scoping
   answer (same-browser-only) is still correct given the architecture
   finding in §1/§3: because sessions are in-memory and not persisted, true
   cross-device/cross-restart single-session enforcement would require new
   infrastructure (a server-side user→session index) that doesn't exist and
   wasn't budgeted for. Recommend explicitly confirming same-browser-only
   remains the scope, now that the underlying constraint is concrete.
4. **Authority-reference verification (Decision 4)** — the plan adds a field
   to *record* a NAP authority reference, but does not and cannot verify
   that the reference is real or valid (no NAP API exists to check against).
   This is explicitly an honest paper-trail mechanism, not an enforcement
   mechanism — confirm this framing is acceptable for the capstone's scope,
   consistent with ChatGPT's own earlier caveat that this isn't a claim of
   regulatory compliance, only of good-faith record-keeping.
5. **Review window duration (Decision 5)** — no specific number of
   calendar/working days was given for how long a reviewer has before a
   submission counts as overdue-for-review. Needs a number (and calendar vs.
   working days) before implementation.
6. **Who may set `RequiresReview = false` (Decision 8)** — the pasted
   prompt explicitly leaves this open ("The policy governing who may disable
   review remains to be confirmed"). Candidates: only the task creator
   (OPD/SuperAdmin) at creation time; or the department Admin too; or
   automatic by `TaskType`. Needs a decision before the UI/authorization
   check can be written.
7. **Masterlist attachment target (Decision 10)** — is the attached
   PDF/data meant to belong to **one record** (so it shows up wherever that
   record appears, masterlist or not) or specifically to **one masterlist
   entry** (tied to that snapshot/export only)? This changes which table
   gets the new column — `Records` vs. `RecordMasterlists` — and was not
   fully resolved by the panelist's one-line note ("be able to add the
   pdf/data to the masterlist").
8. **`Records` table's EF status (Decision 4, incidentally surfaced)** — the
   new `RecordDispositionEvents` table is straightforward to add via EF
   migration regardless, but every *query* against it from `RecordsController`
   will need to use raw `SqlCommand`/`SqlConnection` to match the rest of
   that controller's style (it never uses `AppDbContext` for `Records`-table
   work), which is unusual compared to every other module in the app. Worth
   a one-line confirmation that this inconsistency is intentional/accepted
   rather than something to fix opportunistically while touching this code.
9. **Checklist item cap (Decision 7)** — the panelist's note said "add a
   limit to the subtask" without a number. Needs a specific maximum before
   implementation (purely a validation constant, low-stakes to pick, but
   still needs a number).
10. **"Checklist task assignable to all Admins" exact mechanics (Decision 7)**
    — confirmed scope from the earlier round is "multiple Admins can be
    assignees on one checklist task," which maps cleanly onto the existing
    multi-assignee `TaskAssignment` model. Still open: can an ordinary
    Employee also be an assignee on the same checklist-type task alongside
    Admins, or is this feature Admin-only? Not specified either way yet.

---

## 9. Security or compliance blockers

1. **`publish/appsettings.json` is still tracked in the git working tree**
   (confirmed present via `git ls-files` this pass; **contents were not
   read**, per instruction not to expose secret values). The earlier
   discovery pass flagged a similarly-named file and the project's own
   `PROJECT-STATUS.md` apparently already warned about this. If this file
   contains a real production connection string or other secret, this is a
   live, independent security issue — rotate any real credential it
   contains and remove the file from version control (and from git history,
   since removing it from the working tree alone does not remove it from
   past commits) before or alongside this implementation work, regardless
   of the 11 decisions above.
2. **Records disposal without NAP authority is the status quo today** —
   not a new blocker, but worth restating plainly: until Decision 4 ships,
   the live system allows a records manager to mark a record `Disposed`
   with no authority-tracking step at all. This was already known from the
   earlier pass; this pass only confirms no interim mitigation exists in
   code today (e.g. no warning banner, no required confirmation text
   referencing NAP authority).
3. **No dedicated automated tests for `RecordsController`'s disposal logic**
   (noted in §7, Decision 4) — this means Decision 4's event-history
   addition is going into code with zero existing regression-test coverage
   for the disposal state machine, which raises the risk of a silent
   behavior change going unnoticed. Recommend writing the new
   `RecordsDispositionTests.cs` to cover **both** the new states **and** the
   existing four-state behavior, not just the new states in isolation.
4. **No defect found in `DownloadProof`'s authorization** (Decision 9) —
   stated here explicitly as a negative finding, since the pasted prompt
   specifically asked to check for exactly this class of bug (trusting a
   client-supplied path, or exposing files through a public static path).
   Confirmed: it does neither. No blocker here.
5. **Email uniqueness unconfirmed** (see §8.2) is a latent correctness risk
   for Decision 3 specifically, not a currently-exploitable security issue,
   but should be resolved before email-based login ships.

---

## Scope note

This pass opened every file the pasted prompt specifically named, plus the
files each finding required to verify (e.g. `TaskAssignment.cs` once it
became the answer to Decision 7's FK question). It did not re-open every
file the earlier discovery pass already read in full (e.g.
`TasksController.cs`'s full body) where this pass's new findings did not
require re-verifying those sections — those earlier findings are treated as
still valid and are cited by reference to `docs/handoff/FEATURE_INVENTORY.md`
where relevant. Every Records-related view (`.cshtml`) and every JS file
other than `reports.js` remain unopened, consistent with both passes'
consistent scope (backend/data-model-focused, not UI-markup-focused) —
flagged here again in case UI-level details (e.g. exactly what the Records
table's columns are currently labeled in the browser) matter to any of the
11 decisions' final implementation.

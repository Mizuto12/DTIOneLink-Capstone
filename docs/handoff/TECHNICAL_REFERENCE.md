# TECHNICAL_REFERENCE.md

File/symbol index for the features documented in `FEATURE_INVENTORY.md`.
Only files actually opened and read in this investigation pass are cited
with confidence; files referenced only by name (from `Glob` listings or other
files' comments, never opened) are marked "(not opened)".

---

## Accounts & Security

| Feature | UI entry | Controller/action | Service/logic | DB |
|---|---|---|---|---|
| Login + lockout | `Views/Account/Login.cshtml` (not opened) | `Controllers/AccountController.cs::Login` (GET/POST) | `Security/AccountDefaults.cs` (not opened — constants only inferred from usage), `PasswordHasher<User>` | `Users.FailedLoginCount`, `Users.LockoutEndUtc` |
| Email OTP / forced password change | `Views/Account/EnterCode.cshtml`, `SetPassword.cshtml` | `AccountController.cs::VerifyEmail`, `SetPassword` | `Services/OneTimeCodeService.cs` (not opened — contract inferred from call sites: `IssueAsync`, `VerifyAsync`, `IssueLinkAsync`, `VerifyLinkAsync`) | `OneTimeCodes` |
| Login 2FA / remember device | `Views/Account/EnterCode.cshtml` | `AccountController.cs::VerifyLoginCode`, `ResendLoginCode` | `Services/AccountSessionService.cs` (not opened — contract inferred: `HasTrustedDevice`, `IssueTrustedDeviceCookieAsync`, `EstablishSession`) | `Users.RememberTokenHash/RememberTokenExpiresUtc` |
| Forgot/reset password | `Views/Account/ForgotPassword.cshtml` | `AccountController.cs::ForgotPassword`, `ResetLink`, `ResetPassword` | `OneTimeCodeService.IssueLinkAsync/VerifyLinkAsync` | `OneTimeCodes` |
| Change password (signed-in) | `Views/Account/ChangePassword.cshtml` | `AccountController.cs::ChangePassword` | — | `Users.PasswordHash/SecurityStamp` |
| Session sync / forced sign-out | — (middleware) | `Program.cs` (inline `app.Use` delegate) | — | `Users.SecurityStamp/IsActive/LastSeenAtUtc` |
| Role/permission gating | — | `Filters/RequireLoginAttribute.cs`, `Filters/RequirePermissionAttribute.cs` | `Security/Permissions.cs`, `Security/RolePermissions.cs` | — |
| Rate limiting | — | `[EnableRateLimiting(AuthRateLimit.Policy)]` attributes on `AccountController` actions | `Security/AuthRateLimit.cs` (not opened) | — |
| Tests | — | — | — | `DTIOneLink.Tests/AccountSessionServiceTests.cs`, `OneTimeCodeServiceTests.cs`, `FilterAttributeTests.cs`, `RolePermissionsTests.cs` (none opened; names only) |

## User Management

| Feature | Controller/action | Key helper |
|---|---|---|
| List + open-work badge | `Controllers/UserManagementController.cs::Index` | raw SQL `openWorkSql` |
| Create account | `::Create` | `IsAsciiPrintableNoWhitespace`, duplicate-check SQL |
| Promote/demote/move division | `::ChangeStanding` | `CountOpenWorkAsync` |
| Deactivate/reactivate | `::SetActive` | `CanSetActive` (static, testable in isolation) |
| Admin-issued password reset | `::ResetPassword` | `NewTemporaryPassword` |
| Email correction | `::EditEmail` | — |
| Tests | — | `DTIOneLink.Tests/UserManagementControllerTests.cs` (not opened; name only) |

## Tasks

| Feature | Controller/action | Service | Model |
|---|---|---|---|
| Task list / filters / counts | `Controllers/TasksController.cs::Index` → `BuildIndexModelAsync` | — | `Models/TaskIndexViewModel.cs` |
| Create (OPD) | `::Create` (GET/POST) | `Services/OpdTaskService.cs::CreateAsync` (not opened in full — contract inferred from call site: `NewOpdTask` record params) | `Models/TaskCreateViewModel.cs` |
| Stop recurring | `::StopRepeating` | `Services/TaskRecurrence.cs` (not opened) | — |
| Reassign Responsible Admin | `::ReassignResponsibleAdmin` | `Services/TaskAssignmentService.cs::CanReassign/Reassign/RecalculateOverallStatus` | — |
| Edit + multi-assign | `::Edit` (GET/POST) | `TaskAssignmentService.SyncAssignmentsAsync` | `Models/TaskEditViewModel.cs` |
| Review (approve/return) | `::Review` (GET/POST) | `TaskAssignmentService.RecalculateOverallStatus`, `PropagateToParentMainTaskAsync` | `Models/TaskSubmissionDecisionViewModel.cs` |
| Priority suggestion endpoint | `::SuggestPriority` | `Services/PrioritySuggestionService.cs` (not opened — contract only: `Suggest(dueDate, taskType)`) | — |
| Add subtask | `::AddSubtask` | `OpdTaskService.AddSubtaskAsync` (not opened) | — |
| Delete Main Task tree | `::Delete` | `TaskAssignmentService.PermanentlyDeleteTasksAsync` | — |
| Main Task rollup view | `::MainTaskDetails` | `TaskAssignmentService.RecalculateMainTaskFromSubtasks` (view-time, via `mainTask.Status`/`.Progress` already stored) | `Views/Tasks/MainTaskDetails.cshtml` (not opened) |
| Received directives (Admin) | `::ReceivedTasks` | — | `Models/TaskReceivedListItemViewModel.cs` |
| Employee task list/board | `Controllers/EmployeeController.cs::Index/TaskManagement` | `AccessibleTasksQuery` | — |
| Task detail + progress slider | `::Details`, `::Update`, `::UpdateProgress` | `ApplyProgressUpdateAsync` | `Models/TaskProgressUpdateViewModel.cs` |
| Proof submission | `::SubmitProof` (GET/POST) | `Services/ProofFileValidator.cs` (not opened — contract: `Validate(file) -> (bool, string?)`) | `Models/ProofSubmissionViewModel.cs`, `Models/TaskSubmission.cs` |
| Proof download | `::DownloadProof` | `GetAccessibleSubmissionAsync` | — |
| Comments | `::AddComment` | `Services/TaskActivityLogger.cs::Log` | `Models/TaskComment.cs` |
| Employee delete (own completed) | `::Delete`, `::DeleteAllCompleted` | `TaskAssignmentService.PermanentlyDeleteTasksAsync` | — |
| Task workflow rules (shared) | — | `Services/TaskWorkflow.cs` (fully read) | — |
| Task level/type/recurrence constants | — | `Services/TaskLevels.cs`, `Services/TaskTypes.cs`, `Services/TaskRecurrence.cs` (names/usages only — not opened directly, except as referenced via controllers) | — |
| Tests | — | — | `DTIOneLink.Tests/TaskWorkflowTests.cs`, `TaskAssignmentServiceTests.cs`, `TaskRecurrenceTests.cs`, `TaskTypesAndLevelsTests.cs`, `TasksController*Tests.cs` (6 files), `OpdTaskServiceTests.cs`, `PrioritySuggestionServiceTests.cs`, `EmployeeControllerScopingTests.cs`, `EmployeeControllerSubmitProofTests.cs` — **none opened in this pass**; names strongly suggest the exact rules documented here already have unit-test coverage, which would be the fastest way to confirm edge-case behavior without re-reading source. |

## Records

| Feature | Controller/action | Service |
|---|---|---|
| Index/list/search/filter | `Controllers/RecordsController.cs::Index/GetAll/LoadRecords` | — |
| Dashboard summary | `::Overview` | — |
| Save/Update/Delete | `::Save/Update/Delete` | `Services/RetentionPeriodParser.cs` (not opened — contract: `TryComputeDueDate(recordDate, periodCovered, retentionPeriod)`) |
| Approve (PD sign-off) | `::Approve` | — |
| Disposal lifecycle | `::MarkForDisposal/MarkDisposed/MarkArchived/RestoreToRetention` → `SetDisposalStatus` | — |
| Schema-tolerant column probing | `HasSystemColumns/HasMasterlists/HasApprovalColumns` | — |
| Retention reminders | — | `Services/RecordRetentionReminder.cs` (not opened — contract: `SendDueRemindersAsync(ownerUserId, ct)`, constant `ReminderMonths` used by `RecordsController`) |
| Masterlist Excel/PDF | — (entry points not located this pass) | `Services/RecordMasterlistExcel.cs`, `Services/RecordMasterlistPdf.cs` (neither opened) |
| Tests | — | `DTIOneLink.Tests/RecordRetentionReminderTests.cs`, `RetentionPeriodParserTests.cs`, `PeriodCoveredParserTests.cs` (not opened; names only) |

## Reports

| Feature | Controller/action |
|---|---|
| Unified feed + period filter | `Controllers/ReportsController.cs::Data`, `TryParsePeriod` |
| Records contribution | `::OwnRecordsAsync` (raw SQL) |
| Tests | `DTIOneLink.Tests/ReportsControllerScopingTests.cs` (not opened; name only) |

## Notifications

| Feature | Controller/action | Service |
|---|---|---|
| Bell list/read/dismiss | `Controllers/NotificationController.cs::List/MarkRead/MarkAllRead/Dismiss` (class name `NotificationsController`) | `Services/NotificationService.cs` (fully read) |
| Tests | — | `DTIOneLink.Tests/NotificationServiceTests.cs` (not opened; name only) |

## Dashboards

| Feature | Controller/action |
|---|---|
| Admin dashboard | `Controllers/Admin/DashboardController.cs::AdminDashboard` |
| Completion trend | `::GetCompletionTrend` |
| Super Admin dashboard | `::SuperAdminDashboard` → `Views/SuperAdmin.cshtml` (explicit path, not opened) |
| Task details (shared) | `::Details` → renders `Views/Employee/Details.cshtml` |
| Models | `Models/SuperAdminDashboardViewModel.cs`, `Models/CompletionTrendViewModel.cs` (neither opened directly; fields inferred from controller usage) |
| Tests | `DTIOneLink.Tests/AdminDashboardControllerTests.cs`, `CrossDepartmentResponsibleAdminTests.cs` (not opened; names only) |

## Time Logs / Live / Email / Background Jobs

| Feature | File |
|---|---|
| Time log history | `Controllers/TimeLogsController.cs` (fully read) |
| Live hub + version polling | `Controllers/LiveController.cs` (fully read), `Services/LiveHub.cs`, `Services/LiveVersionService.cs` (neither opened), `BackgroundService/LiveChangeBroadcaster.cs` (not opened) |
| Email sending | `Services/Email/EmailSender.cs` (not opened — contract: `IEmailSender`, `BrevoEmailSender`, `UnconfiguredEmailSender`, `BrevoOptions`), `Services/Email/EmailTemplates.cs` (not opened — contract: `Notification(...)`, `PasswordChanged(...)`), `Services/Email/EmailDispatchService.cs` (not opened) |
| Recurring tasks | `BackgroundService/RecurringTaskService.cs` (not opened) |
| Task reminders | `BackgroundService/TaskReminderService.cs` (fully read) |
| Record reminders | `BackgroundService/RecordRetentionReminderService.cs` (fully read, thin wrapper) + `Services/RecordRetentionReminder.cs` (not opened) |
| Tests | `DTIOneLink.Tests/TimeZoneHelperTests.cs` (not opened; name only) |

## Data / Schema

| Component | File |
|---|---|
| EF model/context | `Data/AppDbContext.cs` (fully read) |
| Runtime schema fallback | `Data/SchemaRepair.cs` (not opened — invoked from `Program.cs::EnsureLatestSchemaAsync`) |
| Migrations | `Migrations/*.cs` — 30+ files, names only inspected via `Glob`; none opened individually. Names used as *evidence of when a feature was introduced* throughout this doc package (e.g. `20261019000000_AddRecordApproval.cs`, `20261020000000_RenameRecordStatusValues.cs`). |
| Security/permissions | `Security/Permissions.cs`, `Security/RolePermissions.cs` (both fully read), `Security/AccountDefaults.cs` (not opened), `Security/AuthRateLimit.cs` (not opened) |
| Filters | `Filters/RequireLoginAttribute.cs`, `Filters/RequirePermissionAttribute.cs` (both fully read) |
| DatabaseHelper | `Services/DatabaseHelper.cs` (not opened — used by `UserManagementController`/`ReportsController` for raw `SqlConnection`s; contract: `GetConnection()`) |
| Display helpers | `Helpers/DisplayHelpers.cs` (not opened) |

## Front End (not substantively inspected this pass)

`Views/**/*.cshtml` (33 files, listed via `Glob`, only `SYSTEM_OVERVIEW.md`'s
root-doc description relied upon for their content), `wwwroot/css/*`,
`wwwroot/js/*` (not opened — in particular `live-refresh.js`,
`offline-sync.js`, `records.js`, `reports.js`, `user-management.js` are
referenced by name in various code comments but their contents were not
read). Any claim in this doc package about exact on-screen UI behavior
(button labels, exact Kanban board layout, chart appearance) should be
treated as **runtime unverified** unless it is sourced from
`docs/requirements-documentation.md`'s own stated second-pass verification.

## Tests Project (names catalogued, contents not opened)

`DTIOneLink.Tests/*.cs` (24 top-level test files + an `Integration/`
subfolder + `TestDoubles/`). This is a strong signal that the exact rules
documented in `FEATURE_INVENTORY.md`/`DATABASE_AND_BUSINESS_RULES.md` already
have executable, authoritative specifications — **recommended first stop**
for verifying any specific business-rule edge case rather than re-reading
controller source. Not opened in this pass due to scope/time; see
`LIMITATIONS_AND_OPEN_QUESTIONS.md`.

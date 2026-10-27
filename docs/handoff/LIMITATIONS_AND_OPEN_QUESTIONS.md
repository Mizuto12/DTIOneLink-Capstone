# LIMITATIONS_AND_OPEN_QUESTIONS.md

This is a discovery-and-documentation pass, not a full-code audit. It
prioritized breadth (every controller, every model, the DB context, security/
filter layer, background jobs, notification service) over opening every
service/view file. What follows separates **confirmed facts**, **documented-
but-not-independently-verified claims** (inherited from the two pre-existing
docs that state they were checked against code), and **genuine open
questions**.

## A. Not Opened in This Pass (scope gaps — "Runtime unverified" by default)

These exist (confirmed via `Glob`/file listing or inbound references) but
their internal logic was not read, so any claim about their exact behavior
elsewhere in this package is inferred from call sites, not from the file
itself:

- `Services/OneTimeCodeService.cs`, `AccountSessionService.cs`,
  `Security/AccountDefaults.cs`, `Security/AuthRateLimit.cs` — exact OTP
  expiry/retry windows, lockout duration, and rate-limit thresholds are
  taken from the root `SYSTEM_OVERVIEW.md`'s own stated figures (5 wrong
  passwords → 15 min lock; 6-digit codes, 10 min expiry, 5 tries, 60s resend,
  3 per 15 min) and **not re-verified against the actual constants** in this
  pass.
- `Services/ProofFileValidator.cs`, `PrioritySuggestionService.cs`,
  `RetentionPeriodParser.cs`, `RecordRetentionReminder.cs`,
  `RecordMasterlistExcel.cs`, `RecordMasterlistPdf.cs`, `OpdTaskService.cs`,
  `TaskRecurrence.cs`, `TaskLevels.cs`, `TaskTypes.cs`,
  `DatabaseHelper.cs`, `Helpers/DisplayHelpers.cs`.
- `Services/Email/EmailSender.cs`, `EmailTemplates.cs`,
  `EmailDispatchService.cs` — email retry/backoff behavior, exact template
  wording, and Brevo error handling are unverified.
- `BackgroundService/RecurringTaskService.cs`, `LiveChangeBroadcaster.cs`,
  `Services/LiveHub.cs`, `LiveVersionService.cs`.
- `Data/SchemaRepair.cs` — what exactly it adds and whether it stays
  perfectly mirrored to every migration (`PROJECT-STATUS.md` explicitly
  flags "every new migration must also be mirrored in `SchemaRepair.cs`" as
  a *process* rule, implying this has been a point of drift risk before —
  not confirmed whether it's currently in sync).
- Every `Views/**/*.cshtml` file's actual markup/JS (33 files), and every
  `wwwroot/js/*.js` file. All UI-level claims in this package (Kanban board
  layout, chart rendering, exact wording) trace back to the pre-existing
  `SYSTEM_OVERVIEW.md`/`requirements-documentation.md`, not to this pass's
  own inspection.
- All 24 files in `DTIOneLink.Tests/` and its `Integration/`/`TestDoubles/`
  subfolders. **Recommended next step** if higher-confidence verification of
  any specific rule in this package is needed — the test names strongly
  suggest they already codify the exact edge cases this package describes
  from reading source (e.g. `TaskAssignmentServiceTests.cs`,
  `CrossDepartmentResponsibleAdminTests.cs`, `RolePermissionsTests.cs`).
- `.github/workflows/*.yml` (deploy pipeline) — not opened; deployment
  claims rely entirely on `PROJECT-STATUS.md`.
- `Migrations/*.cs` migration bodies (30+ files) — only file names were
  used, as dated evidence of when a feature/column was introduced. The
  actual `Up()`/`Down()` SQL was not read for any of them.
- `docs/dfd-*.html`, `docs/design-and-development.md`,
  `docs/implementation-plan.md`, `docs/implementation-results.md`,
  `docs/scenario-readiness-plan.md`, `docs/conclusion.md`,
  `docs/defense-script.md`, `docs/future-work-oic-coverage.md`,
  `docs/testing/` — none of these were opened. They likely contain
  additional capstone-specific framing (data flow diagrams, defense talking
  points) that could usefully supplement this package but were out of
  scope for a code-focused discovery pass.
- `tools/google-docs/`, `tools/google-forms/` — out of scope (not part of
  the running application, per `PROJECT-STATUS.md`).

## B. Confirmed Inconsistencies / Stale Prior Documentation

1. **Records disposal is no longer "notify-only."** `PROJECT-STATUS.md`
   (dated 2026-10-06) states "Records only notify for disposal; no disposal
   workflow" and lists "Record archiving (item 20) ... ruled out." The
   current code (`RecordsController.MarkArchived`/`RestoreToRetention`,
   migration `20261020000000_RenameRecordStatusValues.cs`) directly
   contradicts this — `Archived` is a real, implemented status with its own
   transition rules. **This package's `FEATURE_INVENTORY.md`/
   `MODULE_DEPENDENCIES.md` reflect the current code, not the older doc.**
   Resolve by asking: was the scope ruling formally revisited, or did this
   ship ahead of/without updating that ruling? (Git log shows no commit
   message explaining the scope change — only the feature commits
   themselves.)
2. **Overdue reminders are no longer in-app-only.**
   `docs/requirements-documentation.md` §13 states "Due-date and overdue
   task reminders are delivered in-app only, not by email." Commit
   `42efeba` ("Email overdue task reminders, same as Returned and
   record-disposal notices already do") and the current
   `NotificationService.NotifyTaskOverdueAsync` (which now always passes
   `emailSubject`) contradict this specifically for **overdue** (due-soon
   reminders remain in-app only, confirmed). Treat the requirements doc's
   §13 as stale on this one point.
3. **CSV export on Reports — contradictory, unresolved.**
   `docs/requirements-documentation.md` §14 states each Reports list is
   "downloadable as a CSV file generated in the browser." `PROJECT-STATUS.md`
   (same date range) says "Export Data on Reports does nothing (no
   handler)." This pass fully read `ReportsController.cs` and found **no**
   server-side export action — consistent with `PROJECT-STATUS.md`, not with
   the requirements doc, *for the server side*. Since "generated in the
   browser" implies client-side JS (not opened this pass,
   `wwwroot/js/reports.js` if it exists), this is genuinely **unresolved**:
   it may have been implemented client-side after `PROJECT-STATUS.md` was
   written, or the requirements doc's claim may itself be aspirational/
   unverified. **Action for next investigator:** open `wwwroot/js/reports.js`
   and `Views/Reports/Index.cshtml`.
4. **"Supervisor" role.** Multiple controllers check for role string
   `"Supervisor"` (`TasksController.IsDepartmentTaskManager`,
   `EmployeeController.IsDepartmentTaskManager`) but
   `UserManagementController.Roles` only ever creates `SuperAdmin`/`Admin`/
   `Employee` accounts. This is very likely intentional defensive code for a
   role that was planned but never wired into account creation — matches
   `PROJECT-STATUS.md`'s note that the paper's "Supervisor" role maps to
   `Admin`. Flagged as **Unclear** rather than a bug: no evidence this causes
   incorrect behavior today (since no account can actually hold that role
   string), but it is dead/unreachable code worth confirming with the
   developer before removing.

## C. Genuine Open Questions (code alone cannot resolve these)

1. **Is the `backup-recovery` branch (per `PROJECT-STATUS.md`) merged or
   still separate?** This pass inspected only the `main` branch's working
   tree as checked out. `git log` shows no merge commit referencing it in
   the 5 most recent commits seen. If still unmerged, the Backup &
   Recovery feature `PROJECT-STATUS.md` describes (ZIP download/restore,
   `BackupLogs` table, `BackupReminderService`) **does not exist** in the
   code this package documents — no `BackupLogs` migration or
   `BackupReminderService` file was found via `Glob` in this pass. Treat as
   **Apparently missing from the inspected branch** until confirmed
   otherwise.
2. **Has the publish/-directory secret-leak issue (`PROJECT-STATUS.md` item
   A.1–A.2: a committed production connection string, and a public repo)
   been remediated?** `publish/` is still present and tracked in the current
   working tree (confirmed via the initial file listing in this pass — it
   contains `appsettings.Production.json`). **This was not opened or its
   contents inspected in this pass** specifically to avoid handling/exposing
   a potential live secret. If it still contains a real connection string,
   this is a live security issue requiring immediate developer action
   (rotate the DB password, scrub git history, add `publish/` to
   `.gitignore`) — **flag to the user/developer directly, separately from
   this documentation package**, rather than treating it as a documentation
   finding only.
3. **Is `SchemaRepair.cs` actually kept in sync with every migration?** Not
   verifiable from source alone without diffing every migration against the
   repair logic — `PROJECT-STATUS.md` frames this as an ongoing process
   obligation, not a one-time check.
4. **What exactly does `LiveVersionService`'s "fingerprint" cover?** Needed
   to know whether a Records change, for instance, triggers the same live-
   refresh signal a Tasks change does, or whether Live Updates are currently
   Tasks-only. Requires opening `Services/LiveVersionService.cs` and
   `BackgroundService/LiveChangeBroadcaster.cs`.
5. **Deployment/hosting specifics** (MonsterASP configuration, GitHub Actions
   workflow contents, whether `.github/workflows/deploy-monsterasp.yml`
   still matches `PROJECT-STATUS.md`'s description) — not opened.
6. **Database check-constraint drift** (`CK_Records_Medium`/`CK_Records_Filing`/
   `CK_Records_Access`) — `RecordsController`'s own code comment states these
   "still only allow the old dropdown choices" on *some* deployed database.
   Whether this is still true of the current production database specifically
   cannot be determined from source; it requires querying the live schema.

## D. Inspection Checklist (what this pass actually read, for traceability)

**Fully read (content, not just names):**
`Program.cs`, `Data/AppDbContext.cs`, `Security/Permissions.cs`,
`Security/RolePermissions.cs`, `Controllers/AccountController.cs`,
`Controllers/TasksController.cs` (both halves), `Controllers/RecordsController.cs`
(through the start of `SetDisposalStatus`; the remainder of that one method's
body past the shown excerpt was not re-read but its sibling three actions
calling the same private method were fully read), `Controllers/EmployeeController.cs`,
`Controllers/ReportsController.cs`, `Controllers/NotificationController.cs`,
`Controllers/UserManagementController.cs`, `Controllers/Admin/DashboardController.cs`,
`Controllers/TimeLogsController.cs`, `Controllers/LiveController.cs`,
`Services/TaskWorkflow.cs`, `Services/NotificationService.cs`,
`Services/TaskAssignmentService.cs`, `Filters/RequireLoginAttribute.cs`,
`Filters/RequirePermissionAttribute.cs`, `Models/TaskItem.cs`, `Models/User.cs`,
`BackgroundService/TaskReminderService.cs`,
`BackgroundService/RecordRetentionReminderService.cs`, plus the pre-existing
docs `SYSTEM_OVERVIEW.md` (root), `PROJECT-STATUS.md`,
`docs/requirements-documentation.md`.

**Listed/named only (via `Glob`), not opened:** every file under `Models/`
beyond `TaskItem.cs`/`User.cs`, every file under `Views/`, every file under
`wwwroot/`, every file under `Migrations/`, every file under
`DTIOneLink.Tests/`, `Services/Email/*`, and the remaining `Services/*.cs`/
`BackgroundService/*.cs` files not listed above.

**Not located/opened at all:** `.github/workflows/`, `docs/testing/`,
contents of `publish/` (deliberately not inspected — see §C.2 above).

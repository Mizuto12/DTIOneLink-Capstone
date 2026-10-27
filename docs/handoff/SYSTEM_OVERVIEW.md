# SYSTEM_OVERVIEW.md

**Generated:** 2026-10-26, by direct source inspection (not from screenshots or
file names alone). Cross-checked against two existing in-repo documents that
their own authors state were independently verified against the code:
`SYSTEM_OVERVIEW.md` (repo root, dated 2026-10-07) and
`docs/requirements-documentation.md` (checked against code "in two passes").
Where this document differs from those, it is because newer commits
(`git log`, through `9096cf5`, 2026-10-24) changed behavior — e.g. Records
`Approve`/`MarkArchived`/`RestoreToRetention` actions and overdue-task email
reminders did not exist as of the 2026-10-07 note.

This file is part of a documentation package for handoff to another AI
assistant (ChatGPT). See `CHATGPT_HANDOFF.md` for the condensed entry point
and `LIMITATIONS_AND_OPEN_QUESTIONS.md` for what could not be verified.

---

## 1. Purpose

**DTI OneLink** is an internal task-and-records management web application for
the **DTI (Department of Trade and Industry) Laguna Provincial Office**. It is
a capstone project (STI College Santa Cruz) titled *"An Integrated Task and
Records Management System for DTI Laguna Provincial Office."* It is for office
staff only — there is no public-facing functionality.

Core purposes, confirmed by the controllers and views present:
- Assign, track, and review work (**Tasks** module).
- Log and monitor records subject to retention/disposal schedules
  (**Records** module).
- Summarize task/record activity for staff and management (**Reports**,
  **Dashboards**).
- Manage user accounts and roles (**User Management**).
- Notify users of relevant events in-app and by email (**Notifications**).
- Push live UI updates without a manual page refresh (**SignalR / Live**).
- Record each sign-in session's time-in/time-out (**Time Logs** — explicitly
  *not* an attendance system; see `PROJECT-STATUS.md`'s scope rulings).

Explicitly **out of scope** (per `PROJECT-STATUS.md` and
`docs/requirements-documentation.md`, confirmed by DTI staff, not found
anywhere in the code):
- IPCR, performance ratings, staff ranking/"Employee of the Month" — DTI uses
  a separate system, "PGS Dashboard," for this.
- COA/ISO audits (paper-based).
- A disposal **workflow** — Records only *notifies*; physically disposing of
  a record stays a manual, outside-the-system action (confirmed: no code path
  deletes or exports a disposed record's content).
- An in-app backup/restore feature — relies on the hosting provider/SQL
  Server's own backup facilities. (`PROJECT-STATUS.md` notes a `backup-recovery`
  feature branch exists locally but is unmerged — see
  `LIMITATIONS_AND_OPEN_QUESTIONS.md`.)

## 2. Technology Stack (verified)

| Layer | Technology | Evidence |
|---|---|---|
| Runtime/Framework | ASP.NET Core MVC (Razor views), target `net10.0` / .NET 10 | `DTIOnelink.csproj`, `Program.cs` top-level statements |
| ORM / Database | Entity Framework Core 10 on **SQL Server** (`Microsoft.EntityFrameworkCore.SqlServer`) | `Program.cs` `AddDbContext<AppDbContext>`, `Data/AppDbContext.cs`, `Migrations/*` |
| Direct SQL | `Microsoft.Data.SqlClient` `SqlCommand`/`SqlConnection` used alongside EF in some controllers | `Controllers/RecordsController.cs`, `Controllers/UserManagementController.cs`, `Services/DatabaseHelper.cs` |
| Real-time updates | SignalR (`/hubs/live`) | `Services/LiveHub.cs`, `BackgroundService/LiveChangeBroadcaster.cs`, `Program.cs` |
| Authentication | Custom session-based login — **not** ASP.NET Identity | `Controllers/AccountController.cs`, session keys `UserId`/`UserRole`/`UserDepartment` |
| Session storage | In-memory distributed cache (`AddDistributedMemoryCache`) | `Program.cs` (a SQL-backed session store was tried and reverted — see inline comment) |
| Password hashing | ASP.NET Core Identity's `PasswordHasher<T>` (used standalone, not full Identity) | `AccountController`, `UserManagementController` |
| Email | Brevo transactional email API via `HttpClient`; falls back to console-logging (`UnconfiguredEmailSender`) when no API key is configured | `Services/Email/EmailSender.cs`, `Program.cs` |
| PDF/Excel generation | QuestPDF (Community license) and ClosedXML | `Program.cs` (`QuestPDF.Settings.License`), `Services/RecordMasterlistPdf.cs`, `Services/RecordMasterlistExcel.cs` |
| Data protection / key ring | `AddDataProtection().PersistKeysToFileSystem(App_Data/keys)` — keeps antiforgery/session cookie keys stable across restarts | `Program.cs` (detailed rationale comment) |
| Front end | Server-rendered Razor views, hand-written CSS, vanilla JavaScript — no SPA framework (no React/Angular/Vue in `wwwroot/lib` or `package.json`) | `wwwroot/css`, `wwwroot/js`, `Views/**/*.cshtml` |
| PWA elements | `wwwroot/manifest.json`, `wwwroot/sw.js`, `wwwroot/offline.html`, and an "offline status pill" UI element (`offline-sync.js` per commit `9096cf5`'s message) | file listing; **not independently content-verified in this pass** — see Limitations |
| Hosting (per `PROJECT-STATUS.md`) | MonsterASP, auto-deployed from `main` via GitHub Actions (`.github/workflows/`) | `.github/workflows/` present; not opened in this pass |
| Testing | xUnit-style test project `DTIOneLink.Tests` with unit and `Integration` tests, using `WebApplicationFactory<Program>` against an EF Core in-memory/"Testing" environment | `DTIOneLink.Tests/*.cs`, `Program.cs`'s `IsEnvironment("Testing")` branches |

## 3. High-Level Architecture

Classic ASP.NET Core MVC, three main layers, no separate API/SPA boundary:

```
Browser (Razor views + vanilla JS, wwwroot/js/*)
   │  HTML form posts, fetch() calls to JSON endpoints, SignalR client
   ▼
Controllers (Controllers/*.cs, Controllers/Admin/*.cs)
   │  session-based auth/authz checks (Filters/, Security/)
   ▼
Services (Services/*.cs, Services/Email/*.cs) — business rules, notifications,
   task workflow, record retention math, email dispatch, live-update
   fingerprinting
   ▼
Data access — EF Core (Data/AppDbContext.cs) for most entities;
   raw SqlCommand/SqlConnection for Records and parts of UserManagement
   ▼
SQL Server database, schema versioned by Migrations/*.cs, with a runtime
   "SchemaRepair" fallback (Data/SchemaRepair.cs) that adds missing
   columns/tables directly if a migration fails at startup.
```

Background work runs as hosted `BackgroundService` singletons
(`BackgroundService/*.cs`), each creating its own DI scope per run since the
services they use (`AppDbContext`, `NotificationService`, etc.) are
request-scoped:
- `RecurringTaskService` — creates the next occurrence of a recurring task.
- `TaskReminderService` — hourly due-soon/overdue notifications (in-app +
  email for overdue; in-app only for due-soon).
- `RecordRetentionReminderService` — hourly record-disposal reminders.
- `EmailDispatchService` — drains the `EmailOutbox` table, sending queued
  emails via Brevo.
- `LiveChangeBroadcaster` — pushes SignalR refresh signals when a tracked
  data "fingerprint" changes.

## 4. User Roles

Three roles, defined by *string value* on `User.Role` and checked via
`Security/RolePermissions.cs` (permission strings in `Security/Permissions.cs`):

| Role | Scope | Key permissions held (`RolePermissions.Map`) |
|---|---|---|
| **Employee** | Own assigned tasks only (via `TaskAssignment` rows) | `ManageTasks` (scoped to own tasks by controller query logic — the permission string is shared with Admin, but the controller code is what actually narrows an Employee to their own tasks) |
| **Admin** (also informally "Division Chief" / "Supervisor" in comments — there is **no separate `Supervisor` role string** actually used anywhere found; `Admin` is it) | Own department (`User.Department`), plus records management | `ManageTasks`, `ManageRecords`, `ViewConfidentialRecords`, `ManageUserAccounts` |
| **SuperAdmin** (the "OPD" / Office of the Provincial Director) | Office-wide, across every department | `ManageUserAccounts`, `ViewOfficeWideSummaries`, `ManageOfficeWideTasks`, `ViewConfidentialRecords`, `ApproveRecords` |

Important nuance confirmed in code (`TasksController`, `EmployeeController`
comments): several places check for role string `"Supervisor"` in addition to
`"Admin"` (e.g. `IsDepartmentTaskManager()`), but `UserManagementController.Roles`
only ever creates `SuperAdmin`/`Admin`/`Employee` accounts — `Supervisor`
appears to be dead/defensive code for a role that is never actually assigned.
This matches `PROJECT-STATUS.md`'s note: *"the paper's 'Supervisor' role =
`Admin`."*

Departments (divisions), from `UserManagementController.Divisions`:
`Office of the Provincial Director`, `Business Development Division`,
`Consumer Protection Division`, `Financial and Administrative Unit`. (The
`SYSTEM_OVERVIEW.md` root doc's shorthand "BDD/FAU/CPD" matches three of these;
the OPD is the fourth, used as the `WholeOfficeDepartment` constant in
`TasksController`.)

## 5. Where Things Live (quick map)

| Area | Primary files |
|---|---|
| Startup / DI / middleware pipeline / routing | `Program.cs` |
| Sign-in, passwords, OTP codes, lockout | `Controllers/AccountController.cs`, `Security/AccountDefaults.cs`, `Services/OneTimeCodeService.cs`, `Services/AccountSessionService.cs` |
| Roles & permissions | `Security/Permissions.cs`, `Security/RolePermissions.cs`, `Filters/RequireLoginAttribute.cs`, `Filters/RequirePermissionAttribute.cs` |
| Tasks (admin/office-wide side) | `Controllers/TasksController.cs` |
| Tasks (employee side) | `Controllers/EmployeeController.cs` |
| Task business rules | `Services/TaskWorkflow.cs`, `Services/TaskAssignmentService.cs`, `Services/TaskLevels.cs`, `Services/TaskTypes.cs`, `Services/TaskRecurrence.cs`, `Services/PrioritySuggestionService.cs`, `Services/OpdTaskService.cs`, `Services/TaskActivityLogger.cs` |
| Dashboards | `Controllers/Admin/DashboardController.cs`, `Views/Dashboard/*`, `Views/SuperAdmin.cshtml` |
| Records & masterlists | `Controllers/RecordsController.cs`, `Services/RecordMasterlistExcel.cs`, `Services/RecordMasterlistPdf.cs`, `Services/RecordRetentionReminder.cs`, `Services/RetentionPeriodParser.cs` |
| Reports | `Controllers/ReportsController.cs` |
| Notifications | `Controllers/NotificationController.cs` (class `NotificationsController`), `Services/NotificationService.cs`, `Models/Notification.cs` |
| Email | `Services/Email/EmailSender.cs`, `Services/Email/EmailTemplates.cs`, `Services/Email/EmailDispatchService.cs`, `Models/EmailOutboxMessage.cs` |
| User Management | `Controllers/UserManagementController.cs`, `Views/UserManagement/*` |
| Time Logs | `Controllers/TimeLogsController.cs`, `Models/TimeLog.cs` |
| Live updates | `Controllers/LiveController.cs`, `Services/LiveHub.cs`, `Services/LiveVersionService.cs`, `BackgroundService/LiveChangeBroadcaster.cs`, `wwwroot/js/live-refresh.js` (not opened this pass) |
| Background jobs | `BackgroundService/*.cs` |
| Database schema | `Data/AppDbContext.cs`, `Data/SchemaRepair.cs`, `Migrations/*.cs` |
| Views (UI) | `Views/**/*.cshtml` |
| Styles / scripts / images | `wwwroot/css`, `wwwroot/js`, `wwwroot/images` |
| Tests | `DTIOneLink.Tests/*.cs` |

## 6. Routing

From `Program.cs`:
- `/` and `/Account` → `AccountController.Login` (explicit routes; otherwise
  every controller would default to a `Login` action and 404 on its own index).
- `/{controller}/{action=Index}/{id?}` — standard MVC convention route for
  everything else.
- `/hubs/live` — SignalR hub, rejected outright (401) for anyone without a
  session `UserId`.
- `/health` — public, unauthenticated; returns `OK` (200) if
  `db.Database.CanConnectAsync()` succeeds, else `"Database unavailable"` (503).
  Used by an external uptime monitor to keep the host's process alive so
  background jobs keep running (comment in `Program.cs` names UptimeRobot).

## 7. Request Pipeline Behaviors Worth Knowing

All confirmed directly in `Program.cs`:
1. **Auto-migration on startup** (skipped in the `Testing` environment): runs
   `database.MigrateAsync()`; on failure, writes the error + failed migration
   name to `App_Data/database-update-error.txt` and calls
   `SchemaRepair.EnsureLatestSchemaAsync` to add missing columns/tables
   directly so the app stays usable even if a migration can't apply cleanly.
2. **Stale one-time-code cleanup on startup** (skipped in `Testing`): any
   unconsumed OTP rows are marked consumed, because the in-memory hashing key
   that validates them (`OneTimeCodeHasher`) is itself regenerated on every
   process start, so old codes can never validate anyway.
3. **Per-request session refresh middleware**: on every request with a
   session `UserId`, re-reads `Role`/`Department`/`IsActive`/`SecurityStamp`/
   `LastSeenAtUtc` from the database. A deactivated/deleted account, or a
   session whose stored `SecurityStamp` no longer matches the database (set on
   password/email change), is signed out (`Session.Clear()`) on the very next
   request. `LastSeenAtUtc` is updated at most once a minute — this is what
   drives the "Signed in" status shown in User Management.
4. **Static file caching**: files requested with a `?v=` query string (added
   by `asp-append-version` tag helpers) get `max-age=31536000, immutable`;
   everything else gets `max-age=86400`.
5. **HTTPS/HSTS** only enforced when `app.Environment.IsProduction()`.
6. Session cookie `SecurePolicy` is `Always` in Production, else
   `SameAsRequest` (so local HTTP dev and the `Testing` integration-test host
   still work).

## 8. The Three Environments (per root `SYSTEM_OVERVIEW.md`, not independently
re-verified by this pass beyond what the code implies)

| Environment | What it is | Data |
|---|---|---|
| Local developer machine | Working copy + local SQL Server instance | Separate local database; accounts are *not* shared with production |
| GitHub (`Mizuto12/DTIOneLink-Capstone`) | Shared source code | N/A |
| Production (`dtionelink.runasp.net`, hosted on MonsterASP per `PROJECT-STATUS.md`) | The deployed app | Its own production database; deploy is automatic on push to `main` per `.github/workflows/` (not opened this pass) |

See `docs/handoff/LIMITATIONS_AND_OPEN_QUESTIONS.md` for a known, documented
historical secret-leak issue in this repo's history (publish artifacts) that
`PROJECT-STATUS.md` flags — not independently re-confirmed in this pass, and
not reproduced here.

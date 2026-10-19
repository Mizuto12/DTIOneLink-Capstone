# DTI OneLink — Project Status and Remaining Work

As of 2026-10-06. Written for an AI coding agent (e.g., Codex) and the developer picking up the work.

**DTI OneLink: An Integrated Task and Records Management System for DTI Laguna Provincial Office** — a capstone project (STI College Santa Cruz).

## Overall completion

Measured against the project's own checklist ("New Checklist.docx", 43 numbered required items), after the scope rulings below.

| Measure | Score |
| --- | --- |
| Live site (`main`) | **32.5 / 36 = 90%** |
| Live site + the unmerged Backup branch | **33.5 / 36 = 93%** |
| …plus a recorded restore test | **34.5 / 36 = 96%** |
| If Records must also store uploaded document files (items 14–16 in scope) | 32.5 / 39 = 83% (live) |

The 36 excludes 4 items ruled out of scope (20, 26, 28, 41) and 3 file-upload items (14–16) whose scope is still undecided.

## Tech stack and how it runs

- ASP.NET Core MVC on **.NET 10**, EF Core 10, **SQL Server**. Raw `SqlCommand` is used in some controllers (UserManagement, Records); EF elsewhere.
- Session-based login (no ASP.NET Identity). Roles: `SuperAdmin` (the OPD), `Admin` (Division Chief), `Employee`. Session keys: `UserId`, `UserRole`, `UserDepartment`, `FullName`, `UserEmail`. Role checks are in each controller and `Security/RolePermissions`.
- Live updates via SignalR (`/hubs/live`, `wwwroot/js/live-refresh.js`). Email via **Brevo** API (`Services/Email`), codes through an in-memory queue, notices through the `EmailOutbox` table.
- Hosted on **MonsterASP**. **Every push to `main` deploys automatically** (`.github/workflows/deploy-monsterasp.yml`, Web Deploy). Secrets (DB connection, Brevo key, base URL) come from GitHub secrets.
- Database changes: EF migrations, applied at startup (`Program.cs`). If a migration fails on the host, `Data/SchemaRepair.cs` adds the newest columns/tables directly. **Every new migration must also be mirrored in `SchemaRepair.cs`.**
- `/health` returns `OK` when the database is reachable (for an uptime monitor).

## Conventions to keep

- Users are older, non-technical DTI staff: plain words, large tap targets, no browser `alert()/confirm()` (use the in-page dialogs already in the app), no unnecessary features.
- DTI Laguna has **no IT staff**: anything that needs upkeep must be doable by a Super Admin from the UI or a written guide.
- Icons use a **trimmed** Material Symbols font (`wwwroot/css/fonts.css` lists the included names). An icon not in that list renders as text.
- Phones: every page has rules for ≤ 767px (tables become labelled cards via `data-label`). Keep new pages responsive.
- Comments in code explain *why*; match the existing style.

## Scope rulings (do not build these)

- **IPCR, performance ratings, staff ranking, Employee-of-the-Month** — DTI uses its own **PGS Dashboard** for this (confirmed by DTI staff). Checklist items 26 and 28 are out of scope.
- **Audits (COA, ISO)** — paper-based, outside the system.
- **Time in / time out as attendance** — DTI has DTR/biometrics; a web login is not attendance.
- **Record archiving** (item 20) and **admin password reset** (item 41) — ruled out by the developer.
- Records only **notify** for disposal; no disposal workflow. The paper's "Supervisor" role = `Admin`.

## Checklist status

| Area (checklist items) | Status |
| --- | --- |
| Admin task management (1–8) | Done: edit, reassign/multi-assign, workflow statuses, overdue detection, rule-based priority suggestion, proof review, return with remarks, task history |
| Employee task management (9–12) | Done: progress, proof upload (validated), view result and resubmit, backend ownership checks |
| Records (13, 17–19, 21–24) | Done: DTI masterlist fields, server-side search, retention due-soon/ended, retention reminders, per-user scoping, saved masterlists (Excel) |
| Records file upload (14–16) | **Not built — scope undecided** (masterlist entries only today) |
| Dashboard (25, 27) | **Partial**: no records summary on the dashboard; only a status bar, no real charts |
| Reports (29, 30) | Done: database-backed, with a Period filter (month, custom dates; server-side, Philippine dates) |
| Notifications (31–35) | Done, plus email via Brevo |
| Users and passwords (36–40) | Done: forced password change with email verification, lockout, role checks, sign-in status |
| Backup and recovery (42–43) | **Built on a branch** (not deployed); restore test not yet done |
| Super Admin (44–46, optional) | Done |

## Work in progress (not on `main`)

Two local branches, stacked, **not pushed** (pushing to `main` deploys):

1. `sign-in-activity` (`33f5319`) — Super Admin **Sign-in Activity** page: sign-ins, sign-outs, wrong passwords, lockouts, password changes; 90-day retention. New table `SignInEvents` (migration `AddSignInEvents` + SchemaRepair).
2. `backup-recovery` (`efecd80`, on top of 1) — Super Admin **Backup** page: download proof files as ZIP, restore missing files from a ZIP (never overwrites; validated names; size cap; path traversal blocked), backup history, monthly reminder (`BackupReminderService`). New table `BackupLogs` (migration `AddBackupLogs` + SchemaRepair). Logic tested (13 checks passed).

Both live in the worktree `C:\Users\chandrei\Desktop\DTIOneLink-signin-activity`. To ship: merge `backup-recovery` into `main` (it contains both), build, push.

## Remaining work, in priority order

### A. Security (do first)

1. **Rotate the database password (developer, not code).** `publish/appsettings.Production.json`, committed since 2026-09-30 in this **public** repo, contains the live connection string with its password. Change the DB password in MonsterASP → update the `MONSTER_CONNECTION_STRING` GitHub secret → re-run the deploy workflow.
2. **Remove `publish/` from git** (186 tracked build files) and add it to `.gitignore`. History still holds the old password, which is why step 1 matters.
3. **Enforce HTTPS** in `Program.cs`: `UseHttpsRedirection()` and `UseHsts()` (outside Development).
4. **Session cookie flags**: `Cookie.SecurePolicy = Always`, `Cookie.SameSite = Lax` (antiforgery cookie too).
5. **Security headers** (middleware): `X-Frame-Options: DENY` (or CSP `frame-ancestors 'none'`), `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`.

### B. Checklist gaps

6. ~~Reports date/month filter (item 30)~~ — done and deployed (`85ddcee`).
7. **Export Data on Reports does nothing** (no handler). Either implement (export the currently shown, period-filtered list as CSV/XLSX) or remove. The old calendar/tune buttons were replaced by the Period filter.
8. **Records summary on the dashboard (item 25).** A small card on the Admin and Super Admin dashboards: records due for retention review soon / ended, linking to Records. (A "Records Overview" card was removed earlier as too much; keep this to one line or a few counts.)
9. **One real chart (item 27)**, e.g. tasks completed per month. Keep it simple and readable; no per-person ratings.
10. **Restore test (item 43)** — a person must run it using the "DTI OneLink Backup and Recovery Guide" test sheet, after the backup branch is deployed.
11. **Records file upload (items 14–16)** — only if the panel/DTI requires stored documents. If built: store outside `wwwroot`, unique names, validated types/size, downloads through an authorized action (same pattern as proof uploads in `EmployeeController` + `ProofFileValidator`), and extend the Backup ZIP to include them.

### C. Housekeeping

12. **Local database migration mismatch (developer's machine only).** The local DB already has `Users.LastLoginAtUtc/LastLogoutAtUtc/LastSeenAtUtc` from a replaced draft migration, so `AddUserSignInStatus` fails locally (`App_Data/database-update-error.txt`). Fix on the local DB only:
    ```sql
    DELETE FROM __EFMigrationsHistory WHERE MigrationId LIKE '%_AddUserLastLogin';
    IF COL_LENGTH('dbo.Users','LastLoginAtUtc') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = '20261005134137_AddUserSignInStatus')
        INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20261005134137_AddUserSignInStatus', '8.0.10');
    ```
    Then delete `App_Data/database-update-error.txt`. The live database is not affected.
13. **Uncommitted change in `wwwroot/css/superadmin.css`** on `main` from another session — review, then commit or discard.
14. **`tools/google-forms/CreateQuestionnaireForm.gs`** (evaluation questionnaire generator, not part of the app) is untracked — commit it if wanted; fill in its project-details placeholders first.

## Capstone deliverables outside the code

- **Evaluation questionnaire** (ISO/IEC 25010, Likert, for the IT expert) — a doc plus the Google Forms script above. Placeholders to fill: program, researchers, adviser, contact email.
- **Backup and Recovery Guide** — a doc with monthly steps, recovery steps, recovery targets and the restore test sheet. Open questions in it: MonsterASP's exact backup/restore menu, automatic backup retention, backup folder, responsible person.

# DTI Laguna OneLink — System Overview

A plain-language guide to what the system does and how it flows. This note is
documentation only; it is not used by the app and changes nothing when present
or removed.

Last updated: 2026-10-07, from branch `ui-redesign` (the UI redesign merged with
the colleague's `main` up to commit `08f3375`).

---

## 1. What it is

An internal web app for the **DTI Laguna Provincial Office** to coordinate work:
assign and review tasks, submit proof of completed work, keep a records
masterlist with retention reminders, view reports, and manage staff accounts.
It is for **office staff only**, not the public.

**Tech stack**
- ASP.NET Core MVC (Razor views), **.NET 10**
- SQL Server (via Entity Framework Core)
- SignalR for live page updates
- Brevo for sending email (verification codes, notices)
- Plain hand-written CSS and vanilla JavaScript — no front-end framework

---

## 2. The three copies (important)

| Place | What it is | Changes when… |
|---|---|---|
| **Your PC (this folder)** | Your working copy + your local SQL database | you edit/pull |
| **GitHub** (`Mizuto12/DTIOneLink-Capstone`) | The shared source code | someone pushes |
| **Live site** (`dtionelink.runasp.net`) | The running app + its own database | someone **publishes/deploys** |

- Editing files here does **not** affect GitHub or the live site.
- Accounts and data live in each copy's **own database**. Pulling code never
  copies accounts. That is why an account from the live site does not exist on
  your local database.
- Deploys are **manual** — coordinate with your teammate before pushing to
  `main` or publishing.

---

## 3. Roles and permissions

Three roles, defined in `Security/RolePermissions.cs`:

| Role | Can do |
|---|---|
| **Employee** | See and work on their **own** tasks; submit proof; comment |
| **Admin** | Manage tasks and records **in their department**; manage confidential records; manage user accounts |
| **SuperAdmin** (OPD) | Office-wide summaries; create/assign/edit tasks **across all departments**; manage user accounts. (Deliberately **not** given records access.) |

Permissions are strings (`Security/Permissions.cs`) checked server-side with the
`[RequirePermission(...)]` filter; `[RequireLogin]` requires any signed-in user.
Departments are **BDD**, **FAU**, **CPD** (`Services/TaskLevels.cs`).

---

## 4. Sign-in flow

Handled by `Controllers/AccountController.cs`. Sign-in uses **email + password**
and a server-side **session** (no cookies-based auth framework).

```
Enter email + password
  ├─ wrong password 5× ........→ account locked 15 min
  ├─ using default password ...→ must verify email + set a new password first
  └─ correct
        ├─ MustChangePassword? → VerifyEmail (if email unconfirmed) → SetPassword
        └─ otherwise ........... → signed in, sent to the role's home page
```

Key security behavior (constants in `Security/AccountDefaults.cs`):
- **Default password** for new accounts: `dtionelink2026`. It cannot be used as
  a real login — it forces email verification + choosing a new password, and is
  on the banned-password list.
- **Lockout**: 5 wrong passwords → locked 15 minutes.
- **Emailed codes**: 6-digit, expire in 10 minutes, max 5 tries, resend every
  60 s, max 3 per 15 minutes.
- **Password reset**: Forgot password → emailed code → set new password.
- Passwords are stored **hashed** (one-way) — they can never be read back.
- **Sign-in status** (shown in User Management): signing in records
  `LastLoginAtUtc`, Sign Out records `LastLogoutAtUtc`, and every page load
  updates `LastSeenAtUtc` (at most once a minute). An account shows
  **Signed in** when it logged in after its last sign-out and was seen in the
  last 30 minutes; otherwise **Signed out + "Last active …"**, or
  **Not signed in yet** if it never has. Deactivated always wins.

Role home pages (`RoleHomeUrl`):
- SuperAdmin → `/Dashboard/SuperAdminDashboard`
- Admin → `/Dashboard/AdminDashboard`
- Employee → `/Employee/Index`

---

## 5. Email and verification codes (and local testing)

- Configured with a Brevo API key → real emails are sent (`BrevoEmailSender`).
- **No key (your local setup)** → `UnconfiguredEmailSender` is used. In
  **Development**, it **prints the email, including the code, to the console**
  instead of sending it. `run-local.ps1` runs in Development, so when testing
  locally you read the code from the **terminal**, not an inbox.
- Codes are kept **in memory only** (never stored readable in the database) and
  are cancelled on every app restart — if the app restarts, request a new code.

---

## 6. Tasks (the core workflow)

**Shape of a task** (`Services/`):
- **Level**: `main` task or `subtask`.
- **Type**: `direct-admin`, `department-directive`, or `whole-office`
  (`direct-admin` and `whole-office` are assigned by the OPD / SuperAdmin).

**Status flow** (`Services/TaskWorkflow.cs`):

```
Pending → In Progress → For Review → Completed
                 ↑            │
                 └── Returned for Correction ←┘
```

- Overdue is shown based on the due date while not completed.
- Transitions are validated so a task cannot jump to an invalid state.

**Who does what**
- **Assign / create / edit** tasks — `Controllers/TasksController.cs`
  (`Create`, `Edit`, `Index` list, `ReceivedTasks`, `MainTaskDetails`).
  Admins are scoped to their department; SuperAdmin is office-wide.
- **Work the task** — `Controllers/EmployeeController.cs`: update progress,
  submit proof (file upload), add comments.
- **Review** — `TasksController.Review`: approve → Completed, or send back →
  Returned for Correction.
- **Recurring tasks** run automatically (`RecurringTaskService`); reminders for
  due/overdue tasks run automatically (`TaskReminderService`).

Supporting tables: `TaskItems`, `TaskAssignments`, `TaskSubmissions` (proof),
`TaskActivities` (history), `TaskComments`.

---

## 7. Records and masterlist

`Controllers/RecordsController.cs` (Admin, department-scoped; confidential
records need `ViewConfidentialRecords`):
- Browse/search records (`GetAll` with filters: text, medium, access,
  retention).
- Add/edit a record (`Save`).
- **Masterlist**: open/close/save a masterlist and **download it as Excel**
  (`DownloadMasterlist`, built with ClosedXML in `Services/RecordMasterlistExcel.cs`).
- **Retention reminders** run automatically (`RecordRetentionReminderService`)
  for records nearing the end of their retention period.

---

## 8. Reports

`Controllers/ReportsController.cs`: an Index page plus a `Data` endpoint that
feeds the charts/figures. Uses existing task/record data — no invented numbers.
A **Period** filter (All time, This month, Last month, a month, or custom
From/To dates) narrows everything to items with activity in that period; the
choice is kept in the page address. The former "Audit Logs" category is now
**"Task History"**.

Staff ratings (Top Performers, per-person Efficiency) were removed on purpose:
DTI Laguna tracks staff targets in its own PGS Dashboard, so OneLink stays a
task platform. The Employee dashboard's "My progress" card shows the person's
own task **counts** only — no percentages or scores.

---

## 9. Notifications and live updates

- **Notifications** (`Controllers/NotificationController.cs`, `Notifications`
  table): the bell list, mark-read, mark-all-read, dismiss.
- **Live updates** (SignalR): open pages connect to `/hubs/live`.
  `LiveChangeBroadcaster` watches a data "fingerprint" and pushes a refresh
  signal when it changes, so lists update without a manual reload. The hub
  rejects connections from users who are not signed in.

---

## 10. What happens on startup (`Program.cs`)

1. **Auto-migrate the database** — brings the schema up to date on every start,
   so a deploy that adds columns/tables needs no manual DB step. If a migration
   fails, the error is written to `App_Data/database-update-error.txt` and
   `SchemaRepair` adds missing columns/tables directly so the app keeps working.
2. **Cancel leftover verification codes** from before the restart (they can no
   longer match, since the checking key is in memory only).
3. **Static files are cached by browsers.** Files linked with
   `asp-append-version` (all CSS/JS here) carry a version hash and are kept for
   a year; any change to the file changes the hash, so updates still arrive.
   Other files (fonts) are re-checked daily.
4. **Per-request session sync** — on every request, the signed-in user's role
   and department are re-read from the database, so a promotion/demotion or
   department change applies on the next page load. A deactivated/deleted
   account, or a session older than the account's last password/email change
   (security stamp mismatch), is signed out automatically.

---

## 11. Routing map

- `/` and `/Account` → login page.
- `/{controller}/{action}/{id?}` → standard MVC routes (default action
  `Index`), so `/Records`, `/Tasks`, etc. open that page.
- `/hubs/live` → SignalR hub (signed-in users only).
- `/health` → public uptime check; returns `OK`, or `503` if the database is
  unreachable. Used by an uptime monitor to keep the host awake so the
  background jobs keep running.

---

## 12. Background jobs (run on their own)

| Service | Job |
|---|---|
| `RecurringTaskService` | Creates the next occurrence of recurring tasks |
| `TaskReminderService` | Reminders for due-soon / overdue tasks |
| `RecordRetentionReminderService` | Reminders for records nearing retention end |
| `EmailDispatchService` | Sends queued verification codes and email notices |
| `LiveChangeBroadcaster` | Pushes live-refresh signals to open pages |

---

## 13. Data model (tables)

`Users` (incl. `LastLoginAtUtc`, `LastLogoutAtUtc`, `LastSeenAtUtc`), `TaskItems`, `TaskAssignments`, `TaskSubmissions`, `TaskActivities`,
`TaskComments`, `Notifications`, `OneTimeCodes`, `EmailOutbox`, `UserItems`.
Defined in `Data/AppDbContext.cs`; schema changes live in `Migrations/`.

---

## 14. Where things live (quick map)

| Area | Files |
|---|---|
| Startup, pipeline, routing | `Program.cs` |
| Sign-in / passwords / codes | `Controllers/AccountController.cs`, `Security/AccountDefaults.cs` |
| Roles & permissions | `Security/Permissions.cs`, `Security/RolePermissions.cs` |
| Tasks | `Controllers/TasksController.cs`, `Controllers/EmployeeController.cs`, `Services/Task*.cs` |
| Dashboards | `Controllers/Admin/DashboardController.cs`, `Views/Dashboard/*` |
| Records | `Controllers/RecordsController.cs`, `Services/RecordMasterlistExcel.cs` |
| Reports | `Controllers/ReportsController.cs` |
| Email | `Services/Email/*` |
| Views (UI) | `Views/**/*.cshtml` |
| Styles / scripts / images | `wwwroot/css`, `wwwroot/js`, `wwwroot/images` |

---

## 15. Running it locally

```powershell
.\run-local.ps1          # normal run (Development, http://localhost:5080)
.\run-local.ps1 -Watch   # auto-reload on CSS/view edits (restart if a page change doesn't show)
```

- Runs in **Development**, so verification codes print to the **terminal**.
- Uses your local SQL instance (`localhost\SQLEXPRESS`), separate from the live
  site's database.
- Only `superadmin@local.test` exists in your local database right now; its
  password is hashed and cannot be recovered — reset it locally or ask your
  teammate for a known account.

---

## 16. Recent UI (branch `ui-redesign`)

- **User Management:** one connected module — header strip, identical form
  fields, Save level with the pagination bar; fixed proportional columns with
  "…" + tooltips; 6 accounts per page that fill the area on every page
  (`user-management.js` sizes the rows); quiet Change Role / Deactivate buttons.
- **Task Management (Admin/Super Admin):** status tiles on top, Create Task in
  the search line, balanced filter row, fixed 5-row frame, 5 tasks per page;
  clicking a subtask opens it directly.
- **Employee dashboard:** To Do / In Progress boards, a calendar card that opens
  into a full month (pick a day to see work due or assigned that day), and
  "My progress" counts. Employee Task Management boards fill the width.
- **Super Admin Task Board:** To Do · In Progress · For Review over
  Returned · Completed.
- The non-working top-bar search was removed from all layouts.

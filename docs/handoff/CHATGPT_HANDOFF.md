# CHATGPT_HANDOFF.md

**Purpose of this file:** a condensed entry point for an AI assistant (ChatGPT)
that will help decide what revisions to make to this system, informed by
academic-panelist feedback, and then help draft a precise implementation
prompt back to Claude Code. This file summarizes; it does not replace the
other six documents in `docs/handoff/` — read this first, then go to the
named file for detail/evidence on anything that matters to the decision at
hand.

**Read this file, then:**
1. `SYSTEM_OVERVIEW.md` — architecture, stack, roles, routing, startup behavior.
2. `FEATURE_INVENTORY.md` — every feature found, with an implementation-status
   classification and file evidence.
3. `SYSTEM_STORY.md` — four narrative walkthroughs (Employee, Admin,
   SuperAdmin, background jobs) showing how modules actually interact.
4. `MODULE_DEPENDENCIES.md` — cross-module data flow and specific coupling
   risks (duplicated authorization logic, non-atomic notification writes).
5. `DATABASE_AND_BUSINESS_RULES.md` — schema, FK/cascade behavior, the task
   state machine, and where validation/authorization actually lives.
6. `TECHNICAL_REFERENCE.md` — feature → file/symbol index.
7. `LIMITATIONS_AND_OPEN_QUESTIONS.md` — what was *not* verified, three
   confirmed inconsistencies between this code and older project docs, and
   one potential live security issue that needs a human's attention
   separately from any feature decision (see §C.2 there).

---

## 1. What the system is

**DTI OneLink** — an internal ASP.NET Core MVC web app (.NET 10, EF Core 10,
SQL Server, SignalR, Brevo email) for the **DTI Laguna Provincial Office** to
manage tasks (assignment, progress, proof-of-completion, review) and records
(logging, retention/disposal tracking, masterlists), with supporting
dashboards, reports, notifications, and user/role administration. It is a
capstone project for staff who are **non-technical and have no in-house IT
support** — this constrains what kinds of revisions are realistic (anything
requiring ongoing manual maintenance is a poor fit unless it can be fully
self-service from the UI).

## 2. Technology stack

ASP.NET Core MVC (Razor views, server-rendered — no SPA framework), EF Core
+ raw `SqlCommand` (mixed, deliberately — EF for most entities, raw SQL for
`Records` and parts of `UserManagement`), SQL Server, SignalR for live page
updates, Brevo for transactional email, custom session-based auth (not ASP.NET
Identity), vanilla JS/hand-written CSS, QuestPDF + ClosedXML for document
generation, xUnit-style tests in a separate `DTIOneLink.Tests` project.

## 3. Modules and roles

**Modules:** Accounts/Auth, Roles & Permissions, Tasks (admin side + employee
side), Records, Reports, Notifications, Dashboards, Time Logs, Live Updates,
User Management, background jobs (recurring tasks, reminders, email dispatch,
live broadcast).

**Roles:** `Employee` (own tasks only), `Admin` (department-scoped — tasks +
records + user management within their own division), `SuperAdmin` (the "OPD" —
office-wide task oversight, user management, record approval; **not**
department-scoped records/task management permissions, by design). Four
departments: Office of the Provincial Director (OPD, the SuperAdmin's own),
Business Development Division, Consumer Protection Division, Financial and
Administrative Unit.

## 4. Key workflows (see `SYSTEM_STORY.md` for full narratives)

- **Task lifecycle:** `pending → in-progress → for-review → completed`, with
  a `returned-for-correction → for-review` loop. Enforced centrally by
  `Services/TaskWorkflow.cs`. Applied per-assignee, then rolled up to the
  task, then (for a subtask) rolled up again to its parent Main Task.
- **Three Main Task types** (OPD/SuperAdmin-created only): Direct Admin Task
  (one named Admin, reviewed by OPD), Department Directive (distributed via
  subtasks by a Responsible Admin, reviewed by that department's Admins),
  Whole Office (everyone, auto-assigned).
- **Records lifecycle:** `Retention → Disposal → Disposed` (one-way), with a
  parallel `→ Archived → (RestoreToRetention) → Retention` branch for
  permanent-value records. A separate PD/SuperAdmin "Approve" sign-off exists
  independently of this lifecycle and is cleared automatically on any content
  edit.
- **Notifications:** in-app always; email for a specific subset of event
  types (task assigned/reassigned, approved/returned, overdue [as of a recent
  commit], OPD directive received, password changed, record
  due/marked/disposed). Due-soon reminders and most edit notices are in-app
  only.

## 5. Critical cross-module dependencies to know before changing anything

- **Department-scoping logic is duplicated, not shared**, across
  `TasksController`, `EmployeeController`, `Controllers/Admin/DashboardController.cs`,
  and `ReportsController` (near-identical private helper methods and near-
  identical LINQ `Where` clauses in each). A scoping-rule change must be
  applied in all four places or it will silently diverge — this has already
  happened once historically per in-code comments.
- **Notification writes are not atomic with the task/record state change
  that triggers them** — two separate `SaveChangesAsync` calls. A crash
  between them loses the notification, not the state change.
- **Records schema is runtime-probed, not assumed** — `RecordsController`
  checks `sys.columns` (cached 5 min) before touching several "optional"
  columns, specifically so the app still runs against a database where a
  migration hasn't been applied yet. Any revision that adds a new Records
  column should follow this same tolerant pattern if the live database
  isn't guaranteed to be migrated immediately — or confirm whether that
  historical risk still applies to current deployment practice.
- **`ReportsController`'s own raw-SQL read of `Records` does *not* use the
  same schema-tolerance probing `RecordsController` does** — a latent
  inconsistency, not confirmed to have caused a real failure, flagged in
  `MODULE_DEPENDENCIES.md` §6.

## 6. Database highlights

EF-mapped: `TaskItems`, `Users`, `TaskAssignments` (the real multi-assignee
model — `TaskItem.AssigneeId` is legacy/primary-only), `TaskSubmissions`
(append-only, one per attempt), `TaskActivities` (append-only audit trail,
Tasks only), `TaskComments`, `Notifications`, `OneTimeCodes` (hashed, in-
memory-keyed, invalidated on every app restart), `EmailOutbox`, `TimeLogs`.
**Not EF-mapped, raw-SQL only:** `Records`, `RecordMasterlists`. No soft
deletion anywhere except `User.IsActive`; genuine SQL deletes elsewhere. No
unique constraint on `Users.Email`/`Username` at the DB level (enforced only
in application code).

## 7. Implementation-status summary (see `FEATURE_INVENTORY.md` for full detail/evidence)

**Verified implemented** (high confidence — read directly): full auth flow
incl. OTP/lockout/remember-device, role/permission model, task
creation/edit/assign/reassign/review/delete across all three Main Task types,
recurring-task validation & stop action, proof submission with history,
records CRUD + full disposal/archive/restore/approve lifecycle, retention
reminders, dashboards (Admin/SuperAdmin/Completion-Trend), unified Reports
feed with period filter, notification bell + ~20 event-driven notify methods
with selective email, Time Logs, CSRF protection, rate limiting on auth.

**Partially implemented / unclear:** Reports CSV export (contradictory
claims across existing docs — resolve by checking `wwwroot/js/reports.js`,
not opened this pass); Masterlist Excel/PDF generation (schema/session logic
confirmed, generation code itself not opened); "mark a comment as a formal
revision note" (requirements doc claims this exists; no supporting field
found on `TaskComment` in this pass).

**Apparently missing from the inspected branch:** the Backup & Recovery
feature `PROJECT-STATUS.md` describes (ZIP backup/restore, `BackupLogs`
table) — no matching migration or service file found via search; likely
still sitting on an unmerged local branch per that same document.

**Explicitly out of scope by prior decision** (confirmed absent from code,
not a gap): IPCR/performance ratings/staff ranking (DTI's separate "PGS
Dashboard" system), COA/ISO audits, in-app backup/restore as a *software*
feature (relies on hosting-provider backups instead — though see the
paragraph above; a feature branch for this apparently exists but isn't on
`main`).

## 8. Known inconsistencies between this code and the project's own prior
   documentation (do not let a panelist's question catch this cold)

1. `PROJECT-STATUS.md` says Records disposal is notify-only with archiving
   "ruled out"; the current code has a fully implemented Archive/Restore
   lifecycle. The code is newer — treat the old doc as superseded on this
   point, but confirm with the developer whether this was a deliberate,
   documented scope reversal or an undocumented one.
2. `docs/requirements-documentation.md` says overdue reminders are in-app
   only; a dated commit (`42efeba`) added email for overdue specifically.
   Same resolution: code is newer, old doc is stale on this one point.
3. Reports CSV export: genuinely unresolved as of this pass — the server
   side has no handler, but the client side (JS) was not inspected. Needs a
   follow-up read of `Views/Reports/Index.cshtml` and any `reports.js`
   before treating this as either present or missing.

## 9. Most important considerations for whatever gets decided next

- **No shared authorization/scoping layer exists.** Any revision that
  touches department-scoping, review-authorization, or office-wide-vs-
  departmental checks must be applied consistently across (at minimum)
  `TasksController`, `EmployeeController`, `DashboardController`, and
  `ReportsController` — there is currently no single policy class to change
  once. If a revision plan involves refactoring this into a shared service,
  that refactor itself is a meaningful, separately-scoped piece of work (not
  a side effect of a feature change) given how many near-identical
  LINQ/private-method copies exist.
- **Live production secret-leak risk is unresolved as of this pass** (a
  possibly-real connection string committed under `publish/`,
  `appsettings.Production.json`) — this was *not* opened/read in this pass
  specifically to avoid exposing a live secret into this documentation
  package, but its mere presence in the tracked working tree is itself
  worth surfacing to the developer immediately, independent of any feature
  decision. See `LIMITATIONS_AND_OPEN_QUESTIONS.md` §C.2.
- **The team already maintains detailed, code-checked documentation**
  (`docs/requirements-documentation.md` explicitly describes a two-pass
  verification process against the manuscript). Future documentation work
  should extend/correct that document rather than duplicate it from scratch —
  this handoff package is deliberately a parallel, broader technical
  artifact (covering implementation status/evidence/dependencies that the
  requirements doc doesn't attempt), not a replacement for it.
- **Test coverage exists and was not leveraged in this pass.** 24 test files
  in `DTIOneLink.Tests/` almost certainly already encode the exact edge
  cases this package describes from reading source (task assignment,
  cross-department Responsible Admin handling, role permissions,
  notification triggering). Before specifying a new implementation prompt,
  it would be efficient to have Claude Code run `dotnet test` and/or read the
  relevant test file to confirm current behavior with higher confidence than
  static reading alone provides, especially for anything time-sensitive
  (recurrence, retention date math, overdue detection) that depends on
  exact date/timezone handling (`Services/TimeZoneHelper.cs` — referenced
  throughout but not opened this pass).

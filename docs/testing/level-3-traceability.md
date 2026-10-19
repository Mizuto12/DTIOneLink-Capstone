# Level 3 — Requirements Traceability Matrix

DTI OneLink: An Integrated Task and Records Management System for DTI Laguna Provincial
Office. Roles: **SuperAdmin** (OPD), **Admin** (Division Chief), **Employee**.

**Source of truth for requirements:** `docs/requirements-documentation.md` (Functional
Requirements 1–14, Non-Functional Requirements 1–10), itself verified against the code in
two prior passes this session. This matrix adds a third pass covering areas that doc's
audits didn't reach (notification scoping, records access scoping, reports filter/export,
dashboard role differences, task reassignment) and cross-checks against
`PROJECT-STATUS.md` and `docs/testing/four-levels-of-testing.md` (which already defines
FR-01–FR-12 for the Level 1–3 automated suite; those IDs are cross-referenced below where
they overlap, not duplicated).

**Status values:** Implemented, Partial, Missing, Excluded, Unverified.

---

## Conflicts found between project documents and current code

These are called out separately because the task instructions require reporting
discrepancies rather than assuming either source is correct — in both cases, the code was
re-read directly to settle it.

| # | Document claim | Code reality | Resolution |
| --- | --- | --- | --- |
| C-1 | `PROJECT-STATUS.md` (dated 2026-10-06): *"Export Data on Reports does nothing (no handler)."* | `wwwroot/js/reports.js:203-225` wires a real `click` handler to `#export-btn` that builds and downloads a CSV (`onelink-reports-YYYY-MM-DD.csv`) from the currently filtered report list. `git log -S export-btn` shows this was added in commit `b70b8a1` on 2026-10-09 — three days after `PROJECT-STATUS.md`'s date. | **Implemented.** The status document is stale, not the code. Treat `PROJECT-STATUS.md`'s "Remaining work" item 7 as resolved. |
| C-2 | `PROJECT-STATUS.md`: *"admin password reset (item 41) — ruled out by the developer"* (listed under "Scope rulings — do not build these"). | `Controllers/UserManagementController.cs:397-400` implements an admin-triggered reset that issues the account a new temporary password. | **Implemented, contradicting the scope ruling.** This needs a decision before the defense: either the scope note is outdated and the feature should be documented (update `requirements-documentation.md` Feature 1 and `PROJECT-STATUS.md`), or the feature needs to be deliberately removed to match the stated scope. Flagging rather than silently picking one, per instructions not to assume. |

---

## Functional Requirements

| # | Requirement (docs/requirements-documentation.md) | Status | Evidence | Notes |
| --- | --- | --- | --- | --- |
| FR3-1 | User Account and Role Management | **Implemented** | `UserManagementController.cs` role-creation guards; `User.cs:27` (`Department`) | SuperAdmin-only for Admin/SuperAdmin creation and role/division changes; Admin limited to own-division Employee creation. Also see C-2 above (password reset exists but isn't in this requirement's text). |
| FR3-2 | Secure Login and Access Control | **Implemented** | `AccountController.cs` (hash, lockout, OTP branch); `Program.cs` session config | OTP triggers only when `MustChangePassword=true` AND `EmailConfirmed=false`, not generically for "default/temporary password." Matches FR-01–FR-03 in `four-levels-of-testing.md`. |
| FR3-3 | Task Creation, Assignment, Detailed Task Anatomy | **Implemented** | `Models/TaskItem.cs`, `TaskAssignmentService.cs:215-251` (subtask rollup) | No start date/tags/watchers/general attachments fields — confirmed absent, not merely untested. Subtask→parent status rollup is automatic with no override. |
| FR3-4 | Task Priority Suggestion | **Implemented** (rule-based only) | `Services/PrioritySuggestionService.cs` | Explicitly rule-based (days-until-due), not ML/regression. Matches the Feature 4 rewrite in requirements-documentation.md. |
| FR3-5 | Deadline and Reminder Management | **Implemented** (in-app only) | `BackgroundService/TaskReminderService.cs`; `NotificationService.cs:244-274` | Due-soon/overdue reminders do not send email (confirmed no email subject passed) — in-app feed only. |
| FR3-6 | Task Progress Updates | **Implemented** | `Services/TaskWorkflow.cs:9-13, 88-97` | Statuses: To Do (stored as Pending), In Progress, For Review, Returned for Correction, Completed. Overdue is computed at display time, never stored. |
| FR3-7 | Proof of Completion Submission | **Implemented** | `Models/TaskSubmission.cs`; `Services/ProofFileValidator.cs` | One proof file per submission, not general multi-file attachments. |
| FR3-8 | Supervisor Review and Validation | **Implemented** | `TaskSubmissionDecisionViewModel.cs:10`; `TasksController.cs:1179-1199` | Exactly two outcomes: approve or return (remarks required on return). No third "request revision" option exists. |
| FR3-9 | Workflow Status Monitoring (+ Task Reassignment) | **Implemented** | `TasksController.cs` Index filters (lines 157-235); `TaskAssignmentService.cs:137-164` (reassign) | Filters: priority, employee, due date, status always available; division filter is **SuperAdmin-only** (Admin is always restricted to their own division, not offered a toggle). List View and Kanban Board are **separate pages**, not a switch on one page: the filterable list is Task Management; the board lives on Employee Task Management and as a Timeline/Board option on the Admin dashboard. Reassignment requires same-division active Employee, is blocked once a task is Completed/For Review, records `ReassignedFromUserId` (most recent only — full chain is in the activity log, not a dedicated history table), and notifies both the outgoing and incoming assignee. |
| FR3-10 | Records Classification and Indexing | **Implemented** | `Models/RecordEntry.cs` | Fields: Code, Title, Medium, Location, Period Covered, Filing System, Access Control, Retention Period, plus system fields CreatedByUserId/OwningDepartment/RecordDate/RetentionDueDate. No document-type/subject/source-office/reference-number/sender/recipient fields — confirmed not just untested but genuinely absent. |
| FR3-11 | Records Search and Retrieval | **Implemented, narrower scope than division** | `RecordsController.cs:208-212, 429-434` | **Visibility is owner-only** (`CreatedByUserId = session user`), not division-scoped. A Division Chief cannot see records logged by their own division's other staff, and this is stated in-code to apply to SuperAdmin too. This is a real, deliberate design choice worth confirming is intentional before the defense — a panelist comparing this to the "division-scoped" framing used elsewhere in the system (Tasks, User Management) may ask why Records is scoped differently. |
| FR3-12 | Retention and Disposal Monitoring | **Implemented (notify only, as scoped)** | `RetentionPeriodParser.cs`; `RecordRetentionReminderService` | Due-soon = retention due date within 6 months; ended = date passed. No disposal action exists anywhere in code — matches the explicit "notify only" scope ruling and the task's scope exclusion on "automatic archival or disposal." |
| FR3-13 | Notification and Alert Management | **Implemented, correctly scoped** | `NotificationService.cs:26-44, 75-107`; `NotificationController.cs:44` | Every notification has one explicit `RecipientUserId`; fetch/mark-read/dismiss all filter by the session user's own ID — one user cannot act on another's notification by ID. Admin fan-out is scoped to the task's division; Whole Office tasks intentionally broadcast to all active Admins and Employees. Email sent only for assign/reassign/approve/return/admin-directive/records-due-for-disposal; due/overdue reminders are in-app only. |
| FR3-14 | Administrative Dashboard and Operational Reports | **Implemented, with one scoping gap** | `Controllers/Admin/DashboardController.cs`; `ReportsController.cs:53, 230-267, 307` | SuperAdmin = office-wide, all divisions, no filter. Admin = own division only, **but with a gap**: the Admin dashboard's task query has no "my own assignment" clause (unlike Employee's), so a Whole Office task assigned to that Admin personally does not appear on their own dashboard (it does appear on their Employee-style task views). Records Summary widget exists on the **Admin dashboard only**, not SuperAdmin or Employee. Reports page: `from`/`to` date-range filter is real and server-validated; CSV export is real and client-side (see C-1) — no server-side export, no XLSX. |

## Non-Functional Requirements

| # | Requirement | Status | Evidence | Notes |
| --- | --- | --- | --- | --- |
| NFR3-1 | Scalability | **Unverified** | — | No load/scale test has been run against a growing dataset; see Level 3 performance results. |
| NFR3-2 | Performance | **Partially verified** | `docs/testing/four-levels-of-testing.md` JMeter results (existing, not re-run here) | Existing report shows a cold-start/mid-load/warm-load spread tied to free-tier hosting wake-up, not app-level errors. See `level-3-performance-results.md`. |
| NFR3-3 | Reliability | **Unverified at Level 3** | — | No multi-day/long-running Level 3 reliability case has been executed; `implementation-results.md`'s UAT survey flags Reliability as the lowest-scoring (still "Excellent") ISO/IEC 25010 characteristic, worth a closer look. |
| NFR3-4 | Security | **Implemented, confirmed** | ASP.NET Core Identity `PasswordHasher` (PBKDF2 + HMAC-SHA512, salted, auto-rehash) in `AccountController.cs:44,89,108-110`; lockout; RBAC | Matches the corrected wording in requirements-documentation.md. |
| NFR3-5 | Accessibility and Compatibility | **Unverified at Level 3** | — | No cross-browser/cross-device sweep executed in this pass. |
| NFR3-6 | Usability | **Unverified at Level 3 (UAT data exists)** | `docs/implementation-results.md` | UAT survey (n=7/20) gives 4.67 — but that's Level 4 acceptance data, not a Level 3 system test. |
| NFR3-7 | Availability | **Unverified** | — | Depends on hosting provider; no uptime-monitoring data reviewed in this pass beyond the `/health` endpoint's existence (`PROJECT-STATUS.md`). |
| NFR3-8 | Maintainability | **Unverified at Level 3** | — | Not a UI-testable property; assessed by code review (see `docs/requirements-documentation.md`'s own audit trail), not Level 3 black-box testing. |
| NFR3-9 | Data Integrity | **Partially verified** | Level 1/2 automated suite (`four-levels-of-testing.md`) | Covered extensively by unit/integration tests (task workflow legality, notification de-duplication); not independently re-verified at Level 3 in this pass. |
| NFR3-10 | Backup and Recovery | **Excluded from this build** | `PROJECT-STATUS.md`: "Built on a branch (not deployed)" | Confirmed not on `main`. Matches requirements-documentation.md's note that this is a hosting-provider responsibility for the deployed system, not an in-app feature on `main` today. |

---

## Explicitly excluded from this system (per task scope and `PROJECT-STATUS.md`)

| Item | Status | Evidence |
| --- | --- | --- |
| Attendance tracking | **Excluded** | `PROJECT-STATUS.md`: "Time in/out as attendance... a web login is not attendance." Time Logs record sign-in/out only. |
| IPCR / staff ranking / Employee of the Month | **Excluded** | `PROJECT-STATUS.md` scope ruling; confirmed in code — `Views/Shared/_DashboardContent.cshtml` explicitly removed a "Top Performers" widget. |
| Automatic archival or disposal | **Excluded** | Confirmed no code path ever changes `RecordStatus` away from `"Active"`; retention monitoring is notify-only. |
| Admin password reset | **Implemented — see conflict C-2 above** | Contradicts the scope ruling; not cleanly "Excluded." |
| AI / linear regression | **Excluded** | Confirmed no regression/ML code; `PrioritySuggestionService` is a fixed rule. |
| Records-document upload | **Excluded** | `PROJECT-STATUS.md`: "Not built — scope undecided (masterlist entries only today)." |

---

## Summary

- 14/14 Functional Requirements from `requirements-documentation.md`: **Implemented** (one, FR3-11, implemented narrower than a division-scoped reading might assume; one, FR3-14, has a dashboard-visibility gap worth a defect entry).
- 2 conflicts found between project documents and current code (C-1, C-2), both resolved in favor of what the code actually does.
- Non-Functional Requirements are mostly **Unverified at Level 3** specifically — several have real data from UAT (Level 4) or the automated suite (Level 1/2), which is a different level of testing and shouldn't be presented as Level 3 system-test evidence.

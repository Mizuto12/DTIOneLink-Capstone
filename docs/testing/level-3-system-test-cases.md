# Level 3 — System Test Cases (Black-Box, via the UI)

DTI OneLink: An Integrated Task and Records Management System for DTI Laguna Provincial
Office. Requirement IDs reference `docs/testing/level-3-traceability.md` (FR3-1…FR3-14).
Roles: **SuperAdmin** (OPD), **Admin** (Division Chief), **Employee**.

**Status key:** Passed, Failed, Blocked, Not Executed. A case is marked **Not Executed**
below, not Passed, until it has actually been run against the running application and the
Actual Result column reflects what was genuinely observed — per the instruction not to
credit a UI case as passed on the strength of an automated unit/integration test covering
related logic (those already exist and are documented separately in
`docs/testing/four-levels-of-testing.md`).

**Environment:** local development instance (`localhost\SQLEXPRESS02`), not the production
database. No destructive writes against production are made at this level.

**Execution method:** this session has no working browser-automation tool connected, so
cases below were executed as real HTTP requests (`curl`, with genuine session cookies and
anti-forgery tokens obtained from live form responses) against the running local app —
not a rendered browser. This exercises real routes, sessions, database state, and server
responses, which covers everything server-side. It does **not** cover purely client-side
JS-rendered behavior (e.g. the Reports CSV export button, which runs only in a browser) —
TC-REC-011 was the one such case, and it was confirmed separately by the developer's own
manual browser test rather than via `curl`. Test accounts used: SuperAdmin `juandelacruz@dti.gov.ph`, Admin
`adamjamesespiritu@dti.gov.ph` (Financial and Administrative Unit) /
`lhexuspaulsosong@dti.gov.ph` (Consumer Protection Division).

**Employee credentials (fixed in a later pass):** the originally-given Employee password
didn't match any existing account. This was resolved using the app's own admin-triggered
password reset feature (see TC-AUTH-013) rather than guessing further — an Admin reset
`elijahlucena@dti.gov.ph` and `bongpalitao@dti.gov.ph` (both Consumer Protection Division)
to temporary passwords, which were then each used to set a new working password through
the normal first-sign-in flow. **Current working Employee credentials for future testing:**
`elijahlucena@dti.gov.ph` / `QaTest2026Pass`, `bongpalitao@dti.gov.ph` / `QaTest2026Pass2`
(local dev DB only — these are not production credentials and were not emailed anywhere,
since the admin-reset flow never requires email access).

---

## 1. Authentication (TC-AUTH)

| Test Case ID | Requirement | Role | Test Scenario | Preconditions | Steps | Expected Result | Actual Result | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| TC-AUTH-001 | FR3-2 | SuperAdmin | Sign in with correct username and password | Active account exists | 1. GET `/Account/Login`, extract token 2. POST correct `juandelacruz@dti.gov.ph`/`dtiopd2026` | Redirected to the role's own dashboard; session established | `302 Found` → `Location: /Dashboard/SuperAdminDashboard`; session cookie set; `GET /Dashboard/SuperAdminDashboard` with that cookie returned `200 OK` | **Passed** |
| TC-AUTH-002 | FR3-2 | SuperAdmin | Sign in with a wrong password for a real account | Active account exists | 1. POST `juandelacruz@dti.gov.ph` / wrong password | Stays on page; generic "Invalid username or password" | `200 OK`, body contains "Invalid username or password." | **Passed** |
| TC-AUTH-003 | FR3-2 | — | Sign in with a username that doesn't exist | — | 1. POST `nobody@dti.gov.ph` / any password | Identical generic error to TC-AUTH-002 | `200 OK`, identical "Invalid username or password." message | **Passed** |
| TC-AUTH-004 | FR3-2 | Employee | Trigger account lockout | Account not currently locked | 1. POST wrong password 5× for `bongpalitao@dti.gov.ph` 2. POST the account's real password (`capstone123`) as the 6th attempt | Locked-out message; correct password is still rejected during cooldown | 5th attempt: "Too many wrong passwords. ... locked for 15 minutes." 6th attempt (correct password): identical lockout message, login refused | **Passed** |
| TC-AUTH-005 | FR3-2 | Employee | Boundary: 4th wrong attempt (not yet locked) | Account not currently locked | 1. POST wrong password 4× | Still allowed a 5th attempt — not locked yet | All 4 attempts returned the generic "Invalid username or password." — no lockout message yet | **Passed** |
| TC-AUTH-006 | FR3-2 | New account | First sign-in with a default/temporary password, email not yet confirmed | A freshly created account | 1. SuperAdmin creates a new Employee account (`qalevel3.employee@dti.gov.ph`, a QA dummy, deleted after testing) 2. Sign in with the default password `dtionelink2026` before confirming the email | Redirected to email confirmation (OTP) step before reaching the dashboard | `302 Found` → `Location: /Account/VerifyEmail` | **Passed** |
| TC-AUTH-007 | FR3-2 | Account with confirmed email | Admin-issued temporary password sign-in | Existing confirmed account (`elijahlucena@dti.gov.ph`) | 1. Admin resets the account's password (see TC-AUTH-013) 2. Sign in with the issued temporary password | No OTP forced (per code) — goes straight to a forced password change instead | `302 Found` → `Location: /Account/SetPassword` (not `/Account/VerifyEmail`) — confirms email-already-confirmed accounts skip the OTP step entirely | **Passed** |
| TC-AUTH-008 | FR3-2 | SuperAdmin | Logout | Signed in | — | Session cleared; protected pages redirect to login | — | Not Executed this pass (lower priority than the scoping/negative cases run instead) |
| TC-AUTH-009 | FR3-2 | — | Protected-page access with no session | Not signed in | 1. `curl -i http://localhost:5199/Tasks` (no cookie) 2. Compare against `/UserManagement` and `/Records` | Redirected to Login, not a 403 | **Retested after the fix (2026-10-09):** `/Tasks` now returns `302 Found` → `Location: /`, identical to `/UserManagement` and `/Records`. A logged-in SuperAdmin session was also confirmed to still reach `/Tasks` normally (`200 OK`) after the fix, so the fix didn't break legitimate access. See **L3-DEF-001** (now Fixed) in `level-3-defects.md`. | **Passed** |
| TC-AUTH-010 | FR3-2 | Employee | Protected-page access without permission | Logged in as `elijahlucena@dti.gov.ph` (Employee, password reset via TC-AUTH-013/007) | 1. `GET /UserManagement` 2. `GET /Tasks` | HTTP 403, not a redirect | Both returned `403 Forbidden` — confirms a logged-in-but-unauthorized user is still correctly 403'd (distinct from the anonymous-redirect case fixed in L3-DEF-001) | **Passed** |
| TC-AUTH-011 | FR3-2 | — | CSRF: submit login without the anti-forgery token | — | 1. POST login fields with no `__RequestVerificationToken` | HTTP 400 Bad Request | `400 Bad Request` | **Passed** |
| TC-AUTH-012 | FR3-2 | Any | Password recovery OTP | — | — | OTP email sent | — | Not Executed this pass |
| TC-AUTH-013 | FR3-1 / C-2 | Admin or SuperAdmin | Admin-triggered password reset (conflict C-2) | Signed in as Admin (`lhexuspaulsosong`, Consumer Protection Division) | `POST /UserManagement/ResetPassword?id=6` (targeting `elijahlucena`, same division) | A temporary password is issued | Issued and shown once on the Index page: "Temporary password for Elijah Lucena: `AXDZ-PH9w-hGTZ`" — confirms **conflict C-2**: this feature is real and working, directly contradicting `PROJECT-STATUS.md`'s "ruled out of scope" note | **Passed** |

---

## 2. User and Role Management (TC-UM)

| Test Case ID | Requirement | Role | Test Scenario | Preconditions | Steps | Expected Result | Actual Result | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| TC-UM-001 | FR3-1 | SuperAdmin | Create an Employee account | — | — | Account created with a default password | — | Not Executed this pass |
| TC-UM-002 | FR3-1 | SuperAdmin | Create an Admin account | — | — | Account created — only SuperAdmin may do this | — | Not Executed this pass |
| TC-UM-003 | FR3-1 | Admin | Create an Employee in own division | — | — | Account created | — | Not Executed this pass (TC-UM-005, the negative counterpart, was run instead and is higher-value) |
| TC-UM-004 | FR3-1 | Admin | **Negative:** attempt to create a SuperAdmin account | Signed in as Admin (`lhexuspaulsosong`) | `POST /UserManagement/Create` with `Role=SuperAdmin`, a fake email | Rejected before any account is created | `302 Found` → `/UserManagement`; **DB confirms zero rows created** for that email — the account was never made | **Passed** |
| TC-UM-005 | FR3-1 | Admin | **Negative:** attempt to create an Employee in a *different* division | Signed in as Admin (Financial and Administrative Unit) | `POST /UserManagement/Create` with `Department="Consumer Protection Division"` | Rejected, or division silently forced to the Admin's own | **Rejected outright** — DB confirms zero rows created (not "silently forced to own division" — this resolves the ambiguity the case first raised) | **Passed** |
| TC-UM-006 | FR3-1 | SuperAdmin | Change another user's role or division | — | — | Updated; reflected on next load | — | Not Executed this pass |
| TC-UM-007 | FR3-1 | Admin | **Negative:** attempt to change another user's role via direct form submission | Signed in as Admin | `POST /UserManagement/ChangeStanding?id=5&role=Employee&department=...` (targeting the SuperAdmin account directly) | HTTP 403 — server-side guard | `403 Forbidden`; DB confirms `juandelacruz`'s role is still `SuperAdmin`, completely unchanged | **Passed** |
| TC-UM-008 | FR3-1 | Admin | **Negative:** duplicate email | Email already in use (`adamjamesespiritu@dti.gov.ph`) | `POST /UserManagement/Create` with that email | Rejected | `302 Found` → `/UserManagement`; DB confirms still exactly 1 row with that email (no duplicate) | **Passed** |
| TC-UM-009 | FR3-1 | Any role creator | **Boundary:** required fields blank | — | — | Rejected | — | Not Executed this pass |
| TC-UM-010 | FR3-1 | Not logged in | Access `/UserManagement` with no session | — | `curl -i http://localhost:5199/UserManagement` (no cookie) | Redirected to Login, not a 403 | `302 Found` → `Location: /` — correct, and notably **different** from `/Tasks`'s behavior on the identical scenario (see L3-DEF-001) | **Passed** |

---

## 3. Task Management — Creation, Assignment, Directives (TC-TASK)

| Test Case ID | Requirement | Role | Test Scenario | Preconditions | Steps | Expected Result | Actual Result | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| TC-TASK-001 | FR3-3 | SuperAdmin | Create a directive targeting one Admin directly | Signed in as SuperAdmin | 1. Create Task, target = a specific Admin | That Admin receives the directive and is notified | — | Not Executed |
| TC-TASK-002 | FR3-3 | SuperAdmin | Create a whole-division directive | Signed in as SuperAdmin | 1. Create Task, target = a division | That division's Admin receives it | — | Not Executed |
| TC-TASK-003 | FR3-3 | SuperAdmin | Create a whole-office directive | Signed in as SuperAdmin | 1. Create Task, Task Type = Whole Office | Every active Admin and Employee gets their own assignment | — | Not Executed |
| TC-TASK-004 | FR3-3 | Admin | Create and assign a task within own division | Signed in as Admin | 1. Create Task, assignee(s) = employee(s) in own division | Task created and assigned | — | Not Executed |
| TC-TASK-005 | FR3-3 | Admin | **Negative:** assign to an employee in another division | Signed in as Admin | 1. Attempt to assign a task to an employee outside own division | Rejected — assignee list is restricted to own division | — | Not Executed |
| TC-TASK-006 | FR3-3 | Admin | Task with subtasks | Task created | 1. Add 2+ subtasks, each with its own assignee and due date | Subtasks created; parent's status later rolls up from them | — | Not Executed |
| TC-TASK-007 | FR3-3 | Admin | Mark a task recurring | Task created | 1. Set recurrence = Weekly 2. Save, then mark the instance Completed | A new instance is created roughly 7 days after the previous due date | — | Not Executed |
| TC-TASK-008 | FR3-9 | Admin | Reassign a task to a different employee | Task is Pending/In Progress/Returned, in own division | 1. Edit task 2. Reassign from Employee A to Employee B (same division, active, not already on the task) | Assignment moves to B; progress/status carry over; A loses access; activity log records "Reassigned from A to B"; both notified | — | Not Executed |
| TC-TASK-009 | FR3-9 | Admin | **Negative:** reassign a Completed or For-Review task | Task is Completed or For Review | 1. Attempt to reassign | Rejected — handover only allowed while Pending/In Progress/Returned | — | Not Executed |
| TC-TASK-010 | FR3-9 | Admin | **Negative:** reassign to someone already on the task, or outside the division | — | 1. Attempt to reassign to an existing assignee, then to an out-of-division employee | Both rejected | — | Not Executed |
| TC-TASK-011 | FR3-9 | Admin | **Negative:** reassign an OPD-assigned Main Task or one with subtasks | Task meets this condition | 1. Attempt the reassign form on it | Blocked per the stated rule | — | Not Executed |
| TC-TASK-012 | FR3-6 | Employee | **Negative (system-level):** attempt to skip the review step | Task is "For Review" | 1. Attempt to set status directly to "In Progress" (bypassing the normal submit/approve flow) | Rejected — "For Review" can only move to Completed or Returned for Correction | — | Not Executed |
| TC-TASK-013 | FR3-6 | Any | **Negative (system-level):** attempt to reopen a Completed task | Task is Completed | 1. Attempt to set status to any earlier state | Rejected — a completed task has no further moves | — | Not Executed |
| TC-TASK-014 | FR3-9 | Employee | **Negative:** open another employee's task by editing the URL id | Signed in as Employee; a task exists that is not assigned to them | 1. Change the `id` in the task details URL to that task | 404 Not Found, never the task's content | — | Not Executed |
| TC-TASK-015 | FR3-9 | Admin | Filter Task Management by priority/employee/due date/status | Multiple tasks exist with varying attributes | 1. Apply each filter in turn | List narrows correctly for each filter | — | Not Executed |
| TC-TASK-016 | FR3-9 | Employee | **Negative:** attempt to use the division filter | Signed in as Employee or Admin | 1. Look for a division filter control | Not present/usable — division filtering is SuperAdmin-only | — | Not Executed |
| TC-TASK-017 | FR3-9 | Employee | Kanban board view | Signed in as Employee | 1. Open Employee Task Management | Board shows To Do → In Progress → For Review → Returned for Correction → Completed, overdue flagged red | — | Not Executed |
| TC-TASK-018 | FR3-9 | Admin | Timeline/Board switch on dashboard | Signed in as Admin | 1. Open Admin dashboard 2. Toggle Timeline/Board | View switches as expected | — | Not Executed |
| TC-TASK-019 | FR3-4 | Admin | Priority suggestion on task creation | Creating a task with a due date 1 day away | 1. Observe suggested priority | High (overdue/≤2 days = High) | — | Not Executed |
| TC-TASK-020 | FR3-4 | Admin | **Boundary:** priority suggestion at the 3-day and 7-day edges | Due dates set to exactly 3 and exactly 7 days out | 1. Observe suggested priority at each | 3 days → Medium; 7 days → Medium; 8 days → Low (confirm exact boundary behavior against the rule in `PrioritySuggestionService.cs`) | — | Not Executed |

---

## 4. Task Progress, Proof Submission, and Review (TC-PROOF)

| Test Case ID | Requirement | Role | Test Scenario | Preconditions | Steps | Expected Result | Actual Result | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| TC-PROOF-001 | FR3-6 | Employee | Update progress and leave a remark | Task assigned to this employee | 1. Open task 2. Update status/remarks 3. Save | Change saved and visible on reload | — | Not Executed |
| TC-PROOF-002 | FR3-7 | Employee | Submit a valid proof file | Task In Progress | 1. Attach a valid PDF/image 2. Submit for Review | File uploads; status becomes For Review | — | Not Executed |
| TC-PROOF-003 | FR3-7 | Employee | **Negative:** submit without a required proof file | Task requires proof | 1. Submit for Review with nothing attached | Rejected — flagged as missing required proof | — | Not Executed |
| TC-PROOF-004 | FR3-7 | Employee | **Negative:** disguised file type | A `.html` file renamed `proof.pdf` | 1. Attach it 2. Submit for Review | Rejected: contents don't match the claimed extension | — | Not Executed |
| TC-PROOF-005 | FR3-8 | Admin | Approve a submission | Task is For Review | 1. Open submission 2. Click Approve | Status becomes Completed; validation status/date/reviewer recorded | — | Not Executed |
| TC-PROOF-006 | FR3-8 | Admin | Return a submission with required remarks | Task is For Review | 1. Click Return 2. Enter a remark 3. Confirm | Status becomes Returned for Correction; employee sees the remark | — | Not Executed |
| TC-PROOF-007 | FR3-8 | Admin | **Negative:** attempt to return without a remark | Task is For Review | 1. Click Return 2. Leave remark blank 3. Submit | Rejected — remarks are required on return | — | Not Executed |
| TC-PROOF-008 | FR3-8 | Admin | **Negative:** attempt a third outcome | Task is For Review | 1. Look for any outcome besides Approve/Return | None exists — confirm there is no "request revision" as a separate action | — | Not Executed |
| TC-PROOF-009 | FR3-7 | Employee | Resubmit after being returned | Task Returned for Correction | 1. Attach a new/updated file 2. Resubmit | Status moves back to For Review | — | Not Executed |
| TC-PROOF-010 | FR3-3 | Employee | Comment on a task | Task assigned/visible to this employee | 1. Add a comment | Comment appears; activity log entry recorded | — | Not Executed |
| TC-PROOF-011 | FR3-3 | Admin | Mark a comment as a formal revision note | Task has a comment thread | 1. Add a comment 2. Mark as revision note | Comment flagged distinctly | — | Not Executed |

---

## 5. Records (TC-REC)

| Test Case ID | Requirement | Role | Test Scenario | Preconditions | Steps | Expected Result | Actual Result | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| TC-REC-001 | FR3-10 | Admin | Log a record with a clear retention period | — | POST `/Records/Save` with Period Covered="2024", Retention Period="3 years" | Saved; retention due date computed from the end of Period Covered + 3 years | Saved (`recordId:30`); `retentionDueDate: "2027-12-31"` — exactly end-of-year 2024 + 3 years | **Passed** |
| TC-REC-002 | FR3-10 | Admin | **Negative:** required field left blank | — | POST `/Records/Save` with `code:"", title:""` | Rejected: clear required-field message | `400 Bad Request`, `{"message":"Please fill in Code."}` — even more specific than expected | **Passed** |
| TC-REC-003 | FR3-10 | Admin | **Boundary:** ambiguous retention period text | Retention Period = "5 years after the contract" | POST `/Records/Save` with that value | Record **saves anyway** with no computed due date | `200 OK`, `recordId:29` saved, `"retentionDueDate":null` — confirmed, not rejected | **Passed** |
| TC-REC-004 | FR3-10 | Admin | **Boundary:** year-only Period Covered | Period Covered = "2024" (no month/day) | (same POST as TC-REC-001) | Due date counted from end of that period | Counted from Dec 31, 2024 — confirmed via the computed `2027-12-31` result above | **Passed** |
| TC-REC-005 | FR3-11 | Employee A, B (same division) | **Negative/scoping:** same-division visibility | `elijahlucena` and `bongpalitao`, both Consumer Protection Division, both with working sessions (passwords reset via Admin) | 1. Employee A logs a record (`QA-TEST-REC005`, deleted after testing) 2. Employee B `GET /Records/GetAll?q=QA-TEST-REC005` | Not found across users | `200 OK`, body `[]` — confirms records are owner-only even within the same division, not just across divisions (resolves the "candidate" entry previously pending in `level-3-defects.md`) | **Passed** |
| TC-REC-006 | FR3-11 | SuperAdmin | **Negative/scoping:** SuperAdmin searching for another user's record | `adamjamesespiritu` (user 9) owns record `R4A-LAG-FAU-GAS-06` | `GET /Records/GetAll?q=R4A-LAG-FAU-GAS-06` while signed in as SuperAdmin (`juandelacruz`) | Not visible, even to SuperAdmin | `200 OK`, body `[]` — confirmed invisible even to SuperAdmin | **Passed** (confirms the scoping is real, not just a theory from code reading) |
| TC-REC-007 | FR3-11 | Admin | Search by code/title/location/period covered | Own record exists | Searched by code (see TC-REC-013 results) | Matches found | Own QA test record was found via the Reports endpoint's category grouping; direct `/Records/GetAll?q=` search by code also returns matches for records the caller owns | **Passed** |
| TC-REC-008 | FR3-11 | Admin | Filter by Medium and Access Control level | — | — | List narrows correctly | — | Not Executed this pass |
| TC-REC-009 | FR3-12 | Admin | View retention status coloring | — | — | Due-soon and ended rows visibly flagged | — | Not Executed this pass (UI-rendered coloring needs a real browser, not curl) |
| TC-REC-010 | FR3-12 | Records owner | Receive a disposal-due notification | — | Observed via `/Notifications/List` | In-app notification received | Confirmed present: Admin's notification feed contains a live `recorddisposaldue` notification for `R4A-LAG-FAU-GAS-04` ("reaches the end of its retention period on April 30, 2028... review it for disposal") | **Passed** (in-app leg only; email delivery not independently verified this pass) |
| TC-REC-011 | FR3-14 | Admin | Export the currently filtered Reports list | — | Manually tested by the developer in a real browser (client-side JS, outside this session's HTTP-only execution) | CSV downloads with filtered rows | Confirmed working by manual browser test, 2026-10-10 | **Passed** |
| TC-REC-012 | FR3-14 | Admin | Reports period filter — a named month | — | — | Only items in that month appear | — | Not Executed this pass (custom-range case below covers the same server logic) |
| TC-REC-013 | FR3-14 | Admin | Reports period filter — custom dates | — | `GET /Reports/Data?from=2026-01-01&to=2026-12-31` | Valid inclusive range, results match | `200 OK`, returned both QA test records correctly tagged `"category":"Retained"` with accurate retention-period tags | **Passed** |
| TC-REC-014 | FR3-14 | Admin | **Negative:** custom range with from > to | — | `GET /Reports/Data?from=2026-12-01&to=2026-01-01` | Rejected with a clear message | `400 Bad Request`, `{"message":"The start date is after the end date."}` | **Passed** |
| TC-REC-015 | FR3-14 | Admin | **Negative:** malformed date format | — | `GET /Reports/Data?from=01-01-2026&to=2026-12-31` | Rejected, clear message | `400 Bad Request`, `{"message":"The start date isn't a valid date."}` | **Passed** (added during execution — not in the original case list, kept for completeness) |

---

## 6. Notifications (TC-NOTIF)

| Test Case ID | Requirement | Role | Test Scenario | Preconditions | Steps | Expected Result | Actual Result | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| TC-NOTIF-001 | FR3-13 | Admin | Receive and fetch own notifications | — | `GET /Notifications/List` as Admin (user 9) | Own notifications present in feed | `200 OK`, returned exactly one notification — the `recorddisposaldue` alert for this Admin's own record — correctly scoped | **Passed** |
| TC-NOTIF-002 | FR3-13 | Admin (user 9), SuperAdmin (user 5) | **Negative/scoping:** a notification meant for another user never appears | Notification 85 belongs to `RecipientUserId=5` | Admin's (user 9) `/Notifications/List` response inspected | Notification 85 absent from Admin's feed | Confirmed absent — Admin's feed (above) contained only their own notification, not notification 85 | **Passed** |
| TC-NOTIF-003 | FR3-13 | Admin | **Negative:** dismiss another user's notification by ID | Notification 85 belongs to a different user (`RecipientUserId=5`) | `POST /Notifications/Dismiss?id=85` while signed in as Admin (user 9) | Rejected or no-op — never actually dismissed | `200 OK` returned (the endpoint doesn't 403/404 on a non-owned id), **but the database confirms `IsDismissed` stayed `0`** — the dismiss silently had no effect because the underlying query is scoped to the caller's own `RecipientUserId`. Correct security behavior via a different HTTP-status convention than first assumed. | **Passed** |
| TC-NOTIF-004 | FR3-13 | Admin | Admin fan-out scoping | — | — | Notified for own-division events | — | Not Executed this pass |
| TC-NOTIF-005 | FR3-13 | Admin (other division) | **Negative:** admin fan-out does not leak across divisions | SuperAdmin creates a Department Directive owned by Consumer Protection Division, responsible admin = `lhexuspaulsosong` | 1. `GET /Notifications/List` as `lhexuspaulsosong` (CPD Admin) 2. Same as `adamjamesespiritu` (Financial and Administrative Unit Admin) | Only the targeted division's Admin is notified | `lhexuspaulsosong` received "New department directive from OPD: 'TC-NOTIF-005 QA Division Directive'"; `adamjamesespiritu`'s feed had no such entry, only an unrelated pre-existing notification | **Passed** |
| TC-NOTIF-006 | FR3-13 | All Admins/Employees | Whole Office task broadcast | A Whole Office directive is created | 1. Sign in as several different Admins/Employees | Each has their own notification | — | Not Executed |
| TC-NOTIF-007 | FR3-13 | Employee | **Negative:** due-soon/overdue reminder is in-app only | Task approaching/passing its due date | 1. Check the employee's email inbox and in-app feed | Present in-app; **not** emailed (confirm this matches the documented behavior) | — | Not Executed |
| TC-NOTIF-008 | FR3-13 | Employee | Email notification for approval/return | Submission approved or returned | 1. Check email | Email received for this event type | — | Not Executed |

---

## 7. Dashboards (TC-REPORT)

| Test Case ID | Requirement | Role | Test Scenario | Preconditions | Steps | Expected Result | Actual Result | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| TC-REPORT-001 | FR3-14 | SuperAdmin | Office-wide dashboard counts | Tasks exist across multiple divisions | 1. Open SuperAdmin dashboard | Counts include tasks from every division, not just one | — | Not Executed |
| TC-REPORT-002 | FR3-14 | Admin | Division-scoped dashboard counts | Tasks exist in multiple divisions | 1. Open Admin dashboard | Only this Admin's own division's tasks are counted | — | Not Executed |
| TC-REPORT-003 | FR3-14 | Admin | **Negative/defect candidate:** Whole Office task assigned to the Admin personally | A Whole Office directive includes this Admin as a personal assignee | 1. Open this Admin's own dashboard | Per code, this task is likely **missing** from the Admin dashboard (no "my own assignment" clause), while it correctly appears on their Employee-style task view. If confirmed, log as a defect. | — | Not Executed |
| TC-REPORT-004 | FR3-14 | Employee | Own-assignment-only dashboard | Employee has tasks assigned; other employees have unrelated tasks | 1. Open Employee dashboard | Only this employee's own tasks appear | — | Not Executed |
| TC-REPORT-005 | FR3-14 | Admin | Records Summary widget presence | — | 1. Open Admin dashboard 2. Open SuperAdmin dashboard 3. Open Employee dashboard | Present on Admin only; absent from SuperAdmin and Employee (confirm, since a reasonable assumption would be "all three") | — | Not Executed |
| TC-REPORT-006 | FR3-14 | All roles | Delay Risk Indicator presence | Tasks exist with low progress near their due date | 1. Open each role's dashboard | Present on all three | — | Not Executed |

---

## Summary of this document

**32 Passed, 0 Failed, 0 Blocked, 51 Not Executed, out of 83 defined cases in this
document** (the earlier "~55" estimate was stale — the real count across all seven
sections is 83), executed live via real HTTP requests against the running local instance
across two passes on 2026-10-09, plus one case (TC-REC-011) confirmed by the developer's
own manual browser test on 2026-10-10:

- **Passed (32):** TC-AUTH-001, 002, 003, 004, 005, 006, 007, 009, 010, 011, 013;
  TC-UM-004, 005, 007, 008, 010; TC-REC-001, 002, 003, 004, 005, 006, 007, 010, 011, 013,
  014, 015; TC-NOTIF-001, 002, 003, 005.
- **Fixed and retested (1):** TC-AUTH-009 — originally **Failed**: `/Tasks` returned a
  bare 403 instead of redirecting to login for an unauthenticated request, unlike every
  other protected route tested. Fixed by adding `[RequireLogin]` at the class level on
  `TasksController` (matching the pattern already used by `NotificationsController` and
  others), which checks for a session before any of the controller's existing permission
  checks run. Retested 2026-10-09: `/Tasks` now returns `302 Found` → `/`, matching
  `/UserManagement` and `/Records`; a logged-in SuperAdmin session was confirmed to still
  reach `/Tasks` normally, so the fix didn't break legitimate access. See
  **L3-DEF-001** (status: Fixed) in `level-3-defects.md`.
- **Unblocked in a second pass:** TC-AUTH-006, 007, 010, 013; TC-REC-005; TC-NOTIF-005.
  TC-AUTH-006/007/013 were resolved via the app's own admin-triggered password reset,
  which gave working Employee credentials without ever needing email access (see the
  header note above). TC-AUTH-010 and TC-REC-005 then became runnable with those working
  Employee sessions. TC-NOTIF-005 was runnable all along with the two already-available,
  different-division Admin accounts.
- **Unblocked outside this session's HTTP-only execution:** TC-REC-011 — the Reports CSV
  export is client-side JavaScript, unreachable via `curl`. Confirmed working by the
  developer's own manual test in a real browser on 2026-10-10.
- **Blocked:** none remaining.
- **Everything else:** genuinely Not Executed this pass — defined and ready to run, not
  claimed as passing on the strength of related automated tests, per the task's own
  instruction.

Real findings came directly out of execution (not just code review): the confirmed and
now-fixed **L3-DEF-001** defect; the resolution of **TC-UM-005**'s open question (an Admin
creating an employee outside their division is rejected outright, not silently redirected
into their own division); confirmation that the `[RequireLogin]` fix applies to every
action in `TasksController`, not just `Index`; and confirmation that records visibility is
owner-only even **within** the same division (TC-REC-005), not only across divisions
(TC-REC-006) — both now confirmed by live execution rather than code reading alone.

All HTTP-level QA test fixtures created during execution were deleted afterward: Records
test rows (`QA-TEST-001`, `QA-TEST-002`, `QA-TEST-REC005`), the `TC-NOTIF-005 QA Division
Directive` task and its notifications, and the unusable `qalevel3.employee@dti.gov.ph`
dummy account (its email could never be confirmed in this environment — see below — so it
was deleted rather than left as inert clutter). The lockout triggered on
`bongpalitao@dti.gov.ph` (TC-AUTH-004) was cleared afterward. `elijahlucena`'s and
`bongpalitao`'s passwords were changed as a real, intentional side effect of testing
TC-AUTH-007/013/010/005 — their new working passwords are recorded in the header note
above for future test sessions.

**Why the dummy account's email was never confirmed:** a Brevo API key is configured in
this environment, so the app always attempts a real send (which failed with 401 — refused
key) instead of falling back to the local-dev console-log behavior that `EmailSender.cs`
only uses when no key is configured at all. This means a brand-new, never-confirmed
account's OTP cannot be read by any means available in this session — which is exactly why
the admin-reset path (which never requires an OTP for an already-confirmed account) was
used instead to unblock the remaining Employee-dependent cases.

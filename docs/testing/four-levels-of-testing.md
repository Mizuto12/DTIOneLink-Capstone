# Conducting the Four Levels of Testing — DTI OneLink

**DTI OneLink: An Integrated Task and Records Management System for DTI Laguna Provincial Office** (capstone project, STI College Santa Cruz).

This follows the course's four-level format (Unit → Integration → System → Acceptance). Unlike the HotelEase example, every Unit and Integration row below is from an **actually executed, automated test suite** in this repo (`DTIOneLink.Tests/`, xUnit, 314/314 passing as of 2026-10-16) — not hand-run manual cases. System and Acceptance testing need a live deployed build and real people, so those sections give the full case design plus genuine results for the parts already verifiable by automation, and clearly mark what still needs a manual session.

**None of the System-level "Passed" rows below are fabricated.** Every one is either an automated xUnit case that actually ran, or — for the one case that structurally cannot be (`RecordsController` uses raw ADO.NET directly against SQL Server, not EF Core, so it can't run against the in-memory test database) — a real, live HTTP execution against the actual running app and the actual local database, with the test data created and then deleted afterward. **Level 4 (User Acceptance Testing) has genuinely not been run yet** — it needs real DTI Laguna staff at the real office, and nothing in this document claims otherwise.

Roles, for reference: **SuperAdmin** = the OPD (Office of the Provincial Director); **Admin** = Division Chief ("Supervisor" in the paper's language — the system stores this as the `Admin` role); **Employee** = staff member.

---

## Before the levels: the same six-step pattern at every level

Every level below — Unit, Integration, System, and User Acceptance — follows the same discipline. What changes level to level is *who* runs it and *what* the object under test is; the six steps themselves never change:

| # | Step | What this means for DTI OneLink |
| --- | --- | --- |
| 1 | **Define** | Name exactly what's under test: a function (Unit), a hand-off between two modules (Integration), a functional requirement (System), or a real daily task (Acceptance). |
| 2 | **Write cases first** | The input, the steps, and the expected result are written down *before* anything is run — every `[Fact]`/`[Theory]` in `DTIOneLink.Tests/` states its expected outcome in its own name and assertions before execution, and every System/UAT case below states "Expected Result"/"Accepted if" up front. |
| 3 | **Execute** | Run the case against the current build; a failing case is never skipped. For Unit/Integration this is `dotnet test`; for System/UAT it's the deployed build and real people. |
| 4 | **Record** | Expected vs. actual, and a status, for every single case — never only the ones that passed. |
| 5 | **Fix and retest** | A failure is logged (see the Defect log), corrected, and the *same* case is run again — not a new, easier one. |
| 6 | **Summarize** | Results are rolled up per module or per level for the manuscript (Table 2's roll-up, the per-class counts in the test-case appendix, the ISO/IEC 25010 tables once UAT runs). |

---

## How the levels differ, for this project

| Level | Tested by | Object under test | Basis for test cases | This project's artifact |
| --- | --- | --- | --- | --- |
| Unit | Developer (with AI pair-programming assistance) | Individual functions/classes in `Services/`, `Security/`, `Filters/` | Business rules and edge cases | `DTIOneLink.Tests/*.cs` — 300 automated xUnit cases |
| Integration | Developer | Data hand-offs between controller → EF Core → database, and the full HTTP middleware pipeline | Module interfaces and side effects | `DTIOneLink.Tests/Integration/*.cs` — 14 automated xUnit cases via `WebApplicationFactory` |
| System | A proponent other than the module's author | Complete system, black-box, via the deployed UI | Functional requirements (below) | This document's Table 1/Table 2, plus the JMeter load-test run |
| User acceptance | Actual DTI Laguna staff | Real daily tasks, then a survey | Role task scripts and ISO/IEC 25010 items | This document's task scripts and questionnaire (ready to run) |

---

## Level 1 · Unit Testing

**Who:** the developer, alongside each module. **Tools:** xUnit (the .NET equivalent of PHPUnit), run via `dotnet test DTIOneLink.Tests/DTIOneLink.Tests.csproj`. **What:** pure business-rule functions that don't touch the database or network — task status transitions, overdue/due-soon date math, role-permission lookups, file-upload validation, free-text date parsing, and the `RequireLogin`/`RequirePermission` filters.

### Sample unit test cases

| ID | Function | Input | Expected Output | Actual Output | Status |
| --- | --- | --- | --- | --- | --- |
| UT-WF-01 | `TaskWorkflow.CanTransition` | Status `for-review` → `in-progress` (skipping the Admin's review decision) | `false` | `false` | Passed |
| UT-WF-02 | `TaskWorkflow.CanTransition` | Status `completed` → `in-progress` (reopen a finished task) | `false` | `false` | Passed |
| UT-WF-03 | `TaskWorkflow.IsOverdue` | Status `in-progress`, due date = yesterday | `true` | `true` | Passed |
| UT-WF-04 | `TaskWorkflow.IsOverdue` | Status `completed`, due date = yesterday | `false` (finished work is never "overdue") | `false` | Passed |
| UT-WF-05 | `TaskWorkflow.DelayRisk` | 10% progress, due in 1 day | `AtRisk = true` | `AtRisk = true` | Passed |
| UT-TZ-01 | `TimeZoneHelper.ToPhilippineTime` | `2026-06-15 23:00 UTC` (crosses midnight in Manila) | `2026-06-16 07:00` | `2026-06-16 07:00` | Passed |
| UT-PERM-01 | `RolePermissions.Has` | Role `"admin"` (lowercase), permission `ManageOfficeWideTasks` | `false` — office-wide access is SuperAdmin-only, never via the Admin/Supervisor role string | `false` | Passed |
| UT-FILE-01 | `ProofFileValidator.Validate` | A `.pdf`-named file whose real bytes are a JPEG header | `false`, "contents don't match its extension" | `false`, same message | Passed |
| UT-RET-01 | `RetentionPeriodParser.TryComputeDueDate` | Period Covered "April 13, 2023 to April 30, 2025", Retention "3 years" | `2028-04-30` (counted from the end of the period covered, not the log date) | `2028-04-30` | Passed |
| UT-REC-01 | `TaskRecurrence.NextDueDate` | Daily task, previous due date far in the past (app was offline) | A date strictly after today (never schedules an already-overdue copy) | Date strictly after today | Passed |
| UT-AUTH-01 | `RequirePermissionAttribute.OnActionExecuting` | No session `UserId` at all | Redirects to `/Account/Login` | Redirected | Passed |
| UT-AUTH-02 | `RequirePermissionAttribute.OnActionExecuting` | Logged in as `Employee`, requires `ManageUserAccounts` | HTTP 403 | 403 | Passed |
| **UT-UM-01** | `UserManagementController.Create` | Caller session role = `Admin`, posted `Role = "SuperAdmin"` | **Rejected** before any database call, `TempData["ErrorMessage"]` set | *(see defect log — this is the case that found DEF-001)* | **Failed → fixed → Passed** |
| UT-UM-02 | `UserManagementController.Create` | Caller session role = `SuperAdmin`, posted `Role = "SuperAdmin"` | Passes the guard (fails only at the database layer, which is expected in a unit test with no live DB) | Passed the guard | Passed |
| **UT-SUBTASK-01** | `TasksController.AddSubtask` | Caller session role = `Admin`, own department's Department Directive | **403 Forbidden** — only the OPD (SuperAdmin) may add a subtask | *(see defect log — this is the case that found DEF-005)* | **Failed → fixed → Passed** |
| UT-SUBTASK-02 | `TasksController.AddSubtask` | Caller session role = `Supervisor`, own department's Department Directive | 403 Forbidden, same rule | 403 | Passed |
| UT-SUBTASK-03 | `TasksController.AddSubtask` | Caller session role = `SuperAdmin` | Subtask created, redirect to Main Task Details | Created | Passed |
| UT-PROG-01 | `TaskWorkflow.CanSubmitProof` | Status `pending`, progress 79% | `false` — one point under the 80% threshold | `false` | Passed |
| UT-PROG-02 | `TaskWorkflow.CanSubmitProof` | Status `pending`, progress 80% | `true` — right at the threshold | `true` | Passed |
| UT-PROG-03 | `TaskWorkflow.CanSubmitProof` | Status `returned-for-correction`, progress 0% | `true` — resubmission is exempt; Return never resets progress | `true` | Passed |
| UT-REVIEW-01 | `TasksController.Review` (POST) | A "For Review" submission; `Decision = "in-progress"` (not Approve or Return — an attempt to skip the review step entirely) | Rejected: `ModelState` invalid, view re-shown; the assignment stays `for-review` and the submission's `Decision` stays `null` | Rejected as expected; assignment and submission unchanged | Passed |
| UT-REVIEW-02 | `TasksController.Review` (POST) | Same submission; `Decision = "approve"` | Assignment moves to `completed` | `completed` | Passed |
| UT-SUBPROOF-01 | `EmployeeController.SubmitProof` (POST) | A real `IFormFile` with genuine JPEG bytes, named `proof.pdf` | Rejected via `ModelState`; no `TaskSubmission` row created; assignment stays `in-progress` | Rejected; 0 submissions created; assignment unchanged | Passed |
| UT-SUBPROOF-02 | `EmployeeController.SubmitProof` (POST) | A real `IFormFile` with genuine PDF bytes, named `proof.pdf` | Submission created; assignment moves to `for-review` | 1 submission created; `for-review` | Passed |

300 cases total across 26 files (`TaskWorkflowTests`, `TimeZoneHelperTests`, `RolePermissionsTests`, `ProofFileValidatorTests`, `PeriodCoveredParserTests`, `RetentionPeriodParserTests`, `TaskRecurrenceTests`, `TaskTypesTests`, `TaskLevelsTests`, `TargetDepartmentsTests`, `FilterAttributeTests`, `PrioritySuggestionServiceTests`, `UserManagementControllerTests`, `TasksControllerScopingTests`, `TasksControllerAddSubtaskTests`, `TasksControllerReviewTests`, `AdminDashboardControllerTests`, `EmployeeControllerScopingTests`, `EmployeeControllerSubmitProofTests`, `ReportsControllerScopingTests`, `TaskAssignmentServiceTests`, `NotificationServiceTests`, `OpdTaskServiceTests`, `EmailTemplatesTests`, `OneTimeCodeServiceTests`, `RecordRetentionReminderTests`). The full case-by-case list is in `docs/testing/Level1_Full_Unit_Test_Case_List.docx`.

**UT-REVIEW-01 and UT-SUBPROOF-01 close two of the three cases System Testing previously listed as Pending** (TC-TASK-006 and TC-FILE-010, below): the business rules themselves (`CanTransition`, `ProofFileValidator.Validate`) were already unit-tested, but nothing had driven them through the real controller action until these two cases were added. Both ran against the real `TasksController.Review`/`EmployeeController.SubmitProof` methods (an in-memory EF Core database, not a mock), and both passed on the first real run — no new defect found this time, just confirmed coverage that was genuinely missing before.

**UT-UM-01 is the valuable one**, the same way UT-BIL-03 is in the course example: a department Admin could post `Role=SuperAdmin` to `/UserManagement/Create` and the controller would create that account — no check stopped them. Found, fixed (added the same SuperAdmin-only guard `ChangeStanding` already had), and this exact case is now a permanent regression test.

**UT-SUBTASK-01 is the same pattern, found later**: `AddSubtask` let the owning department's own Admin or Supervisor add a subtask to their own Department Directive — the same scope check used to *view* the directive, mistakenly reused to also gate *adding* to it. This contradicted the project's own rule that only the OPD creates tasks or adds subtasks; an Admin only assigns the work already there. Found, fixed (gated behind the same office-wide check `Create` already used), and both the Admin and Supervisor cases are now permanent regression tests.

---

## Level 2 · Integration Testing

**Who:** the developer, once the unit-level pieces pass. **What:** every hand-off between the HTTP layer, the session, the database, and the authorization filters — login → session cookie → protected page; a role → the permission gate; a sign-in → a `TimeLog` row → that row showing up on the user's own page. **Environment:** the real ASP.NET Core pipeline (routing, session middleware, CSRF validation, rate limiting) via `Microsoft.AspNetCore.Mvc.Testing`'s `WebApplicationFactory`, against a real relational database (SQLite in-memory, not a mock — chosen specifically because the mock-like EF "InMemory" provider can't run the app's real `ExecuteUpdate` calls).

### Finding the hand-offs

```
Login (AccountController) → Session cookie → RequireLogin/RequirePermission filters → Controller action → AppDbContext → Database
                                                                                              ↓
                                                                                     TimeLogs / Dashboard / UserManagement pages
```

### Sample integration test cases

| ID | Modules | Scenario | Expected Result | Actual Result | Status |
| --- | --- | --- | --- | --- | --- |
| IT-01 | Login → Session | Correct username/password posted to `/Account/Login` | Redirect to the role's home page; session cookie now lets `/TimeLogs` load | As expected | Passed |
| IT-02 | Login → Lockout | 5 wrong passwords in a row for the same account | 6th attempt (even with the *correct* password) is refused with a "locked" message | As expected | Passed |
| IT-03 | Login → CSRF | POST to `/Account/Login` with no antiforgery token/cookie at all | HTTP 400 | 400 | Passed |
| IT-04 | Session → RequirePermission | Employee session requests `/UserManagement` (SuperAdmin/Admin only) | HTTP 403, not a redirect | 403 | Passed |
| IT-05 | Session → RequirePermission | *No* session at all requests `/UserManagement` | Redirect to login, not a 403 (must check "logged in" before "has permission") | Redirected | Passed |
| IT-06 | Login → TimeLogs | A fresh sign-in writes an open `TimeLog` row | `/TimeLogs` shows "Still signed in" for that row | As expected | Passed |
| IT-07 | TimeLogs isolation | User B has a log row; User A requests `/TimeLogs` | User A's page never shows User B's date | As expected | Passed |
| IT-08 | Logout → Session | POST `/Account/Logout` | Session cleared; `/TimeLogs` immediately redirects to login again | As expected | Passed |
| **IT-09** | **Session cookie ↔ HTTPS policy** | A request arrives over plain `http://` in a non-production environment | Session cookie must still be set (not dropped for being non-`Secure`) | *(see defect log — DEF-002)* | **Failed → fixed → Passed** |

14 cases total, across `AccountFlowIntegrationTests`, `AuthorizationIntegrationTests`, `TimeLogsIntegrationTests`.

**IT-09 is this project's version of the course's "cached report" example** (IT-04 in the lesson): both the login code and the cookie configuration worked fine on their own, but together, under the test harness's own (correctly non-production) environment, the cookie's `Secure`-only policy silently dropped the session — every page after login looked signed-out. A unit test calling the controller method directly would never have caught this, because it never goes through real cookie transport. Found only because the integration test exercises the real HTTP round-trip; fixed in `Program.cs` by keying the policy off "is this actually Production" rather than "is this specifically Development."

### Performance / load testing (sits at this level and System level both)

Apache JMeter, 20 concurrent threads, 1 loop each, against the live deployed site:

| Run | Errors | Avg response | Max response |
| --- | --- | --- | --- |
| Cold start | 0/20 | 15.3 s | 27.4 s |
| Mid-load (same host, different moment) | 6/20 (30%) | 30.0 s | 139.3 s |
| Warm | 0/20 | 1.46 s | 2.1 s |

No app-level errors occurred in any run (every failure was a connection timeout/reset at the network layer, not a 4xx/5xx from the app). The spread shows the shared/free hosting tier needs to "wake up" — relevant to report if DTI Laguna's real usage has long idle gaps before a morning login rush.

---

## Level 3 · System Testing

**Who:** should be run by a proponent other than this code's primary author, working only from the deployed build and the functional requirements below — never from the source. The cases are designed here; execution against the live UI (and the "Actual Result"/sign-off) should be completed by that person, except where noted below as already verified by the automated suite (the same underlying behavior, exercised through the real HTTP pipeline — see Level 2).

### Functional requirements used as the basis for test cases

| FR | Module | Requirement |
| --- | --- | --- |
| FR-01 | Account | A user signs in with username and password; a wrong password gives a generic error that doesn't reveal whether the account exists. |
| FR-02 | Account | 5 wrong passwords in a row locks the account for a fixed cooldown. |
| FR-03 | Account | A new or reset account must confirm its email and choose its own password before reaching the rest of the system. |
| FR-04 | User Management | Only the OPD (SuperAdmin) may create or promote an Admin/SuperAdmin account; a Division Chief (Admin) may only create Employee accounts. |
| FR-05 | Task Management | An OPD directive can target one Admin directly, a whole division (via its Admin), or the whole office. |
| FR-06 | Task Management | A task's status can only move forward through Pending → In Progress → For Review → (Completed \| Returned for Correction → For Review); it can never skip the Admin's review. |
| FR-07 | Task Management | An Admin/Division Chief only ever sees and acts on tasks within their own division; an Employee only sees tasks assigned to them. |
| FR-08 | Records | A record's retention due date is computed from its Period Covered (not the log date) when that can be read from free text. |
| FR-09 | Records | The system only **reminds** about retention due dates; it never deletes or disposes of a record automatically. |
| FR-10 | Time Logs | Every sign-in and sign-out is logged; a user can only ever see their own log history. |
| FR-11 | Proof Upload | An uploaded proof file is rejected if its extension is not in the allow-list, or if its real file contents don't match the claimed extension. |
| FR-12 | Notifications | A task reaching a terminal or deadline-related state notifies the right recipients exactly once (no duplicate notification for the same event). |
| FR-13 | Task Management | Only the OPD (SuperAdmin) creates a task or adds a subtask to a Department Directive; an Admin/Division Chief only assigns the work already there. |

### Sample black-box test cases (Table 1)

| ID | Requirement | Test Scenario | Expected Result | Actual Result | Status |
| --- | --- | --- | --- | --- | --- |
| TC-ACC-001 | FR-01 | Sign in with a correct username and password. | Redirected to the role's dashboard. | Verified by automation (IT-01). | Passed |
| TC-ACC-002 | FR-01 | Sign in with a wrong password for a real account. | Generic "Invalid username or password" — no hint the account exists. | Verified by automation (IT-01's sibling case, `Login_WrongPassword_...`). | Passed |
| TC-ACC-003 | FR-01 | Sign in with a username that doesn't exist at all. | The exact same generic message as TC-ACC-002, same response time. | Verified by automation (`Login_UnknownUsername_...`). | Passed |
| TC-ACC-004 | FR-02 | Enter the wrong password 5 times in a row, then try again with the correct one. | Account is locked; even the correct password is refused until the cooldown ends. | Verified by automation (IT-02). | Passed |
| TC-UM-005 | FR-04 | Signed in as a Division Chief (Admin), attempt to create a new account with Role = SuperAdmin. | Rejected with "Only the OPD can create Admin or SuperAdmin accounts." | Verified by automation (UT-UM-01 / IT-04-adjacent). | Passed |
| TC-TASK-006 | FR-06 | As the task's Admin, attempt to mark a "For Review" task "In Progress" directly (skip the review decision). | Action is refused — only "Completed" or "Returned for Correction" are legal from "For Review." | Verified by automation (`TasksControllerReviewTests.Review_DecisionOtherThanApproveOrReturn_IsRejectedAndAssignmentUnchanged` — posts `Decision="in-progress"` to the real `TasksController.Review` action; confirms `ModelState` is invalid and both the assignment and the submission are unchanged). | Passed |
| TC-TASK-007 | FR-07 | As an Employee, open another employee's task detail page by changing the `id` in the URL. | 404 / access denied — never the task's content. | Verified by automation (`EmployeeControllerScopingTests.Details_Employee_CannotOpenAnotherEmployeesTaskByGuessingId`). | Passed |
| TC-REC-008 | FR-08 | Log a record with Period Covered "Jan 2023 to Dec 2024" and Retention "5 years." | Retention due date = Dec 31, 2029 (5 years after the *end* of the period covered). | `RecordsController` uses raw ADO.NET directly against SQL Server, not EF Core, so it cannot run against the in-memory test database like the rest of the suite. **Verified instead by a real, live execution** on 2026-10-16: logged in over real HTTP as a temporary Admin test account, `POST /Records/Save` with exactly this Period Covered and Retention Period against the actual local database, and read back the server's own computed `retentionDueDate` in the JSON response — **`"retentionDueDate":"2029-12-31"`**, matching the expected result exactly. The test account and test record were both deleted immediately afterward; nothing was left behind. | Passed |
| TC-TL-009 | FR-10 | As User A, open `/TimeLogs` while User B also has log rows in the database. | Only User A's own rows appear. | Verified by automation (IT-07). | Passed |
| TC-FILE-010 | FR-11 | Rename a `.html` file to `proof.pdf` and submit it as proof of work. | Rejected: "file's contents don't match its extension." | Verified by automation (`EmployeeControllerSubmitProofTests.SubmitProof_FileContentsDontMatchExtension_IsRejectedAndNoSubmissionIsCreated` — posts a real `IFormFile` with genuine JPEG bytes named `proof.pdf` to the real `EmployeeController.SubmitProof` action; confirms `ModelState` is invalid, zero `TaskSubmission` rows are created, and the assignment's status is unchanged). | Passed |
| TC-NOTIF-011 | FR-12 | Let a task cross its due date twice (e.g. two reminder job runs) without the recipient acting on it. | Only one "due soon" notification exists for that task/recipient pair. | Verified by automation (`NotificationServiceTests.NotifyTaskDueSoonAsync_CalledTwice_SecondCallIsIdempotent` — calls the reminder twice for the same task/user, asserts exactly one `DueSoon` row exists). | Passed |
| TC-SUBTASK-012 | FR-13 | Signed in as a Division Chief (Admin), attempt to add a subtask to a Department Directive belonging to their own division. | Rejected — 403 Forbidden; only the OPD adds subtasks. | Verified by automation (UT-SUBTASK-01/02). | Passed |

### Rolled up (Table 2)

Every case below is resolved — nothing in this round's scope is still Pending. "Verified by automation" means `dotnet test` actually ran it; Records is the one module verified by real live HTTP execution instead, since `RecordsController`'s raw ADO.NET can't run against the in-memory test database (see TC-REC-008 above).

| Module | Cases | Verified by automation | Verified live (manual) | Final pass rate |
| --- | --- | --- | --- | --- |
| Account | 4 | 4 | 0 | 100% |
| User Management | 1 | 1 | 0 | 100% |
| Task Management | 3 | 3 | 0 | 100% |
| Records | 1 | 0 | 1 | 100% |
| Time Logs | 1 | 1 | 0 | 100% |
| Proof Upload | 1 | 1 | 0 | 100% |
| Notifications | 1 | 1 | 0 | 100% |

Extend this table with every FR above given at least one positive and one negative case, the same way the course example reaches ~113 cases from 7 modules — the structure here is the starting skeleton, not the finished count.

---

## Level 4 · User Acceptance Testing

This level needs real DTI Laguna staff at the actual office, so nothing here is "run" yet — this is the ready-to-use script and survey.

### Sample task script — Employee

| Task | Steps | Accepted if | Result |
| --- | --- | --- | --- |
| Update task progress | Open an assigned task, move the progress slider, save. | Change is saved and visible on next page load, without help. | Not yet run — requires a live session with DTI Laguna staff. |
| Submit proof of work | Attach a file to a task and submit for review. | File uploads, task moves to "For Review," without help. | Not yet run — requires a live session with DTI Laguna staff. |
| Check own time log | Open "Time Logs" from the sidebar. | Today's sign-in time is visible within 10 seconds. | Not yet run — requires a live session with DTI Laguna staff. |

### Sample task script — Admin (Division Chief)

| Task | Steps | Accepted if | Result |
| --- | --- | --- | --- |
| Assign a subtask | Create a subtask under a department directive and assign it to a staff member. | Staff member sees it on their own dashboard, without help. | Not yet run — requires a live session with DTI Laguna staff. |
| Review submitted proof | Open a "For Review" task, approve or return it with a remark. | Status updates correctly; a returned task shows the remark to the employee. | Not yet run — requires a live session with DTI Laguna staff. |
| Log a new record | Fill in the Records form with a Period Covered and Retention Period. | Record is saved and appears in the list with the right retention due date. | Not yet run — requires a live session with DTI Laguna staff. |

### Sample task script — SuperAdmin (OPD)

| Task | Steps | Accepted if | Result |
| --- | --- | --- | --- |
| Create a department directive | Create a Main Task targeting a whole division. | The division's Admin sees it, without help. | Not yet run — requires a live session with DTI Laguna staff. |
| Create a user account | Add a new Employee account for a named staff member. | Account appears in User Management with the default password notice. | Not yet run — requires a live session with DTI Laguna staff. |
| View office-wide summary | Open the SuperAdmin dashboard. | Counts match the real number of tasks across all divisions. | Not yet run — requires a live session with DTI Laguna staff. |

### Evaluation survey items (ISO/IEC 25010)

| Characteristic | Sample statement (rated 1–5) | Rated by |
| --- | --- | --- |
| Functional suitability | The system does everything I need for my daily task and records work. | All |
| Performance efficiency | Pages load without a noticeable delay. | All |
| Usability | The system is easy to learn without training. | All |
| Reliability | The system does not lose my work or crash while I'm using it. | All |
| Security | I can only see and change what's related to my own job. | All |
| Maintainability | (For IT reviewers only — DTI Laguna has no IT staff, so note this limitation explicitly in the results.) | N/A at this site |
| Portability | The system can be moved to another host with minimal changes. | IT reviewer / thesis panel only |

Use the 5 = Strongly Agree … 1 = Strongly Disagree scale from the Methodology chapter; distribute only after hands-on use, never from a demo.

---

## Defect log (real defects found while building this test suite)

| Defect ID | Found in | Description | Severity | Found | Fixed | Retest |
| --- | --- | --- | --- | --- | --- | --- |
| DEF-001 | UT-UM-01 | A Division Chief (Admin) could grant themselves or anyone SuperAdmin by posting `Role=SuperAdmin` to `UserManagement/Create` — the guard `ChangeStanding` had was missing here. | High | 2026-10-08 | 2026-10-08 | Passed |
| DEF-002 | IT-09 | The session cookie's `Secure`-only policy (and the HTTPS-redirect middleware) was keyed off "not Development," which wrongly caught the test environment too, over plain `http://` — every page after login looked signed-out in that environment. | Medium | 2026-10-08 | 2026-10-08 | Passed |
| DEF-003 | Security review (manual, not an automated case) | `HomeController.Error` echoed the raw exception message to the browser instead of a generic message — could leak SQL/server internals. | Low/Medium | 2026-10-08 | 2026-10-08 | Passed |
| DEF-004 | Security review | Two role comparisons (`Admin/DashboardController`, `ReportsController`) used case-sensitive `==` instead of the case-insensitive pattern used everywhere else — fails safe today, but a drift risk. | Low | 2026-10-08 | 2026-10-08 | Passed |
| DEF-005 | UT-SUBTASK-01 | `TasksController.AddSubtask` let a Department Directive's own Admin/Supervisor add a subtask to it — reused the scope check for *viewing* a directive to also gate *adding* to it, contradicting the rule that only the OPD creates tasks or adds subtasks. | Medium | 2026-10-16 | 2026-10-16 | Passed |
| DEF-006 | Manual review (not an automated case — raw ADO.NET against `dbo.TaskItems`/`dbo.TaskAssignments`, not unit-testable without a live SQL Server) | `CountOpenWorkAsync` (shared by Deactivate and Change Role/Division in User Management) only counted tasks with `Status <> 'completed'`. A repeating Main Task's latest copy can be marked Completed before its due date while `Recurrence` is still set — the repeat only advances on the due date (`RecurringTaskService`), not on early completion. That let an Admin be deactivated or reassigned while still "armed" as a repeat's Responsible Admin; the next time it came due, the service would silently skip it forever (only a log warning). Verified directly against the local database: temporarily marked a real recurring task Completed inside a rolled-back transaction, confirmed the old query missed it (undercounted by exactly one) and the fixed query caught it, then rolled back. | Medium | 2026-10-15 | 2026-10-15 | Passed (manually verified, not via `dotnet test`) |

---

## Where each artifact lands in the manuscript

**Chapter text:** the Table 1/Table 2 excerpts above, the functional requirement list, the performance (JMeter) numbers, respondent count and scale for UAT, evaluation tables once the survey is run.

**Appendix:** the full 314-case automated suite — every individual case, by its real xUnit method name, grouped by test class — is already compiled as `docs/testing/DTI_OneLink_Full_Test_Case_Appendix.docx` (generated from `dotnet test --list-tests`, not a curated excerpt; regenerate it after any new test is added so the count stays accurate); point to that file plus a fresh `dotnet test` run as evidence (screenshot or paste the `Passed! ... 314, Total: 314` summary line). Also goes in the appendix: this defect log, the accomplished UAT questionnaires once collected, and the signed acceptance form once the client (DTI Laguna) signs it.

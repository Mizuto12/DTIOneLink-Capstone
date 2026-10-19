## Testing

### Module 1: Log-in Module

The Log-in Module serves as a secure entry point for registered users of DTI OneLink. It requires the user's registered username (their email) and password, and enforces an account lockout after repeated wrong attempts. Unlike a generic login form, it deliberately gives a wrong password and an unknown username the exact same response, so the page can never be used to find out which accounts exist. The module was tested to validate correct and incorrect sign-in behavior, the lockout rule, and CSRF protection on the form.

| Test Case | Input | Action | Expected Output |
| --- | --- | --- | --- |
| Login with correct username and password | Username = "employee@dti.gov.ph"; Password = correct | Click Log In | Redirect to the role's own dashboard; session cookie set; protected pages now load |
| Login with wrong password | Username = valid account; Password = wrong | Click Log In | Stays on page; error: "Invalid username or password." |
| Login with an unknown username | Username = "nobody@dti.gov.ph"; Password = anything | Click Log In | Stays on page; the exact same error as a wrong password — never reveals the account doesn't exist |
| Login with a locked account | Username = valid account that already failed 5 times; Password = correct | Click Log In | Stays on page; error: "Too many wrong passwords. Sign-in for this account is locked for N minutes." — even the correct password is rejected |
| Fifth wrong password in a row | Username = valid account; Password = wrong, 5th attempt | Click Log In | Account is locked for the cooldown period starting this attempt |
| Submit the login form without the hidden anti-forgery token | Username/Password filled in; `__RequestVerificationToken` missing | Submit the form directly (not through the real page) | HTTP 400 Bad Request — the form post is rejected before reaching the account check |
| New account's first sign-in with the default password | Username = newly created account; Password = default | Click Log In | Redirect to "Confirm your email" (a code is emailed) before reaching the dashboard |

*Table 1. Log-in Module*

### Module 2: User and Role Management Module

The User and Role Management Module lets the OPD (SuperAdmin) and Division Chiefs (Admin) create and oversee staff accounts. Only the OPD may create or promote an Admin or SuperAdmin account — a Division Chief may only create Employee accounts. The module was tested to confirm this restriction, duplicate-account handling, and that only the OPD can change an existing user's role or division.

| Test Case | Input | Action | Expected Output |
| --- | --- | --- | --- |
| OPD creates a new Employee account | Full Name, Email, Division filled in; Role = Employee; signed in as SuperAdmin | Click Save User | Account created with the default password; "Account created, confirm email with a code" message shown |
| Division Chief creates a new Employee account | Same as above; signed in as Admin | Click Save User | Account created — Admins may freely create Employee accounts |
| Division Chief attempts to create a SuperAdmin account | Role = SuperAdmin; signed in as Admin | Click Save User | Rejected: "Only the OPD can create Admin or SuperAdmin accounts." — no account is created |
| Division Chief attempts to create another Admin account | Role = Admin; signed in as Admin | Click Save User | Same rejection as above |
| Create an account with an email already in use | Email matches an existing user | Click Save User | Rejected: "This email is already used by &lt;name&gt;." |
| Required fields left blank | Full Name = ""; Department = "" | Click Save User | Page stays; "Please complete all account fields correctly." |
| OPD changes a Division Chief's division | Signed in as SuperAdmin; select a user; change Division | Click Save (Change Standing) | User's division updates; their next page load reflects the new division |
| Division Chief attempts to change another user's role | Signed in as Admin; open the Change Standing action directly | Submit the form | HTTP 403 Forbidden — the action is OPD-only regardless of the UI |
| Not logged in at all | No session | Request `/UserManagement` | Redirected to the login page, not a 403 |

*Table 2. User and Role Management Module*

### Module 3: Task Management Module

The Task Management Module lets the OPD assign directives, Division Chiefs manage their own division's work, and Employees update and submit their assigned tasks. A task's status can only move forward through a fixed sequence, and every role only ever sees the tasks within its own scope.

| Test Case | Input | Action | Expected Output |
| --- | --- | --- | --- |
| Employee updates progress on an assigned task | Open an assigned task; move progress slider to 60% | Click Save | Progress is saved and shown on next page load |
| Employee submits proof of work | Attach a valid PDF; task currently "In Progress" | Click Submit for Review | File uploads; task status becomes "For Review" |
| Employee attaches a disguised file as proof | A `.html` file renamed to `proof.pdf` | Click Submit for Review | Rejected: "The file's contents don't match its extension. Please re-upload a genuine file of that type." |
| Admin reviews a "For Review" task and approves it | Open the task; click Approve | Click Approve | Status becomes "Completed" |
| Admin returns a task with a remark | Open the task; enter a correction remark; click Return | Click Return for Correction | Status becomes "Returned for Correction"; employee sees the remark |
| Attempt to skip the review step | Task status = "For Review"; try to set it directly to "In Progress" | (system-level call) | Rejected — "For Review" can only move to "Completed" or "Returned for Correction" |
| Attempt to reopen a completed task | Task status = "Completed"; try to set it to "In Progress" | (system-level call) | Rejected — a completed task has no further moves |
| Division Chief opens the department dashboard | Signed in as Admin for Business Development Division | Open Task Management | Only tasks belonging to that division are listed |
| Employee opens another employee's task by changing the URL id | Signed in as Employee; edit the `id` in the task details URL to a task not assigned to them | Open the URL | 404 Not Found — never the task's content |
| OPD creates a whole-office directive | Role = SuperAdmin; Task Type = Whole Office | Click Create | Every active Admin and Employee gets their own assignment on the task |

*Table 3. Task Management Module*

### Module 4: Records Module

The Records Module lets staff log DTI Laguna's physical and electronic records and tracks when each one is due for retention review. The system only reminds about due dates — it never deletes or disposes of a record automatically.

| Test Case | Input | Action | Expected Output |
| --- | --- | --- | --- |
| Log a new record with a clear retention period | Period Covered = "April 13, 2023 to April 30, 2025"; Retention Period = "3 years" | Click Save | Record saved; retention due date = April 30, 2028 (3 years after the end of the period covered) |
| Log a record with an ambiguous retention period | Retention Period = "5 years after the contract" | Click Save | Record saved without a computed due date; left for records personnel to review manually |
| Log a record with only a year given as Period Covered | Period Covered = "2024" | Click Save | Due date counted from December 31, 2024 (end of year) |
| View records nearing their retention due date | Open Records list | — | Rows due within the next 3 months (or already past due) are visibly flagged, with no deletion action available |
| Search records by code or title | Type a record code into the search box | Click Search | Matching records appear; server-side filtered |
| Attempt to save a record with missing required fields | Code = ""; Title = "" | Click Save | Page stays; "Please complete all required fields." |

*Table 4. Records Module*

### Module 5: Time Logs Module

The Time Logs Module records every sign-in and sign-out and lets each user see their own history only — never another user's.

| Test Case | Input | Action | Expected Output |
| --- | --- | --- | --- |
| Sign in successfully | Valid username and password | Click Log In | A new Time Log row is created for this user with today's sign-in time, no sign-out yet |
| Open Time Logs right after signing in | Signed in user opens "Time Logs" from the sidebar | — | The just-created row appears, showing "Still signed in" |
| Sign out | Click Logout | Click Logout | The open Time Log row for this session gets today's sign-out time filled in |
| Open Time Logs with no history | A user who has never signed in through the normal flow | Open "Time Logs" | "No time logs yet." is shown, not an error |
| One user's logs checked against another's | User A has one log row; User B has a different, unrelated log row | User A opens "Time Logs" | Only User A's own row appears; User B's row and date never show up |

*Table 5. Time Logs Module*

### Module 6: Notifications Module

The Notifications Module alerts staff and the OPD about task deadlines, directive completions, and record retention — each exactly once per event, never duplicated.

| Test Case | Input | Action | Expected Output |
| --- | --- | --- | --- |
| A task's due date arrives | A task becomes due within the reminder window | (scheduled job runs) | The assignee gets exactly one "due soon" notification |
| A task's due date passes a second time (job runs twice) | Same task, reminder already sent once | (scheduled job runs again) | No second notification is created for the same task and recipient |
| OPD directive reaches completion | All assignees of a whole-office directive finish their part | — | The OPD gets a single "directive completed" notification, firing only once |
| A record approaches its disposal review date | Record's retention due date is within the reminder window | (scheduled job runs) | The record's owner is notified once, with wording reflecting the days remaining |
| Notification text is too long | A task name longer than 500 characters | (system creates the notification) | Message is truncated to 500 characters rather than failing to save |

*Table 6. Notifications Module*

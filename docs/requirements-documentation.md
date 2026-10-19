# Requirements Documentation

> This part establishes the basis for the agreement between the customer/client and the
> developers/programmers on what the software product is to do.

**Formatting note (not part of the manuscript text):** this section was previously drafted
as a User Story → Short Description → System Support → Storyboard table per feature. After
checking two accepted capstone papers from the same program (Project B.A.R.I.S.T.A. and
YUGTOPIA) for the expected format, neither uses that structure. Both instead use a plain
two-part layout:

1. **Functional Requirements** — numbered features, each followed by a flat list of
   "The system shall..." statements. No user story, no separate "Short Description"/"System
   Support" labels, and no inline storyboard references.
2. **Non-Functional Requirements** — a separate numbered list (Scalability, Performance,
   Reliability, Security, Accessibility and Compatibility, Usability, Availability,
   Maintainability, Data Integrity, Backup and Recovery), each a short paragraph.

Storyboards/mockups are **not** merged into this section in either reference — they stay in
their own separate design/storyboard section. So the earlier advice to delete or merge your
existing "Design of Software, System, Product, and/or Process" section was wrong; keep it as
its own section, organized however reads best (by role or by feature), and leave this section
to just the two lists below.

The content below is rewritten into that format from your existing 16 features. The
Non-Functional Requirements are newly drafted (your manuscript didn't have a dedicated
section for them) — review and adjust wording to match what you actually implemented and
can defend.

**Accuracy note (not part of the manuscript text):** every claim below was checked against
the actual code in two passes, not just reworded from the manuscript. Two features that the
original draft listed as implemented — Archival Referencing and Backup and Recovery Support
— have no code behind them anywhere in the project and have been removed; a panelist asking
"show me" would find nothing. Employee Recognition was rewritten to Task Priority
Suggestion, since the dashboard's "Top Performers" widget was deliberately removed (staff
ratings are explicitly out of scope — see `Views/Shared/_DashboardContent.cshtml`) and what
actually exists is a simple rule (days until due date), not linear regression. The Records
features (10–12) originally kept the manuscript's generic field names (document type,
subject, source office, reference number, sender, recipient) — the real record only has
code, title, medium, location, period covered, filing system, access control, and retention
period, so those three features were rewritten around the real fields and search behavior.
Several further corrections came from a second check: the OTP step is tied to an unconfirmed
email on an account that still must change its password, not generically to "default or
temporary passwords"; task review has only two outcomes (approve or return), not three; the
List View and Kanban Board are two different pages, not one page with both; and the
Records Summary dashboard widget and CSV export only cover what's actually there (Admin
dashboard only; export is a client-side CSV of the current list, not a workload-distribution
report). Features are renumbered 1–14 to close the gaps.

---

## Functional Requirements

### 1. User Account and Role Management

- The system shall allow the system administrator to create, update, deactivate, and
  manage user accounts.
- The system shall maintain user roles — Super Admin, Admin, and Employee — to support
  organized, role-based access control.
- The system shall allow a Super Admin to create an account of any role and division, and
  to change an existing account's role or division. An Admin may only create Employee
  accounts within their own division.

### 2. Secure Login and Access Control

- The system shall provide secure authentication using salted password hashes and
  session-based login.
- The system shall temporarily lock an account out after repeated failed sign-in attempts.
- The system shall require a one-time email code to confirm the account's email address
  before completing sign-in, for an account that still must change its password and whose
  email is not yet confirmed (e.g. a newly created account, or one whose email an
  administrator just changed) — and separately for password recovery.
- The system shall enforce role-based access control so each user is routed to, and can
  only use, the features assigned to their role.
- The system shall restrict access to records and reports in accordance with the Data
  Privacy Act of 2012 (RA 10173).

### 3. Task Creation, Assignment, and Detailed Task Anatomy

- The system shall allow a supervisor to create a task with a title, description,
  assignee(s), reporter, due date, priority, status, and subtasks.
- The system shall allow a task to be assigned to one or more employees.
- The system shall allow a task to be marked as recurring, with options for Daily, Weekly,
  Monthly, or Quarterly, so the system automatically creates a new instance a fixed interval
  after the previous due date.
- The system shall allow a supervisor or employee to break a task into smaller subtasks,
  each with its own assignee, due date, and status.
- The system shall automatically recalculate a parent task's status from its subtasks: it
  becomes Completed once every subtask is completed, For Review once every subtask is
  completed or in review, and otherwise remains In Progress or Pending — with no manual
  override.
- The system shall provide a threaded comment section and a chronological activity log
  within each task, recording every status change, assignee change, due-date edit, file
  upload, and comment.
- The system shall allow a supervisor to mark a comment as a formal revision note.

### 4. Task Priority Suggestion

- The system shall suggest a priority level for a task — High, Medium, or Low — based on
  how many days remain until its due date: overdue or due within 2 days suggests High, due
  within 3–7 days suggests Medium, and more than 7 days away suggests Low.
- This suggestion is a fixed rule based on the due date, intended as a starting reference
  for whoever sets the task's priority, not an automated or learned decision.

### 5. Deadline and Reminder Management

- The system shall allow a supervisor to set a due date when creating or editing a task.
- The system shall generate an in-app reminder for tasks approaching their due date and an
  in-app alert for tasks that are overdue.

### 6. Task Progress Updates

- The system shall allow an employee to update the status of a task assigned to them — To
  Do, In Progress, For Review, Returned for Correction, or Completed.
- The system shall display a task as Overdue when its due date has passed, computed at
  display time rather than stored as a separate status.
- The system shall allow an employee to leave remarks describing current progress when
  updating a task's status.

### 7. Proof of Completion Submission

- The system shall allow an employee to upload a proof file — such as an accomplishment
  report, screenshot, or supporting document — with a task submission.
- The system shall validate the uploaded file by type before accepting it.
- The system shall flag a task as missing its required proof if it is submitted without
  one.

### 8. Supervisor Review and Validation

- The system shall allow a supervisor to review a submitted task output and either approve
  it or return it for correction, with remarks required when returning it.
- The system shall record the validation status, review remarks, review date, and the
  reviewing supervisor for every submission.

### 9. Workflow Status Monitoring

- The system shall provide a centralized, filterable list of tasks, with filters for
  priority, employee, due date, and status. A Super Admin may additionally filter by
  division; an Admin's view is always limited to their own division.
- The system shall additionally provide a Kanban board view (To Do → In Progress → For
  Review → Returned for Correction → Completed, with overdue tasks flagged in red) on the
  Employee Task Management page, and as a Timeline/Board option on the Admin dashboard.

### 10. Records Classification and Indexing

- The system shall allow records personnel to log a record with its code, title, medium
  (Hard Copy, Electronic, or Hard Copy/Electronic), storage location, period covered,
  filing system, access control level, and retention period.
- The system shall tag each record with the division that logged it and the date it was
  logged.
- The system shall compute a retention due date automatically from the end of the record's
  period covered (or the date it was logged, if that can't be read) plus its typed
  retention period, for retention monitoring.

### 11. Records Search and Retrieval

- The system shall provide a search box that matches a typed term against a record's code,
  title, storage location, or period covered.
- The system shall allow filtering the record list by medium and by access control level.

### 12. Retention and Disposal Monitoring

- The system shall flag a record's color-coded retention status as due soon once its
  computed retention due date is within six months, and as ended once that date has
  passed.
- The system shall notify records personnel (in-app and by email) when a record's
  retention due date has passed, so it can be reviewed for disposal.
- The system shall only notify of this; it does not itself record or carry out a disposal
  action — reviewing and disposing of the physical or electronic record remains a manual
  process outside the system.

### 13. Notification and Alert Management

- The system shall provide a single in-app notification feed covering task deadlines,
  overdue work, pending reviews, and records nearing retention review.
- The system shall display a notification indicator in the header of every screen.
- The system shall additionally deliver an email (via Brevo) for a task being assigned or
  reassigned, a submission being approved or returned, an admin-directed task being
  created, and a record becoming due for disposal review. Due-date and overdue task
  reminders are delivered in-app only, not by email.

### 14. Administrative Dashboard and Operational Reports

- The system shall provide a role-based dashboard for each of Super Admin, Admin, and
  Employee, each showing a task summary and a Delay Risk Indicator — a rule-based
  comparison of time elapsed against task progress — scoped to what that role can see.
- The system shall additionally show a Records Summary (total records, records due soon,
  records needing disposal review, and saved masterlists) on the Admin dashboard.
- The system shall provide a Reports page listing Active Tasks, Completed Tasks, a Task
  History activity log, and records grouped by retention status, with each list
  downloadable as a CSV file generated in the browser from what is currently displayed.

---

## Non-Functional Requirements

### 1. Scalability

The system shall continue to support additional user accounts, tasks, and records as the
office's usage grows, without requiring structural changes to the application.

### 2. Performance

The system shall respond to common actions — signing in, loading a dashboard, saving a
task, and searching records — within a few seconds under normal office network conditions.

### 3. Reliability

The system shall process task and record updates consistently and without data loss during
normal office operating hours.

### 4. Security

The system shall store passwords as salted hashes, require one-time email code
verification before a new or email-changed account's first sign-in, lock an account out
temporarily after repeated failed sign-in attempts, and enforce role-based access control
so a user can only reach the features and records their role permits. The system shall
comply with the Data Privacy Act of 2012 (RA 10173) in how it stores and restricts access
to personal and office data.

### 5. Accessibility and Compatibility

The system shall be usable on modern desktop and mobile web browsers without requiring any
additional software installation on the user's device.

### 6. Usability

The system shall provide a simple, uncluttered interface appropriate for non-technical
office staff, consistent with the office's minimal-features, user-friendly requirement.

### 7. Availability

The system shall remain accessible throughout the office's working hours, relying on its
managed hosting provider for uptime rather than on-site IT staff.

### 8. Maintainability

The system shall be organized into separate modules (accounts, tasks, records,
notifications) so that future fixes and feature changes can be made to one module without
affecting the others, and so the system can be handed over without requiring a dedicated IT
staff member to maintain it.

### 9. Data Integrity

The system shall ensure that task statuses, submission records, and records-management
entries remain accurate and consistent with the actions users actually performed.

### 10. Backup and Recovery

The system shall rely on its managed hosting provider's and database server's backup
facilities to protect office data against accidental loss. The application itself does not
implement a separate in-app backup or restore feature — this is an infrastructure property
of where the system is hosted, not a function the software performs.

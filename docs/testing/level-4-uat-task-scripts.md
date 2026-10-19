# Level 4 — UAT Role-Based Task Scripts

DTI OneLink: An Integrated Task and Records Management System for DTI Laguna Provincial
Office. These are **real-work task scripts for a facilitator to observe**, not a
questionnaire. Every task below only uses a function confirmed present in the current code
(cross-checked against `docs/requirements-documentation.md` and the Level 3 traceability
work in `level-3-traceability.md`).

**Explicitly not included, per scope:** automatic record disposal or archival (the system
only *notifies*; see FR3-12), AI/linear-regression-based recommendations (the real feature
is a simple due-date rule, FR3-4), attendance tracking (Time Logs record sign-in/out only,
not attendance), and records-document file upload (not built — masterlist entries only).

**Result field convention:** every task starts as **Pending UAT**. It only changes once a
real participant actually attempts it with a facilitator present. A task is not marked
Completed because the underlying feature was verified in Level 3 testing — that was
testing by a developer-adjacent tester, not by an actual intended user, which is the whole
point of Level 4.

---

## Role: SuperAdmin / OPD

### UAT-SA-001 — Create a division directive

| Field | Value |
| --- | --- |
| Participant role | SuperAdmin (OPD) |
| Real work task | Create a task directive targeting one whole division, the way the OPD would assign work to a Division Chief. |
| Preconditions / test data | A SuperAdmin account; at least one division with an active Admin. |
| User steps (plain language) | 1. Sign in. 2. Go to Task Management, click Create Task. 3. Fill in the title and due date. 4. Set the target to a specific division. 5. Save. |
| Acceptance criteria (observable) | The task appears in that division's task list; the Division Chief's account shows it on their next sign-in. |
| Completion time | _______ |
| Assistance required | _______ |
| Issues / remarks | _______ |
| Result | **Pending UAT** |

### UAT-SA-002 — Create a whole-office directive

| Field | Value |
| --- | --- |
| Participant role | SuperAdmin (OPD) |
| Real work task | Create a directive meant for every division at once. |
| Preconditions / test data | A SuperAdmin account. |
| User steps | 1. Create Task. 2. Set Task Type to Whole Office. 3. Save. |
| Acceptance criteria | Every active Admin and Employee account receives their own copy of the assignment. |
| Completion time | _______ |
| Assistance required | _______ |
| Issues / remarks | _______ |
| Result | **Pending UAT** |

### UAT-SA-003 — Create a new staff account

| Field | Value |
| --- | --- |
| Participant role | SuperAdmin (OPD) |
| Real work task | Add a new employee to the system the way the OPD would onboard new staff. |
| Preconditions / test data | A SuperAdmin account; a sample name/email/division not already in the system. |
| User steps | 1. User Management → Add User. 2. Fill in name, email, division, role. 3. Save. |
| Acceptance criteria | The account appears in User Management with a default-password notice; the participant can explain, in their own words, what the new employee needs to do to sign in the first time. |
| Completion time | _______ |
| Assistance required | _______ |
| Issues / remarks | _______ |
| Result | **Pending UAT** |

### UAT-SA-004 — View the office-wide dashboard

| Field | Value |
| --- | --- |
| Participant role | SuperAdmin (OPD) |
| Real work task | Check the overall status of tasks across the whole office, the way the OPD would before a status meeting. |
| Preconditions / test data | Existing tasks across more than one division. |
| User steps | 1. Open the SuperAdmin dashboard. 2. Locate the task-status counts and the at-risk/delay indicator. |
| Acceptance criteria | The participant can correctly state, from the dashboard alone, roughly how many tasks are overdue or at risk, without opening individual tasks. |
| Completion time | _______ |
| Assistance required | _______ |
| Issues / remarks | _______ |
| Result | **Pending UAT** |

### UAT-SA-005 — Review office-wide reports with a date range

| Field | Value |
| --- | --- |
| Participant role | SuperAdmin (OPD) |
| Real work task | Pull a report of activity for a specific month. |
| Preconditions / test data | Tasks/records with varying dates. |
| User steps | 1. Open Reports. 2. Select a specific month (or a custom date range). 3. Review the list. 4. Try Export Data. |
| Acceptance criteria | The list changes to match the chosen period; a CSV file downloads when Export Data is clicked. |
| Completion time | _______ |
| Assistance required | _______ |
| Issues / remarks | _______ |
| Result | **Pending UAT** |

---

## Role: Admin / Division Chief

### UAT-AD-001 — Assign a task to a division staff member

| Field | Value |
| --- | --- |
| Participant role | Admin (Division Chief) |
| Real work task | Create and assign a task to a member of their own division. |
| Preconditions / test data | An Admin account; at least one active Employee in the same division. |
| User steps | 1. Create Task. 2. Pick an assignee from the division's staff. 3. Set a due date and priority. 4. Save. |
| Acceptance criteria | The task appears on the assigned employee's task list; the Admin can see it in their division's Task Management view. |
| Completion time | _______ |
| Assistance required | _______ |
| Issues / remarks | _______ |
| Result | **Pending UAT** |

### UAT-AD-002 — Reassign a task between staff

| Field | Value |
| --- | --- |
| Participant role | Admin (Division Chief) |
| Real work task | Hand a task off from one staff member to another, as would happen if someone goes on leave. |
| Preconditions / test data | A task in the division that is Pending, In Progress, or Returned for Correction (not yet Completed or For Review). |
| User steps | 1. Open the task's Edit page. 2. Use the reassign control to move it to a different employee. 3. Save. |
| Acceptance criteria | The task now shows the new assignee; the previous assignee no longer sees it; the task's history/activity shows the handoff. |
| Completion time | _______ |
| Assistance required | _______ |
| Issues / remarks | _______ |
| Result | **Pending UAT** |

### UAT-AD-003 — Review and approve a submitted task

| Field | Value |
| --- | --- |
| Participant role | Admin (Division Chief) |
| Real work task | Review a staff member's submitted proof of work and approve it. |
| Preconditions / test data | A task currently "For Review" with a submitted proof file. |
| User steps | 1. Open the task. 2. Review the submitted file. 3. Click Approve. |
| Acceptance criteria | The task's status becomes Completed; the participant can find, on the same page, who submitted it and when. |
| Completion time | _______ |
| Assistance required | _______ |
| Issues / remarks | _______ |
| Result | **Pending UAT** |

### UAT-AD-004 — Return a submission with a correction note

| Field | Value |
| --- | --- |
| Participant role | Admin (Division Chief) |
| Real work task | Send a submission back to the employee with a note on what to fix. |
| Preconditions / test data | A task currently "For Review." |
| User steps | 1. Open the task. 2. Click Return / Return for Correction. 3. Write a remark. 4. Confirm. |
| Acceptance criteria | The task status becomes Returned for Correction; the remark is visible to the employee. |
| Completion time | _______ |
| Assistance required | _______ |
| Issues / remarks | _______ |
| Result | **Pending UAT** |

### UAT-AD-005 — Log a new record with a retention period

| Field | Value |
| --- | --- |
| Participant role | Admin (Division Chief) |
| Real work task | Log a new document in the records masterlist, the way records personnel would file a new document. |
| Preconditions / test data | A sample record with a real code/title/period covered/retention period (e.g. "3 years"). |
| User steps | 1. Open Records. 2. Fill in the entry form. 3. Save. |
| Acceptance criteria | The record appears in the masterlist with a computed retention/review date the participant can see without calculating it themselves. |
| Completion time | _______ |
| Assistance required | _______ |
| Issues / remarks | _______ |
| Result | **Pending UAT** |

### UAT-AD-006 — Check the division dashboard

| Field | Value |
| --- | --- |
| Participant role | Admin (Division Chief) |
| Real work task | Check their own division's task status before a daily huddle. |
| Preconditions / test data | Existing tasks in the division. |
| User steps | 1. Open the Admin dashboard. |
| Acceptance criteria | Counts shown are limited to this Admin's own division (the participant should notice, and the facilitator should confirm, that another division's tasks are not mixed in). |
| Completion time | _______ |
| Assistance required | _______ |
| Issues / remarks | _______ |
| Result | **Pending UAT** |

---

## Role: Employee

### UAT-EM-001 — Update task progress

| Field | Value |
| --- | --- |
| Participant role | Employee |
| Real work task | Update the progress of a task assigned to them. |
| Preconditions / test data | A task assigned to this employee, currently In Progress. |
| User steps | 1. Open Task Management (or the Kanban board). 2. Open the assigned task. 3. Update its status/remarks. 4. Save. |
| Acceptance criteria | The change is visible immediately and still there after reloading the page. |
| Completion time | _______ |
| Assistance required | _______ |
| Issues / remarks | _______ |
| Result | **Pending UAT** |

### UAT-EM-002 — Submit proof of completion

| Field | Value |
| --- | --- |
| Participant role | Employee |
| Real work task | Finish a task and submit evidence of the completed work. |
| Preconditions / test data | A task assigned to this employee; a sample file (e.g. a photo or PDF) to attach. |
| User steps | 1. Open the task. 2. Attach the file. 3. Click Submit for Review. |
| Acceptance criteria | The task status becomes For Review; the employee can see their own submission afterward. |
| Completion time | _______ |
| Assistance required | _______ |
| Issues / remarks | _______ |
| Result | **Pending UAT** |

### UAT-EM-003 — Resubmit after a returned task

| Field | Value |
| --- | --- |
| Participant role | Employee |
| Real work task | Act on a correction note and resubmit. |
| Preconditions / test data | A task Returned for Correction, with a visible remark. |
| User steps | 1. Open the task. 2. Read the remark. 3. Attach a new file. 4. Resubmit. |
| Acceptance criteria | The participant can state, in their own words, what the remark asked them to fix, before resubmitting; status returns to For Review. |
| Completion time | _______ |
| Assistance required | _______ |
| Issues / remarks | _______ |
| Result | **Pending UAT** |

### UAT-EM-004 — Check own notifications

| Field | Value |
| --- | --- |
| Participant role | Employee |
| Real work task | Check for new assignments or review results without being told about them directly. |
| Preconditions / test data | At least one recent notification (e.g. a new assignment, or an approval/return). |
| User steps | 1. Open the notification bell/feed. |
| Acceptance criteria | The participant can find and explain a recent notification unaided. |
| Completion time | _______ |
| Assistance required | _______ |
| Issues / remarks | _______ |
| Result | **Pending UAT** |

### UAT-EM-005 — Switch between List and Board views

| Field | Value |
| --- | --- |
| Participant role | Employee |
| Real work task | Find the view of their tasks that's easiest for them personally. |
| Preconditions / test data | Several tasks assigned to this employee in different statuses. |
| User steps | 1. Open Task Management (list). 2. Open the Kanban board on the Employee Task Management page. |
| Acceptance criteria | The participant can identify, in the board view, which tasks are overdue (flagged visually). |
| Completion time | _______ |
| Assistance required | _______ |
| Issues / remarks | _______ |
| Result | **Pending UAT** |

---

## Role: IT Expert

The IT Expert evaluation has **not been run yet** — `tools/google-forms/CreateQuestionnaireForm.gs` still has unfilled project-detail placeholders, confirming no form has been generated or sent. These tasks are preparation for that still-outstanding session.

### UAT-IT-001 — Review account/role/data handling

| Field | Value |
| --- | --- |
| Participant role | IT Expert |
| Real work task | Independently verify that each role only reaches what it should — not by trusting the UI, but by trying to access another role's pages directly. |
| Preconditions / test data | SuperAdmin, Admin, and Employee test accounts; knowledge of the app's URL structure. |
| User steps | 1. Sign in as Employee. 2. Attempt to open `/UserManagement` and `/Tasks` directly by URL. 3. Note what happens for each. |
| Acceptance criteria | Access is denied for both (though see `level-3-defects.md` L3-DEF-001 — the exact *way* it's denied currently differs between the two; worth having the IT Expert independently notice and comment on this). |
| Completion time | _______ |
| Assistance required | _______ |
| Issues / remarks | _______ |
| Result | **Pending UAT** |

### UAT-IT-002 — Review deployment and maintainability

| Field | Value |
| --- | --- |
| Participant role | IT Expert |
| Real work task | Assess whether the system can be kept running and updated without a dedicated IT staff member, given DTI Laguna has none. |
| Preconditions / test data | Access to the repository, `PROJECT-STATUS.md`, and the deploy workflow. |
| User steps | 1. Review `.github/workflows/deploy-monsterasp.yml`. 2. Review `Data/SchemaRepair.cs` and the migration pattern. 3. Form a judgment on whether routine upkeep needs a programmer. |
| Acceptance criteria | The expert can describe, from the materials alone, how a new database column reaches production and what happens if a migration fails on the host. |
| Completion time | _______ |
| Assistance required | _______ |
| Issues / remarks | _______ |
| Result | **Pending UAT** |

### UAT-IT-003 — Complete the IT Expert questionnaire

| Field | Value |
| --- | --- |
| Participant role | IT Expert |
| Real work task | Complete the technical ISO/IEC 25010 evaluation (Maintainability, Portability, and the rest) after reviewing the system and its technical materials. |
| Preconditions / test data | `tools/google-forms/CreateQuestionnaireForm.gs` must first be filled in (program, researchers, adviser, contact email) and run to generate the actual form — this has not happened yet. |
| User steps | 1. (Researcher) Fill in the script's placeholders and run it to generate the form. 2. Send the form link to the expert. 3. Expert completes it after reviewing the system and code. |
| Acceptance criteria | A completed response exists in the form's response sheet. |
| Completion time | _______ |
| Assistance required | _______ |
| Issues / remarks | _______ |
| Result | **Pending UAT** |

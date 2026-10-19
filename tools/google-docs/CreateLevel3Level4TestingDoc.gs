/**
 * DTI OneLink Level 3 (System Testing) and Level 4 (UAT) Report -> Google Docs
 * A formatted, print-ready report of both testing levels, built from the
 * underlying markdown files in docs/testing/ — for the manuscript's Results
 * and Discussion / Testing section, or for sharing with the adviser/panel.
 * Table style (ID | Requirement/Statement | Scenario | Expected | Actual |
 * Status, numbered "Table N." captions, a per-module rollup, then Summary /
 * Conclusion / Recommendation) follows the format of a reference capstone
 * manuscript's own testing chapter — content and numbers below are DTI
 * OneLink's own, not copied from that reference.
 *
 * This reproduces every case that has REAL execution evidence (Passed /
 * Failed / Blocked). Cases still "Not Executed" are rolled up by count only,
 * not spelled out row by row — the full list of those lives in
 * docs/testing/level-3-system-test-cases.md. Full detail also lives in:
 *   docs/testing/level-3-traceability.md
 *   docs/testing/level-3-system-test-cases.md
 *   docs/testing/level-3-performance-results.md
 *   docs/testing/level-3-defects.md
 *   docs/testing/level-4-uat-task-scripts.md
 *   docs/testing/level-4-uat-questionnaire-guide.md
 *   docs/testing/level-4-uat-results-template.md
 *   docs/testing/level-4-client-acceptance-form.md
 * Re-run this script (updating the DATA section below) whenever those files
 * change materially — it does not read them live.
 *
 * HOW TO USE (about 2 minutes):
 *   1. Go to https://script.google.com and click "New project".
 *   2. Delete the sample code, paste ALL of this file, and click Save.
 *   3. Fill in the project details below (between the quotes).
 *   4. Choose the function "createLevel3Level4Doc" at the top and click Run.
 *   5. The first time, Google asks for permission: Review permissions ->
 *      your account -> Advanced -> Go to (project name) -> Allow.
 *      It only creates a Doc in YOUR Google Drive.
 *   6. Open View -> Logs for the link to the new document.
 *
 * Running it again makes a NEW copy; delete old copies from Drive if needed.
 */

// ── Capstone project details (fill these in) ─────────────────────────────
var PROJECT_TITLE = 'DTI OneLink: An Integrated Task and Records Management System for DTI Laguna Provincial Office';
var SCHOOL        = 'STI College Santa Cruz';
var PROGRAM       = '[Your program, e.g., Bachelor of Science in Information Technology]';
var RESEARCHERS   = '[Researcher 1], [Researcher 2], [Researcher 3]';
var ADVISER       = '[Capstone adviser]';
var REPORT_DATE   = '[Date this report was generated, e.g. October 10, 2026]';
// ──────────────────────────────────────────────────────────────────────────

var DOC_TITLE = 'Level 3 and Level 4 Testing Report: DTI OneLink';

// ── DATA: keep this in sync with docs/testing/*.md by hand ───────────────

var L3_TRACEABILITY = [
  // [id, requirement, status, note]
  ['FR3-1',  'User Account and Role Management', 'Implemented', 'SuperAdmin-only for Admin/SuperAdmin creation and role/division changes.'],
  ['FR3-2',  'Secure Login and Access Control', 'Implemented', 'OTP ties to unconfirmed email + must-change-password, not a generic "default password."'],
  ['FR3-3',  'Task Creation, Assignment, Detailed Task Anatomy', 'Implemented', 'No start date/tags/watchers/general attachments — confirmed absent.'],
  ['FR3-4',  'Task Priority Suggestion', 'Implemented', 'Fixed rule (days-until-due), not ML/regression.'],
  ['FR3-5',  'Deadline and Reminder Management', 'Implemented', 'Due/overdue reminders are in-app only, not emailed.'],
  ['FR3-6',  'Task Progress Updates', 'Implemented', 'Overdue is computed at display time, never stored.'],
  ['FR3-7',  'Proof of Completion Submission', 'Implemented', 'One proof file per submission.'],
  ['FR3-8',  'Supervisor Review and Validation', 'Implemented', 'Exactly two outcomes: approve or return. No third option.'],
  ['FR3-9',  'Workflow Status Monitoring (+ Reassignment)', 'Implemented', 'Division filter is SuperAdmin-only. List and Board are separate pages.'],
  ['FR3-10', 'Records Classification and Indexing', 'Implemented', 'No document-type/subject/source-office/reference-number fields.'],
  ['FR3-11', 'Records Search and Retrieval', 'Implemented (narrow scope)', 'Visibility is owner-only, not division-wide — confirmed even for SuperAdmin.'],
  ['FR3-12', 'Retention and Disposal Monitoring', 'Implemented (notify only)', 'No disposal action exists anywhere in code.'],
  ['FR3-13', 'Notification and Alert Management', 'Implemented', 'Correctly scoped per recipient; confirmed via live execution.'],
  ['FR3-14', 'Administrative Dashboard and Operational Reports', 'Implemented (one gap)', 'Admin dashboard may miss a Whole Office task assigned to the Admin personally.']
];

var L3_CONFLICTS = [
  ['PROJECT-STATUS.md said Reports export "does nothing (no handler)."', 'False — a working client-side CSV export exists (added commit b70b8a1, 2026-10-09). The status document predated it.'],
  ['PROJECT-STATUS.md listed "admin password reset" as ruled out of scope.', 'Contradicted by code — UserManagementController.cs:397-400 implements a working admin-triggered reset. Needs a scope decision before the defense.']
];

// Authentication (TC-AUTH) — rows with real execution evidence only.
var L3_AUTH = [
  // [id, requirement, scenario, expected, actual, status]
  ['TC-AUTH-001', 'FR3-2', 'Sign in with correct username and password', 'Redirected to the role’s own dashboard; session established', '302 Found -> /Dashboard/SuperAdminDashboard; session cookie set; dashboard load returned 200 OK', 'Passed'],
  ['TC-AUTH-002', 'FR3-2', 'Sign in with a wrong password for a real account', 'Generic "Invalid username or password"', '200 OK, body contains "Invalid username or password."', 'Passed'],
  ['TC-AUTH-003', 'FR3-2', 'Sign in with a username that doesn’t exist', 'Identical generic error to TC-AUTH-002', '200 OK, identical message', 'Passed'],
  ['TC-AUTH-004', 'FR3-2', 'Trigger account lockout (5 wrong + 1 correct password)', 'Locked-out message; correct password still rejected during cooldown', '5th attempt locked the account; 6th (correct password) attempt also refused with the same lockout message', 'Passed'],
  ['TC-AUTH-005', 'FR3-2', 'Boundary: 4th wrong attempt (not yet locked)', 'Still allowed a 5th attempt', 'All 4 attempts returned the generic error, no lockout yet', 'Passed'],
  ['TC-AUTH-006', 'FR3-2', 'First sign-in, email not yet confirmed', 'Redirected to OTP step', 'New dummy Employee account, signed in with default password before confirming email: 302 -> /Account/VerifyEmail', 'Passed'],
  ['TC-AUTH-007', 'FR3-2', 'Admin-issued temporary password sign-in', 'No OTP forced', 'Account with email already confirmed, signed in with an admin-issued temp password: 302 -> /Account/SetPassword (not VerifyEmail)', 'Passed'],
  ['TC-AUTH-009', 'FR3-2', 'Protected-page access with no session (/Tasks vs /UserManagement, /Records)', 'Redirected to Login, not a 403', 'Originally Failed: /Tasks returned 403 directly (L3-DEF-001). Fixed with [RequireLogin] on TasksController; retested 2026-10-09 — /Tasks now returns 302 -> /, matching /UserManagement and /Records. Valid SuperAdmin session still reaches /Tasks normally (200 OK).', 'Passed (fixed + retested)'],
  ['TC-AUTH-010', 'FR3-2', 'Protected-page access without permission (Employee)', 'HTTP 403, not a redirect', 'Logged-in Employee session: /UserManagement and /Tasks both returned 403 Forbidden', 'Passed'],
  ['TC-AUTH-011', 'FR3-2', 'CSRF: submit login without the anti-forgery token', 'HTTP 400 Bad Request', '400 Bad Request', 'Passed'],
  ['TC-AUTH-013', 'FR3-1 / C-2', 'Admin-triggered password reset (resolves conflict C-2)', 'A temporary password is issued', 'Admin reset a same-division Employee’s password; a real temporary password was issued and shown once — confirms this feature works despite PROJECT-STATUS.md listing it as out of scope', 'Passed']
];

// User and Role Management (TC-UM) — rows with real execution evidence only.
var L3_UM = [
  ['TC-UM-004', 'FR3-1', 'Negative: Admin attempts to create a SuperAdmin account', 'Rejected before any account is created', '302 -> /UserManagement; DB confirms zero rows created for that email', 'Passed'],
  ['TC-UM-005', 'FR3-1', 'Negative: Admin attempts to create an Employee in a different division', 'Rejected, or silently forced to the Admin’s own division', 'Rejected outright — DB confirms zero rows created', 'Passed'],
  ['TC-UM-007', 'FR3-1', 'Negative: Admin changes another user’s role via direct form submission', 'HTTP 403 — server-side guard', '403 Forbidden; target user’s role unchanged in DB', 'Passed'],
  ['TC-UM-008', 'FR3-1', 'Negative: duplicate email on account creation', 'Rejected', '302 -> /UserManagement; DB still has exactly 1 row with that email', 'Passed'],
  ['TC-UM-010', 'FR3-1', 'Access /UserManagement with no session', 'Redirected to Login, not a 403', '302 -> Location: / (correct — unlike /Tasks, see L3-DEF-001)', 'Passed']
];

// Records (TC-REC) — rows with real execution evidence only.
var L3_REC = [
  ['TC-REC-001', 'FR3-10', 'Log a record with a clear retention period', 'Retention due date computed from end of Period Covered + 3 years', 'Saved; retentionDueDate "2027-12-31" — exactly end-of-2024 + 3 years', 'Passed'],
  ['TC-REC-002', 'FR3-10', 'Negative: required field left blank', 'Rejected with a clear required-field message', '400 Bad Request, "Please fill in Code."', 'Passed'],
  ['TC-REC-003', 'FR3-10', 'Boundary: ambiguous retention period text', 'Saves anyway with no computed due date', '200 OK, saved with retentionDueDate: null', 'Passed'],
  ['TC-REC-004', 'FR3-10', 'Boundary: year-only Period Covered', 'Due date counted from end of that period', 'Confirmed via TC-REC-001’s computed result (Dec 31)', 'Passed'],
  ['TC-REC-005', 'FR3-11', 'Negative/scoping: same-division visibility (Employee A vs B)', 'B’s record not found by A', 'Two working same-division Employee sessions: A logged a record, B searched for it: 200 OK, body [] — owner-only scoping applies within a division too', 'Passed'],
  ['TC-REC-006', 'FR3-11', 'Negative/scoping: SuperAdmin searching for another user’s record', 'Not visible even to SuperAdmin', '200 OK, body [] — confirmed invisible even to SuperAdmin', 'Passed'],
  ['TC-REC-007', 'FR3-11', 'Search by code/title/location/period covered', 'Matches found for own records', 'Confirmed via GetAll?q= search by code', 'Passed'],
  ['TC-REC-010', 'FR3-12', 'Receive a disposal-due notification', 'In-app notification received', 'Live recorddisposaldue notification confirmed in feed', 'Passed'],
  ['TC-REC-011', 'FR3-14', 'Export the currently filtered Reports list', 'CSV downloads with filtered rows', 'Client-side JS (Reports CSV export button), unreachable via this session’s HTTP-only execution — confirmed working instead by the developer’s own manual test in a real browser, 2026-10-10', 'Passed'],
  ['TC-REC-013', 'FR3-14', 'Reports period filter — custom dates', 'Valid inclusive range, results match', '200 OK, both test records correctly categorized "Retained"', 'Passed'],
  ['TC-REC-014', 'FR3-14', 'Negative: custom range with from > to', 'Rejected with a clear message', '400 Bad Request, "The start date is after the end date."', 'Passed'],
  ['TC-REC-015', 'FR3-14', 'Negative: malformed date format', 'Rejected, clear message', '400 Bad Request, "The start date isn’t a valid date."', 'Passed']
];

// Notifications (TC-NOTIF) — rows with real execution evidence only.
var L3_NOTIF = [
  ['TC-NOTIF-001', 'FR3-13', 'Receive and fetch own notifications', 'Own notifications present in feed', '200 OK, exactly one notification returned, correctly scoped', 'Passed'],
  ['TC-NOTIF-002', 'FR3-13', 'Negative/scoping: a notification for another user never appears', 'Absent from the caller’s feed', 'Confirmed absent from Admin’s own feed', 'Passed'],
  ['TC-NOTIF-003', 'FR3-13', 'Negative: dismiss another user’s notification by ID', 'Rejected or no-op', '200 OK returned, but DB confirms IsDismissed stayed 0 — silently scoped to caller', 'Passed'],
  ['TC-NOTIF-005', 'FR3-13', 'Negative: admin fan-out does not leak across divisions', 'Not notified', 'SuperAdmin created a Consumer Protection Division directive: that division’s Admin was notified; the Financial and Administrative Unit Admin’s feed had no such entry', 'Passed']
];

var L3_MODULE_ROLLUP = [
  // [module, casesDefined, passed, failed, blocked, notExecuted]
  // Counts verified directly against docs/testing/level-3-system-test-cases.md
  // (2026-10-09 second pass) — every row's actual Status column was counted,
  // not estimated.
  ['Authentication (TC-AUTH)', 13, 11, 0, 0, 2],
  ['User and Role Management (TC-UM)', 10, 5, 0, 0, 5],
  ['Task Management (TC-TASK)', 20, 0, 0, 0, 20],
  ['Task Progress / Proof / Review (TC-PROOF)', 11, 0, 0, 0, 11],
  ['Records (TC-REC)', 15, 12, 0, 0, 3],
  ['Notifications (TC-NOTIF)', 8, 4, 0, 0, 4],
  ['Dashboards (TC-REPORT)', 6, 0, 0, 0, 6]
];

var L3_DEFECT = {
  id: 'L3-DEF-001',
  found_in: 'TC-AUTH-009',
  description: 'Unauthenticated access to /Tasks returned a bare HTTP 403 instead of redirecting to login. Reproduced live: an unauthenticated request to /Tasks returned 403 Forbidden, while the identical request to /UserManagement and /Records correctly returned 302 Found to the login page. Root cause: every action in TasksController (not just Index) manually checked permission and returned StatusCode(403) without first checking whether a session existed at all, unlike the RequireLogin/RequirePermission pattern used (and tested) elsewhere in the app.',
  severity: 'Medium',
  status: 'Found via live HTTP execution, 2026-10-09. Fixed the same day by adding [RequireLogin] at the class level on TasksController, matching the pattern already used by NotificationsController. Retested 2026-10-09: /Tasks now redirects correctly for an anonymous request, and a valid SuperAdmin session still reaches /Tasks normally, confirming legitimate access was not broken.'
};

var L3_PERFORMANCE = {
  existing: 'Apache JMeter, 20 concurrent threads, 1 loop each, against the live deployed site. Cold start: 0/20 errors, 15.3s avg / 27.4s max. Mid-load: 6/20 (30%) errors, 30.0s avg / 139.3s max. Warm: 0/20 errors, 1.46s avg / 2.1s max. No app-level errors in any run — every failure was a connection timeout/reset, not a 4xx/5xx.',
  newRun: 'Not executed in this pass. No unauthorized load test was run against the production site, and no local/staging test tool was set up yet.'
};

var L4_TASK_COUNTS = [
  ['SuperAdmin / OPD', 5],
  ['Admin / Division Chief', 6],
  ['Employee', 5],
  ['IT Expert', 3]
];

// Per-item ISO/IEC 25010 ratings, computed from the corrected raw response
// tally (7 of 20 targeted respondents; counts are [5,4,3,2,1]).
var L4_ITEMS = [
  // [part, no, statement, c5, c4, c3, c2, c1]
  ['I. Functional suitability', '1.1', 'The system has the functions our office needs to manage tasks.', 4, 2, 1, 0, 0],
  ['I. Functional suitability', '1.2', 'The system has the functions our office needs to manage the records masterlist.', 6, 1, 0, 0, 0],
  ['I. Functional suitability', '1.3', 'The system shows correct information (task status, progress, due dates, retention dates).', 6, 1, 0, 0, 0],
  ['I. Functional suitability', '1.4', 'The functions fit the way our office actually works.', 5, 2, 0, 0, 0],
  ['I. Functional suitability', '1.5', 'Notifications and reminders reach the right people about the right things.', 5, 2, 0, 0, 0],
  ['II. Performance efficiency', '2.1', 'Pages open quickly enough for daily use.', 5, 2, 0, 0, 0],
  ['II. Performance efficiency', '2.2', 'Saving a task, update or record finishes quickly.', 5, 2, 0, 0, 0],
  ['II. Performance efficiency', '2.3', 'Searching for tasks or records gives results quickly.', 6, 1, 0, 0, 0],
  ['II. Performance efficiency', '2.4', 'The system can handle the number of staff, tasks and records in our office.', 5, 2, 0, 0, 0],
  ['III. Compatibility', '3.1', 'The system works in the web browser we normally use.', 5, 2, 0, 0, 0],
  ['III. Compatibility', '3.2', 'The masterlist exported to Excel can be used with our other office files.', 3, 4, 0, 0, 0],
  ['III. Compatibility', '3.3', 'The system can be used alongside our other work programs without problems.', 4, 3, 0, 0, 0],
  ['IV. Usability', '4.1', 'I can tell what each screen is for.', 4, 2, 1, 0, 0],
  ['IV. Usability', '4.2', 'I could learn to use the system with a short orientation.', 6, 0, 1, 0, 0],
  ['IV. Usability', '4.3', 'Buttons, menus and messages are clear to me.', 6, 0, 1, 0, 0],
  ['IV. Usability', '4.4', 'The system asks me to confirm before an action that is hard to undo.', 4, 3, 0, 0, 0],
  ['IV. Usability', '4.5', 'The screens look clean and consistent.', 6, 1, 0, 0, 0],
  ['IV. Usability', '4.6', 'Text and buttons are large enough to read and click comfortably.', 5, 2, 0, 0, 0],
  ['V. Reliability', '5.1', 'The system worked without errors during the demonstration.', 3, 3, 1, 0, 0],
  ['V. Reliability', '5.2', 'The system would be available whenever I need it during work hours.', 4, 3, 0, 0, 0],
  ['V. Reliability', '5.3', 'A mistake in entering information does not cause lost work.', 4, 3, 0, 0, 0],
  ['V. Reliability', '5.4', 'Our office’s data can be backed up and recovered if something goes wrong.', 4, 2, 1, 0, 0],
  ['VI. Security', '6.1', 'Each person sees only the tasks and records their role allows.', 4, 3, 0, 0, 0],
  ['VI. Security', '6.2', 'Only authorized people can change a task or record.', 5, 2, 0, 0, 0],
  ['VI. Security', '6.3', 'The system records who did what and when.', 5, 2, 0, 0, 0],
  ['VI. Security', '6.4', 'Signing in protects my account (password, emailed code, lock after repeated wrong passwords).', 5, 2, 0, 0, 0],
  ['VI. Security', '6.5', 'I would trust the system with our office’s information.', 3, 4, 0, 0, 0],
  ['VII. Maintainability', '7.1', 'Staff accounts, roles and divisions can be updated by our office without outside help.', 4, 3, 0, 0, 0],
  ['VII. Maintainability', '7.2', 'When a problem happens, the message helps us understand what went wrong.', 5, 2, 0, 0, 0],
  ['VII. Maintainability', '7.3', 'The system could be updated when our office’s needs change.', 6, 1, 0, 0, 0],
  ['VIII. Portability', '8.1', 'The system can be used on desktop computers, laptops, tablets and phones.', 6, 1, 0, 0, 0],
  ['VIII. Portability', '8.2', 'The system needs only a web browser; nothing has to be installed.', 6, 1, 0, 0, 0],
  ['VIII. Portability', '8.3', 'The system could replace our current way of tracking tasks and records.', 5, 1, 1, 0, 0],
  ['IX. Overall acceptance', '9.1', 'Overall, the system meets the needs of our office.', 5, 2, 0, 0, 0],
  ['IX. Overall acceptance', '9.2', 'I would use the system in my regular work if it is adopted.', 4, 3, 0, 0, 0],
  ['IX. Overall acceptance', '9.3', 'I would recommend that our office adopt the system.', 6, 1, 0, 0, 0]
];

var L4_RESPONDENTS = '7 of 20 targeted';

var L4_LIMITATIONS = [
  'Only 7 of 20 targeted DTI Laguna personnel have been surveyed so far; the means below are descriptive of those 7, not generalized to the rest of the office.',
  'The raw data is a per-item response tally (count of respondents choosing each rating), not individually identified per-respondent rows, so no per-respondent breakdown table is produced here.',
  'No role-based task-completion session has been run yet — every task in the table below is still Pending UAT.',
  'The IT Expert technical evaluation has not been run at all; CreateQuestionnaireForm.gs still needs its placeholders filled in and the form generated and sent.',
  'No client acceptance decision has been recorded yet.'
];
// ──────────────────────────────────────────────────────────────────────────

function createLevel3Level4Doc() {
  var doc = DocumentApp.create(DOC_TITLE);
  var body = doc.getBody();
  body.setMarginTop(54).setMarginBottom(54).setMarginLeft(72).setMarginRight(72);

  body.appendParagraph(DOC_TITLE).setHeading(DocumentApp.ParagraphHeading.TITLE);
  body.appendParagraph(PROJECT_TITLE).setItalic(true);
  body.appendParagraph(
    SCHOOL + ' — ' + PROGRAM + '\n' +
    'Researchers: ' + RESEARCHERS + '\n' +
    'Adviser: ' + ADVISER + '\n' +
    'Report generated: ' + REPORT_DATE);

  body.appendParagraph(
    'This report presents Level 3 (black-box system testing via the UI/HTTP) and ' +
    'Level 4 (user acceptance testing) for DTI OneLink, following unit testing (Level ' +
    '1) and integration testing (Level 2), which are documented separately in ' +
    'docs/testing/four-levels-of-testing.md. Figures here were computed from genuine ' +
    'execution evidence or raw response data — nothing in this report is an estimate.');

  // ───────────────────────── LEVEL 3 ─────────────────────────
  body.appendPageBreak();
  body.appendParagraph('Level 3 — System Testing').setHeading(DocumentApp.ParagraphHeading.HEADING1);
  body.appendParagraph(
    'Black-box testing performed via real HTTP requests against the running application ' +
    '(session-authenticated, with genuine anti-forgery tokens) — not a rendered browser, ' +
    'so purely client-side JavaScript behavior could not be exercised this way; the one ' +
    'such case (TC-REC-011) was instead confirmed by a manual browser test. ' +
    'Status key: Passed, Failed, Blocked, Not Executed. A case is never credited as ' +
    'Passed on the strength of a related Level 1/2 automated test.'
  ).setItalic(true);

  body.appendParagraph('Requirements Traceability').setHeading(DocumentApp.ParagraphHeading.HEADING2);
  appendTraceabilityTable(body, L3_TRACEABILITY);
  tableCaption(body, 'Table 1. Level 3 requirements traceability (FR3-1 to FR3-14)');

  body.appendParagraph('Conflicts Found Between Project Documents and Current Code').setHeading(DocumentApp.ParagraphHeading.HEADING2);
  appendTwoColTable(body, ['Document claim', 'Code reality'], L3_CONFLICTS);
  tableCaption(body, 'Table 2. Documentation-vs-code conflicts found during traceability analysis');

  body.appendParagraph('1. Authentication (TC-AUTH)').setHeading(DocumentApp.ParagraphHeading.HEADING2);
  appendCaseTable(body, L3_AUTH);
  tableCaption(body, 'Table 3. Level 3 system testing — Authentication');

  body.appendParagraph('2. User and Role Management (TC-UM)').setHeading(DocumentApp.ParagraphHeading.HEADING2);
  appendCaseTable(body, L3_UM);
  tableCaption(body, 'Table 4. Level 3 system testing — User and Role Management');

  body.appendParagraph('3. Records (TC-REC)').setHeading(DocumentApp.ParagraphHeading.HEADING2);
  appendCaseTable(body, L3_REC);
  tableCaption(body, 'Table 5. Level 3 system testing — Records');

  body.appendParagraph('4. Notifications (TC-NOTIF)').setHeading(DocumentApp.ParagraphHeading.HEADING2);
  appendCaseTable(body, L3_NOTIF);
  tableCaption(body, 'Table 6. Level 3 system testing — Notifications');

  body.appendParagraph(
    'Task Management (TC-TASK, 20 cases), Task Progress/Proof/Review (TC-PROOF, 11 ' +
    'cases) and Dashboards (TC-REPORT, 6 cases) are fully defined in ' +
    'docs/testing/level-3-system-test-cases.md but have no execution evidence yet — ' +
    'they are included in the rollup below as Not Executed, not claimed as passing.'
  );

  body.appendParagraph('Rolled Up Per Module').setHeading(DocumentApp.ParagraphHeading.HEADING2);
  appendModuleRollupTable(body, L3_MODULE_ROLLUP);
  tableCaption(body, 'Table 7. Level 3 system testing — cases per module');

  body.appendParagraph('Confirmed Defect').setHeading(DocumentApp.ParagraphHeading.HEADING2);
  appendDefectTable(body, L3_DEFECT);
  tableCaption(body, 'Table 8. Confirmed Level 3 defect');

  body.appendParagraph('Performance Testing').setHeading(DocumentApp.ParagraphHeading.HEADING2);
  var perfHeader = body.appendParagraph('Existing report (prior to this pass):');
  perfHeader.setBold(true);
  body.appendParagraph(L3_PERFORMANCE.existing);
  var perfNew = body.appendParagraph('New testing in this pass:');
  perfNew.setBold(true);
  body.appendParagraph(L3_PERFORMANCE.newRun);

  // ───────────────────────── LEVEL 4 ─────────────────────────
  body.appendPageBreak();
  body.appendParagraph('Level 4 — User Acceptance Testing').setHeading(DocumentApp.ParagraphHeading.HEADING1);
  body.appendParagraph(
    'Three distinct kinds of Level 4 evidence are tracked separately and should not be ' +
    'blended: (1) role-based task completion observed by a facilitator, (2) user feedback ' +
    'and acceptance decisions in participants’ own words, and (3) ISO/IEC 25010 ' +
    'questionnaire ratings.'
  ).setItalic(true);

  body.appendParagraph('Role-Based Task Scripts').setHeading(DocumentApp.ParagraphHeading.HEADING2);
  appendCountTable(body, ['Role', 'Tasks scripted'], L4_TASK_COUNTS);
  tableCaption(body, 'Table 9. Level 4 role-based task scripts, status: Pending UAT');
  body.appendParagraph('Every task above is defined and ready, but none has been attempted by an actual participant with a facilitator present yet — see docs/testing/level-4-uat-task-scripts.md for the full steps and acceptance criteria.');

  body.appendParagraph('Respondents').setHeading(DocumentApp.ParagraphHeading.HEADING2);
  body.appendParagraph(
    L4_RESPONDENTS + ' responded to the ISO/IEC 25010 survey as of this report. All ' +
    'ratings were computed directly from the raw per-item response tally in ' +
    'Respondents.xlsx (counts of how many of the 7 respondents chose each rating), not ' +
    'from a previously summarized figure.'
  );

  body.appendParagraph('ISO/IEC 25010 Ratings Per Statement').setHeading(DocumentApp.ParagraphHeading.HEADING2);
  appendIso25010ItemTable(body, L4_ITEMS);
  tableCaption(body, 'Table 10. Individual ratings, frequency of each rating, and weighted mean per statement (N = 7)');

  body.appendParagraph(
    'WM = Σ(f × x) / N, where f is the number of respondents who chose a rating, x ' +
    'is the rating value and N = 7.'
  ).setItalic(true);

  body.appendParagraph('Summary Per Quality Characteristic').setHeading(DocumentApp.ParagraphHeading.HEADING2);
  var partSummary = computePartSummary(L4_ITEMS);
  appendPartSummaryTable(body, partSummary);
  tableCaption(body, 'Table 11. Summary of the ISO/IEC 25010 evaluation');

  var overallPara = body.appendParagraph(
    'Overall weighted mean (mean of the 9 part means): ' + partSummary.overall.toFixed(2) + '.'
  );
  overallPara.setBold(true);

  body.appendParagraph('Known Limitations').setHeading(DocumentApp.ParagraphHeading.HEADING2);
  L4_LIMITATIONS.forEach(function (item) {
    body.appendListItem(item).setGlyphType(DocumentApp.GlyphType.BULLET);
  });

  // ───────────────────────── SUMMARY / CONCLUSION / RECOMMENDATION ─────────────────────────
  body.appendPageBreak();
  body.appendParagraph('Summary').setHeading(DocumentApp.ParagraphHeading.HEADING1);
  var l3Totals = sumRollup(L3_MODULE_ROLLUP);
  body.appendParagraph(
    'Level 3 system testing covered ' + l3Totals.cases + ' defined cases across 7 ' +
    'modules. Of these, ' + l3Totals.passed + ' were executed and passed, including ' +
    'TC-AUTH-009 (initially failed as L3-DEF-001, fixed, and passed on retest), six ' +
    'Employee- and cross-division-dependent cases unblocked in a second pass by using ' +
    'the app’s own admin-triggered password reset to obtain working Employee ' +
    'credentials rather than fabricating results, and TC-REC-011 (the Reports CSV ' +
    'export, client-side JavaScript unreachable by this session’s HTTP-only ' +
    'execution), confirmed instead by the developer’s own manual test in a real ' +
    'browser. No case was marked Passed on the strength of a Level 1/2 automated test ' +
    'alone. The remaining ' + l3Totals.notExecuted + ' cases are defined but not yet run.'
  );
  body.appendParagraph(
    'Level 4 user acceptance testing has one completed leg (the ISO/IEC 25010 survey, ' +
    L4_RESPONDENTS + ', overall weighted mean ' + partSummary.overall.toFixed(2) +
    ') and three legs not yet started: role-based task completion with a facilitator, ' +
    'the separate IT Expert technical evaluation, and the formal client acceptance ' +
    'decision.'
  );

  body.appendParagraph('Conclusion').setHeading(DocumentApp.ParagraphHeading.HEADING1);
  body.appendParagraph(
    'The Level 3 results executed so far support the conclusion that the tested ' +
    'requirements — authentication, user and role management, records classification ' +
    'and retrieval, and notification scoping — behave as specified. One defect ' +
    '(L3-DEF-001) was found during execution, a consistency gap rather than a security ' +
    'hole (the affected routes still denied access correctly; they simply did not ' +
    'redirect an anonymous visitor to the login page the way every other protected ' +
    'route does). It was fixed the same day by adding the existing [RequireLogin] ' +
    'filter at the class level, and the fix was confirmed on retest.'
  );
  body.appendParagraph(
    'The Level 4 ISO/IEC 25010 ratings from the 7 respondents surveyed so far are ' +
    'positive across all nine parts, with no part averaging below 4.4. This conclusion ' +
    'applies only to those 7 respondents; it does not establish the acceptance of DTI ' +
    'Laguna Provincial Office as a whole, which still requires completing the remaining ' +
    'respondents, the IT Expert evaluation, and a signed client acceptance decision.'
  );

  body.appendParagraph('Recommendation').setHeading(DocumentApp.ParagraphHeading.HEADING1);
  var recs = [
    'Run the remaining Level 3 cases for Task Management, Task Progress/Proof/Review, and Dashboards, which currently have no execution evidence.',
    'Complete the ISO/IEC 25010 survey with the remaining 13 of 20 targeted respondents before citing the overall weighted mean as final.',
    'Run the separate IT Expert technical evaluation (CreateQuestionnaireForm.gs) once its project-detail placeholders are filled in.',
    'Conduct the role-based task-completion sessions with a facilitator present, and record the results in level-4-uat-results-template.md.',
    'Obtain a signed client acceptance decision from an authorized DTI Laguna representative using level-4-client-acceptance-form.md.'
  ];
  recs.forEach(function (item) {
    body.appendListItem(item).setGlyphType(DocumentApp.GlyphType.NUMBER);
  });

  doc.saveAndClose();
  Logger.log('Testing report document: ' + doc.getUrl());
}

// ── computation helpers ──────────────────────────────────────────────────

function itemMean(row) {
  var c5 = row[3], c4 = row[4], c3 = row[5], c2 = row[6], c1 = row[7];
  var n = c5 + c4 + c3 + c2 + c1;
  var wsum = 5 * c5 + 4 * c4 + 3 * c3 + 2 * c2 + 1 * c1;
  return n ? wsum / n : 0;
}

function computePartSummary(items) {
  var parts = {}; // part -> {sum, count}
  var order = [];
  items.forEach(function (row) {
    var part = row[0];
    if (!parts[part]) { parts[part] = { sum: 0, count: 0 }; order.push(part); }
    parts[part].sum += itemMean(row);
    parts[part].count += 1;
  });
  var rows = order.map(function (part) {
    var mean = parts[part].sum / parts[part].count;
    return [part, parts[part].count, mean];
  });
  var overall = rows.reduce(function (acc, r) { return acc + r[2]; }, 0) / rows.length;
  return { rows: rows, overall: overall };
}

function sumRollup(rollup) {
  var totals = { cases: 0, passed: 0, failed: 0, blocked: 0, notExecuted: 0 };
  rollup.forEach(function (r) {
    totals.cases += r[1];
    totals.passed += r[2];
    totals.failed += r[3];
    totals.blocked += r[4];
    totals.notExecuted += r[5];
  });
  return totals;
}

// ── table helpers ──────────────────────────────────────────────────────

function tableCaption(body, text) {
  var p = body.appendParagraph(text);
  p.setItalic(true);
  p.setAlignment(DocumentApp.HorizontalAlignment.CENTER);
  p.setFontSize(9);
  body.appendParagraph('');
}

function appendTraceabilityTable(body, rows) {
  var table = body.appendTable([['ID', 'Requirement', 'Status', 'Note']].concat(rows));
  styleHeaderRow(table.getRow(0));
  table.setColumnWidth(0, 50);
  table.setColumnWidth(1, 150);
  table.setColumnWidth(2, 90);
  table.setColumnWidth(3, 178);
  shrinkBodyText(table, 9);
  return table;
}

function appendTwoColTable(body, headers, rows) {
  var table = body.appendTable([headers].concat(rows));
  styleHeaderRow(table.getRow(0));
  table.setColumnWidth(0, 220);
  table.setColumnWidth(1, 248);
  shrinkBodyText(table, 10);
  return table;
}

function appendCountTable(body, headers, rows) {
  var table = body.appendTable([headers].concat(rows.map(function (r) { return [r[0], String(r[1])]; })));
  styleHeaderRow(table.getRow(0));
  return table;
}

// ID | Requirement | Test Scenario | Expected Result | Actual Result | Status
function appendCaseTable(body, rows) {
  var header = ['Test Case ID', 'Requirement', 'Test Scenario', 'Expected Result', 'Actual Result', 'Status'];
  var table = body.appendTable([header].concat(rows));
  styleHeaderRow(table.getRow(0));
  table.setColumnWidth(0, 72);
  table.setColumnWidth(1, 50);
  table.setColumnWidth(2, 118);
  table.setColumnWidth(3, 110);
  table.setColumnWidth(4, 118);
  table.setColumnWidth(5, 50);
  shrinkBodyText(table, 8);
  colorStatusColumn(table, 5);
  return table;
}

function appendModuleRollupTable(body, rollup) {
  // [module, casesDefined, passed, failed, blocked, notExecuted] -> displayed
  // as Module | Cases | Passed 1st run | Failed | After retest | Final.
  // Only L3-DEF-001 (TC-AUTH-009, in Authentication) was ever Failed and then
  // fixed/retested, so that is the only module with a nonzero Failed/After
  // retest split; every other module's Final count was reached on the first
  // run (Passed 1st run = Final).
  var header = ['Module', 'Cases', 'Passed 1st run', 'Failed', 'After retest', 'Final'];
  var rows = rollup.map(function (r) {
    var isAuth = r[0].indexOf('Authentication') === 0;
    var failed = isAuth ? 1 : 0;
    var afterRetest = isAuth ? 1 : 0;
    var finalPassed = r[2];
    var passed1st = finalPassed - afterRetest;
    return [r[0], String(r[1]), String(passed1st), String(failed), String(afterRetest), String(finalPassed)];
  });
  var totals = sumRollup(rollup);
  var totalFailed = 1, totalAfterRetest = 1;
  rows.push(['Total', String(totals.cases), String(totals.passed - totalAfterRetest), String(totalFailed), String(totalAfterRetest), String(totals.passed)]);
  var table = body.appendTable([header].concat(rows));
  styleHeaderRow(table.getRow(0));
  var lastRow = table.getRow(table.getNumRows() - 1);
  for (var c = 0; c < lastRow.getNumCells(); c++) {
    lastRow.getCell(c).getChild(0).asParagraph().setBold(true);
  }
  shrinkBodyText(table, 9);
  return table;
}

function appendDefectTable(body, defect) {
  var rows = [
    ['Defect ID', defect.id],
    ['Found in', defect.found_in],
    ['Description', defect.description],
    ['Severity', defect.severity],
    ['Status', defect.status]
  ];
  var table = body.appendTable(rows);
  table.setColumnWidth(0, 100);
  table.setColumnWidth(1, 368);
  for (var r = 0; r < table.getNumRows(); r++) {
    table.getRow(r).getCell(0).setBackgroundColor('#dce1ff');
    table.getRow(r).getCell(0).getChild(0).asParagraph().setBold(true);
  }
  shrinkBodyText(table, 10);
  return table;
}

// No. | Part | Statement | 5 | 4 | 3 | 2 | 1 | WM
function appendIso25010ItemTable(body, items) {
  var header = ['No.', 'Statement', '5', '4', '3', '2', '1', 'WM'];
  var rows = items.map(function (row) {
    var mean = itemMean(row);
    return [row[1], row[2], String(row[3]), String(row[4]), String(row[5]), String(row[6]), String(row[7]), mean.toFixed(2)];
  });
  var table = body.appendTable([header].concat(rows));
  styleHeaderRow(table.getRow(0));
  table.setColumnWidth(0, 30);
  table.setColumnWidth(1, 270);
  for (var c = 2; c < 7; c++) table.setColumnWidth(c, 24);
  table.setColumnWidth(7, 40);
  shrinkBodyText(table, 8);
  return table;
}

function appendPartSummaryTable(body, summary) {
  var header = ['Quality Characteristic', 'Statements', 'Weighted Mean', 'Interpretation'];
  var rows = summary.rows.map(function (r) {
    return [r[0].replace(/^[IVX]+\.\s*/, ''), String(r[1]), r[2].toFixed(2), interpret(r[2])];
  });
  rows.push(['Overall', '36', summary.overall.toFixed(2), interpret(summary.overall)]);
  var table = body.appendTable([header].concat(rows));
  styleHeaderRow(table.getRow(0));
  var lastRow = table.getRow(table.getNumRows() - 1);
  for (var c = 0; c < lastRow.getNumCells(); c++) {
    lastRow.getCell(c).getChild(0).asParagraph().setBold(true);
  }
  shrinkBodyText(table, 10);
  return table;
}

function interpret(mean) {
  if (mean >= 4.5) return 'Strongly Agree';
  if (mean >= 3.5) return 'Agree';
  if (mean >= 2.5) return 'Neither Agree nor Disagree';
  if (mean >= 1.5) return 'Disagree';
  return 'Strongly Disagree';
}

function colorStatusColumn(table, colIndex) {
  for (var r = 1; r < table.getNumRows(); r++) {
    var cell = table.getRow(r).getCell(colIndex);
    var text = cell.getText();
    var color = '#000000';
    if (text.indexOf('Passed') !== -1) color = '#188038';
    else if (text.indexOf('Failed') !== -1) color = '#d93025';
    else if (text.indexOf('Blocked') !== -1) color = '#e37400';
    cell.getChild(0).asParagraph().editAsText().setForegroundColor(color).setBold(true);
  }
}

function shrinkBodyText(table, size) {
  for (var r = 1; r < table.getNumRows(); r++) {
    var row = table.getRow(r);
    for (var c = 0; c < row.getNumCells(); c++) {
      row.getCell(c).getChild(0).asParagraph().setFontSize(size);
    }
  }
}

function styleHeaderRow(row) {
  for (var i = 0; i < row.getNumCells(); i++) {
    var cell = row.getCell(i);
    cell.setBackgroundColor('#dce1ff'); // the app's light navy tint
    cell.getChild(0).asParagraph().setBold(true);
  }
}

/**
 * DTI OneLink "Design of Software, System, Product, and/or Processes" -> Google Docs
 * A formatted, print-ready document built from docs/design-and-development.md
 * — for the manuscript's Methodology chapter, or for sharing with the
 * adviser/panel.
 *
 * Re-run this script (updating the DATA section below) whenever that
 * markdown file changes materially — it does not read it live.
 *
 * HOW TO USE (about 2 minutes):
 *   1. Go to https://script.google.com and click "New project".
 *   2. Delete the sample code, paste ALL of this file, and click Save.
 *   3. Fill in the project details below (between the quotes).
 *   4. Choose the function "createDesignDevelopmentDoc" at the top and click Run.
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

var DOC_TITLE = 'Design of Software, System, Product, and/or Processes: DTI OneLink';

// ── DATA: keep this in sync with docs/design-and-development.md by hand ──

var INTRO =
  'The development of DTI OneLink followed the Agile Methodology, organized into ' +
  'repeating Sprints, each moving through seven stages: Plan, Design, Develop, Test, ' +
  'Deploy, Review, and Launch (see Figure 1. Agile Methodology). Rather than building ' +
  'the entire system in one continuous phase, the project was divided into Sprints, ' +
  'each delivering one working feature, module, or fix — aligned with DTI Laguna ' +
  'Provincial Office’s actual task-coordination and records-management needs — ' +
  'before the next Sprint began.';

var CYCLE_STEPS = [
  ['Plan', 'At the start of each Sprint, the team identified a single feature, module, or defect to address — for example, "add task reassignment within a division" or "fix the login outage caused by a SQL dependency conflict." Scope was deliberately kept narrow per Sprint so that each change could be fully understood, tested, and reasoned about on its own.'],
  ['Design', 'Before coding began, the team worked out how the feature fit the existing system: what database schema changes it needed (expressed later as an EF Core migration), which role (SuperAdmin, Admin, or Employee) it affected, and how it fit the existing workflow rules — for example, deciding that a task reassignment should carry over progress and notify both the outgoing and incoming assignee, before any of that was implemented.'],
  ['Develop', 'The planned design was implemented directly against the existing codebase: an ASP.NET Core MVC application with Entity Framework Core over Microsoft SQL Server, using session-based authentication and three roles (SuperAdmin, Admin, Employee). Each Sprint produced a working increment of the software that was functional and aligned with the overall project objectives.'],
  ['Test', 'Each Sprint’s output was verified before being considered done — through unit tests, integration tests, and, where the change affected user-facing behavior, a black-box system-testing pass executed against the running application. Defects found during this step were logged, fixed, and retested with the same test case before being closed, rather than assumed fixed.'],
  ['Deploy', 'Every change that passed testing was published through a GitHub Actions workflow that builds and deploys the application to MonsterASP (Web Deploy), so each Sprint reached the live environment without manual file copying or server access.'],
  ['Review', 'At the end of each Sprint, the team reviewed what was completed — against commit history, requirements documentation, and status notes — and, where applicable, against feedback from DTI Laguna staff, to assess what succeeded and what needed to carry into the next Sprint.'],
  ['Launch', 'Once reviewed, the increment became part of the live system actually available to DTI Laguna users, closing the loop before the next Sprint’s Plan stage began. The on-site demonstration and the ongoing ISO/IEC 25010 user-acceptance evaluation are the clearest example of this stage at a project-wide scale, rather than per individual Sprint.']
];
// ──────────────────────────────────────────────────────────────────────────

function createDesignDevelopmentDoc() {
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

  body.appendParagraph('Development Methodology').setHeading(DocumentApp.ParagraphHeading.HEADING1);
  body.appendParagraph(INTRO);

  CYCLE_STEPS.forEach(function (step) {
    body.appendParagraph(step[0]).setHeading(DocumentApp.ParagraphHeading.HEADING2);
    body.appendParagraph(step[1]);
  });

  doc.saveAndClose();
  Logger.log('Design and Development document: ' + doc.getUrl());
}

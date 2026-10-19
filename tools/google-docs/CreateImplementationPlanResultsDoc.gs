/**
 * DTI OneLink Implementation Plan and Implementation Results -> Google Docs
 * A formatted, print-ready document built from docs/implementation-plan.md and
 * docs/implementation-results.md — for the manuscript's Results and
 * Discussion chapter, or for sharing with the adviser/panel.
 *
 * Re-run this script (updating the DATA section below) whenever those two
 * markdown files change materially — it does not read them live.
 *
 * HOW TO USE (about 2 minutes):
 *   1. Go to https://script.google.com and click "New project".
 *   2. Delete the sample code, paste ALL of this file, and click Save.
 *   3. Fill in the project details below (between the quotes).
 *   4. Choose the function "createImplementationDoc" at the top and click Run.
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

var DOC_TITLE = 'Implementation Plan and Implementation Results: DTI OneLink';

// ── DATA: keep this in sync with docs/implementation-plan.md and ────────
// ── docs/implementation-results.md by hand ───────────────────────────────

var PLAN_OVERVIEW = [
  'DTI OneLink addresses the fragmented task follow-ups, manual spreadsheet-based ' +
  'records tracking, and lack of centralized monitoring at DTI Laguna Provincial ' +
  'Office. The system centralizes task assignment, monitoring, and deadline ' +
  'prioritization; records classification and retention tracking; and automated ' +
  'in-app/email notifications into one role-based web application for OPD, division ' +
  'chiefs, and employees across the Business Development Division, Financial and ' +
  'Administrative Unit, and Consumer Protection Division.',
  'Because the office has no dedicated IT staff, the system was built to run on ' +
  'managed cloud hosting rather than office-owned hardware, so day-to-day use needs ' +
  'nothing beyond a web browser.'
];

var PLAN_TASKS = [
  ['Requirements Gathering and Analysis', 'Consulted OPD staff, division chiefs, and employees to identify task-coordination and records-retention pain points, and confirmed the system’s boundaries (e.g., retention monitoring is notification-only, not automated disposal; attendance tracking is out of scope).'],
  ['System Design and Development', 'Built on ASP.NET Core MVC with Entity Framework Core over Microsoft SQL Server, using session-based authentication and three roles (SuperAdmin, Admin, Employee). Schema changes are applied automatically through EF Core migrations at startup, with an idempotent-SQL fallback for the hosted environment.'],
  ['Continuous Deployment', 'A GitHub Actions workflow builds and publishes every merged change to MonsterASP (Web Deploy); Brevo delivers OTP and notification emails. Neither requires the office to operate its own server or mail system.'],
  ['Internal Testing', 'Verified the system through unit tests, integration tests, a black-box system-testing pass executed against the running application, and a user-acceptance evaluation.'],
  ['On-Site Demonstration and Evaluation', 'Conducted a live demonstration and hands-on walkthrough at DTI Laguna Provincial Office, where staff used the Dashboard, Task Management, and Records Management modules before completing the ISO/IEC 25010 evaluation instrument.']
];

var PLAN_RESOURCES = [
  ['Client hardware', 'Any office computer, laptop, or mobile device with a modern web browser'],
  ['Server hardware', 'None — hosting is managed by the provider (MonsterASP); the office owns no server'],
  ['Hosting', 'MonsterASP (ASP.NET Core hosting), deployed via GitHub Actions'],
  ['Database', 'Microsoft SQL Server, managed by the hosting provider'],
  ['Email', 'Brevo (transactional email API) for OTP and notification delivery'],
  ['Network', 'A standard office internet connection'],
  ['Personnel', 'OPD (SuperAdmin), Division Chiefs (Admin), employees (Employee) — no dedicated IT staff needed']
];

var PLAN_CONVERSION =
  'Pilot evaluation. DTI OneLink does not replace one single prior system, so there is ' +
  'no fixed-date parallel cutover. Adoption is validated through the live ' +
  'demonstration and ongoing ISO/IEC 25010 survey (currently 7 of 20 targeted ' +
  'respondents), with the remaining respondents, the IT Expert evaluation, and a ' +
  'signed client acceptance decision as the gate before divisions retire their ' +
  'previous manual tracking methods.';

var PLAN_SITE_REQUIREMENTS = [
  'No local server room, UPS, or LAN infrastructure is needed — hosting is fully managed.',
  'Hosting and email credentials (GitHub, MonsterASP, Brevo) must be formally handed over to whoever maintains the system after the capstone concludes, since DTI Laguna has no in-house IT staff.',
  'The system is unusable during an office-side internet outage, a deliberate trade-off given the office’s lack of IT staff to maintain local infrastructure.',
  'Backups rely on the hosting provider’s and database server’s own facilities; the application has no separate in-app backup/restore feature.'
];

var RESULTS_RESPONDENTS =
  'The evaluation instrument was distributed to DTI Laguna Provincial Office’s full ' +
  'targeted population of 20 personnel. Seven (7) respondents completed and returned ' +
  'the survey within the data-gathering period so far — a 35% response rate. The ' +
  'remaining thirteen were not available to respond before the cut-off for this draft ' +
  'and are not represented in the figures below. This should be reported honestly as ' +
  'n = 7 of 20, a limitation of the evaluation, rather than projected or estimated for ' +
  'staff who were not surveyed.';

var RESULTS_INSTRUMENT =
  'The system was evaluated using a researcher-made questionnaire based on the ' +
  'ISO/IEC 25010 software quality model, covering nine characteristics: ' +
  'Functionality, Performance Efficiency, Compatibility, Usability, Reliability, ' +
  'Security, Maintainability, Portability, and Overall Acceptance. Each ' +
  'characteristic was broken down into 3–6 indicator items, rated on a 5-point ' +
  'Likert scale (5 = Strongly Agree, 1 = Strongly Disagree, with an N/A option ' +
  'excluded from the computation). Responses were scored using the weighted mean: ' +
  'WM = Σ(rating × number of respondents who chose that rating) / respondents who ' +
  'rated that item.';

var RESULTS_INTERPRETATION = [
  ['4.21 – 5.00', 'Strongly Agree', 'Excellent'],
  ['3.41 – 4.20', 'Agree', 'Very Satisfactory'],
  ['2.61 – 3.40', 'Neutral', 'Satisfactory'],
  ['1.81 – 2.60', 'Disagree', 'Fair'],
  ['1.00 – 1.80', 'Strongly Disagree', 'Poor']
];

var RESULTS_CHARACTERISTICS = [
  ['Functionality', '4.71'],
  ['Performance Efficiency', '4.75'],
  ['Compatibility', '4.57'],
  ['Usability', '4.67'],
  ['Reliability', '4.46'],
  ['Security', '4.63'],
  ['Maintainability', '4.71'],
  ['Portability', '4.76'],
  ['Overall Acceptance', '4.71']
];
var RESULTS_OVERALL_MEAN = '4.67';

var RESULTS_DISCUSSION =
  'All nine ISO/IEC 25010 characteristics scored within the Strongly Agree / ' +
  'Excellent range (4.21–5.00), with an overall weighted mean of 4.67. Among ' +
  'respondents, Portability (4.76) scored highest, followed by Performance ' +
  'Efficiency (4.75), while Reliability (4.46) scored comparatively lowest — ' +
  'driven mainly by item 5.1 ("the system worked without errors during the ' +
  'demonstration"), where ratings were more spread out than on other items (three ' +
  '5s, three 4s, one 3) rather than concentrated at the top. This suggests ' +
  'Reliability-related behavior during live demonstrations is the area most worth ' +
  'revisiting before full rollout, even though it still falls in the "Excellent" band.\n\n' +
  'The results indicate that, among the DTI Laguna personnel who were able to ' +
  'evaluate the system, DTI OneLink was very positively received across functional ' +
  'correctness, performance, compatibility, usability, reliability, security, ' +
  'maintainability, portability, and overall acceptance. However, because only 7 of ' +
  'the 20 targeted personnel responded, these results should be read as an early, ' +
  'favorable indication rather than a conclusive office-wide evaluation. The ' +
  'researchers recommend collecting responses from the remaining personnel — ' +
  'particularly staff who were not present during the on-site demonstration — ' +
  'before treating this evaluation as final.';

var RESULTS_SUPPORTING_EVIDENCE =
  'The research team presented the Task Board and live dashboard to DTI Laguna OPD ' +
  'staff, division personnel, and faculty panelists at the Provincial Office, ' +
  'giving respondents direct, hands-on exposure to the system before rating it — ' +
  'contextual evidence of the evaluation process, not data in itself.';

var RESULTS_OPERATIONAL_NOTE =
  'A formal, timed before-and-after comparison (e.g., average time to locate a ' +
  'record, number of missed task follow-ups per month, under the previous manual ' +
  'process versus with DTI OneLink) has not been conducted. Unlike the ISO/IEC ' +
  '25010 evaluation above, no quantitative operational data has been collected from ' +
  'a real, sustained pilot period yet — the on-site session so far was a one-time ' +
  'demonstration and survey, not a multi-week parallel run.\n\n' +
  'To produce this comparison honestly rather than estimating it, the office would ' +
  'need to run DTI OneLink alongside its existing manual process for a defined ' +
  'pilot period and record, for the same workload, metrics such as time to locate a ' +
  'record, number of task deadlines missed or discovered late per month, and number ' +
  'of records whose retention status required manual lookup versus flagged ' +
  'automatically. Until that pilot period happens and these are measured, this ' +
  'section should not claim specific improvement percentages — doing so without ' +
  'real measurements would misrepresent the evaluation. The ISO/IEC 25010 results ' +
  'above remain the system’s primary evidence of acceptability until an ' +
  'operational pilot is run.';
// ──────────────────────────────────────────────────────────────────────────

function createImplementationDoc() {
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

  // ───────────────────────── IMPLEMENTATION PLAN ─────────────────────────
  body.appendPageBreak();
  body.appendParagraph('Implementation Plan').setHeading(DocumentApp.ParagraphHeading.HEADING1);

  body.appendParagraph('Overview of the System').setHeading(DocumentApp.ParagraphHeading.HEADING2);
  PLAN_OVERVIEW.forEach(function (p) { body.appendParagraph(p); });

  body.appendParagraph('Major Tasks in Implementation').setHeading(DocumentApp.ParagraphHeading.HEADING2);
  PLAN_TASKS.forEach(function (t) {
    var item = body.appendListItem(t[0] + ': ' + t[1]);
    item.setGlyphType(DocumentApp.GlyphType.NUMBER);
    item.editAsText().setBold(0, t[0].length - 1, true);
  });

  body.appendParagraph('Resources Needed').setHeading(DocumentApp.ParagraphHeading.HEADING2);
  appendTwoColTable(body, ['Category', 'Resource'], PLAN_RESOURCES);
  tableCaption(body, 'Table 1. Implementation resources');

  body.appendParagraph('Conversion Strategy').setHeading(DocumentApp.ParagraphHeading.HEADING2);
  body.appendParagraph(PLAN_CONVERSION);

  body.appendParagraph('Site-Specific Requirements').setHeading(DocumentApp.ParagraphHeading.HEADING2);
  PLAN_SITE_REQUIREMENTS.forEach(function (item) {
    body.appendListItem(item).setGlyphType(DocumentApp.GlyphType.BULLET);
  });

  // ───────────────────────── IMPLEMENTATION RESULTS ─────────────────────────
  body.appendPageBreak();
  body.appendParagraph('Implementation Results').setHeading(DocumentApp.ParagraphHeading.HEADING1);

  body.appendParagraph('ISO/IEC 25010 Evaluation Results').setHeading(DocumentApp.ParagraphHeading.HEADING2);

  body.appendParagraph('Respondents').setHeading(DocumentApp.ParagraphHeading.HEADING3);
  body.appendParagraph(RESULTS_RESPONDENTS);

  body.appendParagraph('Evaluation Instrument').setHeading(DocumentApp.ParagraphHeading.HEADING3);
  body.appendParagraph(RESULTS_INSTRUMENT);
  appendInterpretationTable(body, RESULTS_INTERPRETATION);
  tableCaption(body, 'Table 2. Interpretation scale for the weighted mean');

  body.appendParagraph('Results per Quality Characteristic').setHeading(DocumentApp.ParagraphHeading.HEADING3);
  appendCharacteristicsTable(body, RESULTS_CHARACTERISTICS, RESULTS_OVERALL_MEAN);
  tableCaption(body, 'Table 3. ISO/IEC 25010 evaluation results (N = 7)');

  body.appendParagraph('Discussion').setHeading(DocumentApp.ParagraphHeading.HEADING3);
  RESULTS_DISCUSSION.split('\n\n').forEach(function (p) { body.appendParagraph(p); });

  body.appendParagraph('Supporting Evidence').setHeading(DocumentApp.ParagraphHeading.HEADING3);
  body.appendParagraph(RESULTS_SUPPORTING_EVIDENCE);

  body.appendParagraph('Operational Before/After Results — Not Yet Available').setHeading(DocumentApp.ParagraphHeading.HEADING2);
  RESULTS_OPERATIONAL_NOTE.split('\n\n').forEach(function (p) { body.appendParagraph(p); });

  doc.saveAndClose();
  Logger.log('Implementation Plan and Results document: ' + doc.getUrl());
}

// ── table helpers ──────────────────────────────────────────────────────

function tableCaption(body, text) {
  var p = body.appendParagraph(text);
  p.setItalic(true);
  p.setAlignment(DocumentApp.HorizontalAlignment.CENTER);
  p.setFontSize(9);
  body.appendParagraph('');
}

function appendTwoColTable(body, headers, rows) {
  var table = body.appendTable([headers].concat(rows));
  styleHeaderRow(table.getRow(0));
  table.setColumnWidth(0, 160);
  table.setColumnWidth(1, 338);
  shrinkBodyText(table, 10);
  return table;
}

function appendInterpretationTable(body, rows) {
  var header = ['Mean Range', 'Descriptive Rating', 'Descriptive Interpretation'];
  var table = body.appendTable([header].concat(rows));
  styleHeaderRow(table.getRow(0));
  shrinkBodyText(table, 10);
  return table;
}

function appendCharacteristicsTable(body, rows, overallMean) {
  var header = ['Characteristic', 'Weighted Mean', 'Descriptive Rating'];
  var dataRows = rows.map(function (r) { return [r[0], r[1], 'Strongly Agree / Excellent']; });
  dataRows.push(['Overall Weighted Mean', overallMean, 'Strongly Agree / Excellent']);
  var table = body.appendTable([header].concat(dataRows));
  styleHeaderRow(table.getRow(0));
  var lastRow = table.getRow(table.getNumRows() - 1);
  for (var c = 0; c < lastRow.getNumCells(); c++) {
    lastRow.getCell(c).getChild(0).asParagraph().setBold(true);
  }
  shrinkBodyText(table, 10);
  return table;
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

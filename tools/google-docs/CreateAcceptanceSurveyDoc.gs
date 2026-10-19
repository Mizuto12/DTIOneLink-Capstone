/**
 * DTI OneLink User Acceptance Survey (ISO/IEC 25010) -> Google Docs
 * A formatted, print-ready version of the survey (letter-size, checkboxes for
 * each rating), for handing out on paper or sharing as a document.
 *
 * HOW TO USE (about 2 minutes):
 *   1. Go to https://script.google.com and click "New project".
 *   2. Delete the sample code, paste ALL of this file, and click Save.
 *   3. Fill in the project details below (between the quotes).
 *   4. Choose the function "createAcceptanceSurveyDoc" at the top and click Run.
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
var CONTACT_EMAIL = '[Contact email for questions about this study]';
// ──────────────────────────────────────────────────────────────────────────

var DOC_TITLE = 'User Acceptance Survey (ISO/IEC 25010): DTI OneLink';
var BOX = '☐'; // empty checkbox

var SCALE_HEADERS = ['5', '4', '3', '2', '1', 'N/A'];

var PARTS = [
  {
    title: 'Part I. Functional suitability',
    help: 'Does the system do what our office needs, and correctly?',
    items: [
      ['1.1', 'The system has the functions our office needs to manage tasks.'],
      ['1.2', 'The system has the functions our office needs to manage the records masterlist.'],
      ['1.3', 'The system shows correct information (task status, progress, due dates, retention dates).'],
      ['1.4', 'The functions fit the way our office actually works.'],
      ['1.5', 'Notifications and reminders reach the right people about the right things.']
    ]
  },
  {
    title: 'Part II. Performance efficiency',
    help: 'Is the system fast enough for daily work?',
    items: [
      ['2.1', 'Pages open quickly enough for daily use.'],
      ['2.2', 'Saving a task, update or record finishes quickly.'],
      ['2.3', 'Searching for tasks or records gives results quickly.'],
      ['2.4', 'The system can handle the number of staff, tasks and records in our office.']
    ]
  },
  {
    title: 'Part III. Compatibility',
    help: 'Does the system work with what our office already uses?',
    items: [
      ['3.1', 'The system works in the web browser we normally use.'],
      ['3.2', 'The masterlist exported to Excel can be used with our other office files.'],
      ['3.3', 'The system can be used alongside our other work programs without problems.']
    ]
  },
  {
    title: 'Part IV. Usability',
    help: 'Is the system easy to learn and use?',
    items: [
      ['4.1', 'I can tell what each screen is for.'],
      ['4.2', 'I could learn to use the system with a short orientation.'],
      ['4.3', 'Buttons, menus and messages are clear to me.'],
      ['4.4', 'The system asks me to confirm before an action that is hard to undo.'],
      ['4.5', 'The screens look clean and consistent.'],
      ['4.6', 'Text and buttons are large enough to read and click comfortably.']
    ]
  },
  {
    title: 'Part V. Reliability',
    help: 'Can our office depend on the system?',
    items: [
      ['5.1', 'The system worked without errors during the demonstration.'],
      ['5.2', 'The system would be available whenever I need it during work hours.'],
      ['5.3', 'A mistake in entering information does not cause lost work.'],
      ['5.4', "Our office's data can be backed up and recovered if something goes wrong."]
    ]
  },
  {
    title: 'Part VI. Security',
    help: "Does the system protect our office's information?",
    items: [
      ['6.1', 'Each person sees only the tasks and records their role allows.'],
      ['6.2', 'Only authorized people can change a task or record.'],
      ['6.3', 'The system records who did what and when.'],
      ['6.4', 'Signing in protects my account (password, emailed code, lock after repeated wrong passwords).'],
      ['6.5', "I would trust the system with our office's information."]
    ]
  },
  {
    title: 'Part VII. Maintainability',
    help: 'Can the system be kept up to date as our office changes?',
    items: [
      ['7.1', 'Staff accounts, roles and divisions can be updated by our office without outside help.'],
      ['7.2', 'When a problem happens, the message helps us understand what went wrong.'],
      ['7.3', "The system could be updated when our office's needs change."]
    ]
  },
  {
    title: 'Part VIII. Portability',
    help: 'Can the system be used on our devices and replace our current way of working?',
    items: [
      ['8.1', 'The system can be used on desktop computers, laptops, tablets and phones.'],
      ['8.2', 'The system needs only a web browser; nothing has to be installed.'],
      ['8.3', 'The system could replace our current way of tracking tasks and records.']
    ]
  },
  {
    title: 'Part IX. Overall acceptance',
    help: 'Your overall judgement after the demonstration.',
    items: [
      ['9.1', 'Overall, the system meets the needs of our office.'],
      ['9.2', 'I would use the system in my regular work if it is adopted.'],
      ['9.3', 'I would recommend that our office adopt the system.']
    ]
  }
];

var OPEN_QUESTIONS = [
  'What changes, if any, are required before the office can use the system?',
  'Which tasks or processes in your work does the system not cover, if any?',
  'What, if anything, was confusing or difficult in what you saw?',
  'Which features, if any, would be most useful to your office?',
  'Other comments or recommendations'
];

function createAcceptanceSurveyDoc() {
  var doc = DocumentApp.create(DOC_TITLE);
  var body = doc.getBody();

  // Letter-size page with comfortable margins (points; 72 = 1 inch).
  body.setMarginTop(54).setMarginBottom(54).setMarginLeft(72).setMarginRight(72);

  body.appendParagraph(DOC_TITLE).setHeading(DocumentApp.ParagraphHeading.TITLE);
  body.appendParagraph(
    'Dear Respondent,\n\n' +
    'We are ' + PROGRAM + ' students of ' + SCHOOL + ', currently conducting our capstone project entitled "' +
    PROJECT_TITLE + '."\n\n' +
    'You have just seen the system demonstrated. We respectfully ask you to rate it against the eight software ' +
    'quality characteristics of ISO/IEC 25010, and to tell us whether it meets the needs of your office. ' +
    'The survey takes about 10 minutes.\n\n' +
    'Thank you for your time.\n\n' +
    'Respectfully,\n' + RESEARCHERS + '\nResearchers\n\n' +
    'Noted by: ' + ADVISER + ', Capstone Adviser');

  // Informed consent
  body.appendParagraph('Informed consent').setHeading(DocumentApp.ParagraphHeading.HEADING2);
  body.appendParagraph(
    'Your participation is voluntary, and you may stop at any time without any consequence. ' +
    'Your answers will be used only for this capstone project and will be kept confidential in accordance with ' +
    'the Data Privacy Act of 2012 (Republic Act No. 10173). Results will be reported only as summaries; no name ' +
    'will be published. For questions about this study, contact: ' + CONTACT_EMAIL + '.');
  body.appendParagraph(BOX + '  Yes, I agree to take part in this survey.');
  body.appendParagraph(BOX + '  No, I do not agree.');

  // Respondent profile
  body.appendPageBreak();
  body.appendParagraph('Respondent profile and instructions').setHeading(DocumentApp.ParagraphHeading.HEADING2);
  var instructions = body.appendParagraph('Instructions');
  instructions.setBold(true);
  body.appendListItem('Answer based on what you saw in the demonstration, or what you tried yourself.')
    .setGlyphType(DocumentApp.GlyphType.NUMBER);
  body.appendListItem('Choose one answer for each statement by ticking the box.')
    .setGlyphType(DocumentApp.GlyphType.NUMBER);
  body.appendListItem('Tick N/A if that feature was not shown or does not apply to your work. N/A is not counted.')
    .setGlyphType(DocumentApp.GlyphType.NUMBER);
  body.appendListItem('There are no right or wrong answers. Honest answers, including disagreement, help us most.')
    .setGlyphType(DocumentApp.GlyphType.NUMBER);
  body.appendListItem('If something is missing or should change, please write it in the comments at the end.')
    .setGlyphType(DocumentApp.GlyphType.NUMBER);

  appendProfileTable(body, [
    'Name (optional)',
    'Position / designation',
    'Division or unit',
    'Role you would have in the system',
    'Years working at DTI Laguna',
    'Date of the demonstration'
  ]);
  body.appendParagraph(BOX + ' OPD   ' + BOX + ' BDD   ' + BOX + ' CPD   ' + BOX + ' FAU   (Division)');
  body.appendParagraph(BOX + ' Super Admin (OPD)   ' + BOX + ' Admin (Division Chief)   ' + BOX + ' Employee   (Role)');

  // Rating scale
  body.appendPageBreak();
  body.appendParagraph('Rating scale').setHeading(DocumentApp.ParagraphHeading.HEADING2);
  appendScaleTable(body, [
    ['5', 'Strongly Agree', 'The statement is fully true.'],
    ['4', 'Agree', 'The statement is mostly true.'],
    ['3', 'Neither Agree nor Disagree', 'The statement is about as true as it is untrue.'],
    ['2', 'Disagree', 'The statement is mostly not true.'],
    ['1', 'Strongly Disagree', 'The statement is not true at all.'],
    ['N/A', 'Not applicable', 'Not shown, or does not apply to my work. Not counted.']
  ]);

  // The rated parts, one table each
  PARTS.forEach(function (part, i) {
    if (i === 0) body.appendPageBreak();
    body.appendParagraph(part.title).setHeading(DocumentApp.ParagraphHeading.HEADING2);
    body.appendParagraph(part.help).setItalic(true);
    appendStatementTable(body, part.items);
  });

  // Acceptance decision and comments
  body.appendPageBreak();
  body.appendParagraph('Acceptance decision and comments').setHeading(DocumentApp.ParagraphHeading.HEADING2);
  body.appendParagraph('Based on the demonstration, what is your decision on the system? Tick one.').setBold(true);
  body.appendParagraph(BOX + '  Accepted: the system can be used as it is.');
  body.appendParagraph(BOX + '  Accepted with revisions: the system can be used after the changes I list below.');
  body.appendParagraph(BOX + "  Not accepted: the system does not meet the office's needs yet (please explain below).");

  body.appendParagraph('Please answer in your own words. These are optional.').setItalic(true);
  OPEN_QUESTIONS.forEach(function (q, i) {
    body.appendParagraph((i + 1) + '. ' + q).setBold(true);
    body.appendParagraph('_____________________________________________________________________________');
    body.appendParagraph('_____________________________________________________________________________');
  });

  body.appendParagraph('');
  body.appendParagraph('Signature over printed name: ________________________________     Date: ______________');

  doc.saveAndClose();
  Logger.log('Survey document: ' + doc.getUrl());
}

// Two-column profile table: label on the left, a blank answer cell on the right.
function appendProfileTable(body, labels) {
  var rows = labels.map(function (label) { return [label, '']; });
  rows.unshift(['Respondent profile', 'Answer']);
  var table = body.appendTable(rows);
  styleHeaderRow(table.getRow(0));
  table.setColumnWidth(0, 200);
  table.setColumnWidth(1, 268);
  return table;
}

// Rating-scale legend: score, label and meaning.
function appendScaleTable(body, rows) {
  var table = body.appendTable([['Rating', 'Verbal description', 'Meaning']].concat(rows));
  styleHeaderRow(table.getRow(0));
  table.setColumnWidth(0, 60);
  table.setColumnWidth(1, 160);
  table.setColumnWidth(2, 248);
  return table;
}

// One statement table: # | statement | 5 | 4 | 3 | 2 | 1 | N/A, with a centred
// empty checkbox in each rating cell. Widths add up to the printable 468 pt.
function appendStatementTable(body, items) {
  var header = ['#', 'Statement'].concat(SCALE_HEADERS);
  var rows = [header];
  items.forEach(function (it) {
    rows.push([it[0], it[1]].concat(SCALE_HEADERS.map(function () { return BOX; })));
  });

  var table = body.appendTable(rows);
  styleHeaderRow(table.getRow(0));

  table.setColumnWidth(0, 34);   // #
  table.setColumnWidth(1, 200);  // statement
  for (var c = 2; c < header.length; c++) table.setColumnWidth(c, 39); // 6 scale columns

  for (var r = 0; r < table.getNumRows(); r++) {
    for (var col = 2; col < header.length; col++) {
      var cell = table.getRow(r).getCell(col);
      cell.getChild(0).asParagraph().setAlignment(DocumentApp.HorizontalAlignment.CENTER);
    }
    if (r > 0) {
      var stmt = table.getRow(r).getCell(1);
      stmt.getChild(0).asParagraph().setFontSize(10);
    }
  }
  body.appendParagraph(''); // space after the table
  return table;
}

function styleHeaderRow(row) {
  for (var i = 0; i < row.getNumCells(); i++) {
    var cell = row.getCell(i);
    cell.setBackgroundColor('#dce1ff'); // the app's light navy tint
    cell.getChild(0).asParagraph().setBold(true);
  }
}

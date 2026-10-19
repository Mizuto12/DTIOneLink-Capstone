/**
 * DTI OneLink User Acceptance Survey (ISO/IEC 25010) -> Google Forms
 * For DTI Laguna Provincial Office staff, answered right after the demonstration.
 *
 * HOW TO USE (about 2 minutes):
 *   1. Go to https://script.google.com and click "New project".
 *   2. Delete the sample code, paste ALL of this file, and click Save.
 *   3. Fill in the project details below (between the quotes).
 *   4. Choose the function "createAcceptanceSurveyForm" at the top and click Run.
 *   5. The first time, Google asks for permission: click Review permissions,
 *      choose your account, then Advanced -> Go to (project name) -> Allow.
 *      It only creates a Form and a Sheet in YOUR Google Drive.
 *   6. Open the Execution log for the EDIT link (review it) and the SEND link
 *      (give this to the respondents). Responses go to a Google Sheet.
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

var FORM_TITLE = 'User Acceptance Survey (ISO/IEC 25010): DTI OneLink';

var SCALE = [
  '5 - Strongly Agree',
  '4 - Agree',
  '3 - Neither Agree nor Disagree',
  '2 - Disagree',
  '1 - Strongly Disagree',
  'N/A'
];

// The eight ISO/IEC 25010 product quality characteristics, worded for office
// staff who saw the system demonstrated.
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

function createAcceptanceSurveyForm() {
  var form = FormApp.create(FORM_TITLE);
  form.setDescription(
    'Dear Respondent,\n\n' +
    'We are ' + PROGRAM + ' students of ' + SCHOOL + ', currently conducting our capstone project entitled "' +
    PROJECT_TITLE + '."\n\n' +
    'You have just seen the system demonstrated. We respectfully ask you to rate it against the eight software ' +
    'quality characteristics of ISO/IEC 25010, and to tell us whether it meets the needs of your office. ' +
    'The survey takes about 10 minutes.\n\n' +
    'Thank you for your time.\n\n' +
    'Respectfully,\n' + RESEARCHERS + '\nResearchers\n\n' +
    'Noted by: ' + ADVISER + ', Capstone Adviser'
  );
  form.setCollectEmail(false);
  form.setProgressBar(true);
  form.setShowLinkToRespondAgain(false);
  form.setConfirmationMessage('Thank you. Your answers have been recorded and will be used only for this capstone project.');

  // Informed consent (Data Privacy Act of 2012, RA 10173)
  form.addSectionHeaderItem()
    .setTitle('Informed consent')
    .setHelpText(
      'Your participation is voluntary, and you may stop at any time without any consequence. ' +
      'Your answers will be used only for this capstone project and will be kept confidential in accordance ' +
      'with the Data Privacy Act of 2012 (Republic Act No. 10173). Results will be reported only as summaries; ' +
      'no name will be published. For questions about this study, contact: ' + CONTACT_EMAIL + '.'
    );
  var consent = form.addMultipleChoiceItem();
  consent.setTitle('Do you agree to take part in this survey?')
    .setChoices([
      consent.createChoice('Yes, I agree to take part.', FormApp.PageNavigationType.CONTINUE),
      consent.createChoice('No, I do not agree.', FormApp.PageNavigationType.SUBMIT)
    ])
    .setRequired(true);

  // Respondent profile and instructions
  form.addPageBreakItem()
    .setTitle('Respondent profile and instructions')
    .setHelpText(
      'Instructions\n' +
      '1. Answer based on what you saw in the demonstration, or what you tried yourself.\n' +
      '2. Choose one answer for each statement.\n' +
      '3. Choose N/A if that feature was not shown or does not apply to your work. N/A is not counted.\n' +
      '4. There are no right or wrong answers. Honest answers, including disagreement, help us most.\n' +
      '5. If something is missing or should change, please write it at the end.'
    );
  form.addTextItem().setTitle('Name (optional)');
  form.addTextItem().setTitle('Position / designation').setRequired(true);
  var division = form.addMultipleChoiceItem();
  division.setTitle('Division or unit')
    .setChoices([
      division.createChoice('Office of the Provincial Director (OPD)'),
      division.createChoice('Business Development Division (BDD)'),
      division.createChoice('Consumer Protection Division (CPD)'),
      division.createChoice('Financial and Administrative Unit (FAU)')
    ])
    .showOtherOption(true)
    .setRequired(true);
  var role = form.addMultipleChoiceItem();
  role.setTitle('Role you would have in the system')
    .setChoices([
      role.createChoice('Super Admin (OPD)'),
      role.createChoice('Admin (Division Chief)'),
      role.createChoice('Employee')
    ])
    .setRequired(true);
  form.addTextItem()
    .setTitle('Years working at DTI Laguna')
    .setValidation(FormApp.createTextValidation().requireNumberGreaterThanOrEqualTo(0)
      .setHelpText('Please enter a number.').build())
    .setRequired(true);
  form.addDateItem().setTitle('Date of the demonstration').setRequired(true);

  form.addSectionHeaderItem()
    .setTitle('Rating scale')
    .setHelpText(
      '5 - Strongly Agree: the statement is fully true.\n' +
      '4 - Agree: the statement is mostly true.\n' +
      '3 - Neither Agree nor Disagree: the statement is about as true as it is untrue.\n' +
      '2 - Disagree: the statement is mostly not true.\n' +
      '1 - Strongly Disagree: the statement is not true at all.\n' +
      'N/A - Not shown, or does not apply to my work. Not counted.'
    );

  // One page per characteristic, each a Likert grid (one row per statement).
  PARTS.forEach(function (part) {
    form.addPageBreakItem().setTitle(part.title).setHelpText(part.help);
    form.addGridItem()
      .setTitle(part.title.replace(/^Part [IVX]+\.\s*/, '') + ': rate each statement')
      .setRows(part.items.map(function (it) { return it[0] + '  ' + it[1]; }))
      .setColumns(SCALE)
      .setRequired(true); // one answer per row; N/A is available
  });

  // Acceptance decision, on the same page as Part IX.
  form.addMultipleChoiceItem()
    .setTitle('Based on the demonstration, what is your decision on the system?')
    .setChoiceValues([
      'Accepted: the system can be used as it is.',
      'Accepted with revisions: the system can be used after the changes I list below.',
      "Not accepted: the system does not meet the office's needs yet (please explain below)."
    ])
    .setRequired(true);

  form.addSectionHeaderItem()
    .setTitle('Comments')
    .setHelpText('Please answer in your own words. These are optional.');
  OPEN_QUESTIONS.forEach(function (q, i) {
    form.addParagraphTextItem().setTitle((i + 1) + '. ' + q);
  });

  var sheet = SpreadsheetApp.create(FORM_TITLE + ' (Responses)');
  form.setDestination(FormApp.DestinationType.SPREADSHEET, sheet.getId());

  Logger.log('EDIT link (review the form):  ' + form.getEditUrl());
  Logger.log('SEND link (give to respondents): ' + form.getPublishedUrl());
  Logger.log('Responses sheet: ' + sheet.getUrl());
}

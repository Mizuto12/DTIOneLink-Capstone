/**
 * DTI OneLink IT Expert Evaluation Questionnaire -> Google Forms
 *
 * HOW TO USE (about 2 minutes):
 *   1. Go to https://script.google.com and click "New project".
 *   2. Delete the sample code, paste ALL of this file, and click Save.
 *   3. Choose the function "createQuestionnaireForm" at the top and click Run.
 *   4. The first time, Google asks for permission: click Review permissions,
 *      choose your account, then Advanced -> Go to (project name) -> Allow.
 *      It needs access to create a Form and a Sheet in YOUR Google Drive only.
 *   5. Open View -> Logs (or Execution log). It shows two links:
 *        - EDIT link: open it to review the form before sending.
 *        - SEND link: give this one to the IT expert.
 *      The form and its response sheet are also in your Google Drive.
 *
 * Running it again makes a NEW copy; delete old copies from Drive if needed.
 *
 * BEFORE RUNNING: fill in the project details below (between the quotes).
 */

// ── Capstone project details (fill these in) ─────────────────────────────
var PROJECT_TITLE = 'DTI OneLink: An Integrated Task and Records Management System for DTI Laguna Provincial Office';
var SCHOOL        = 'STI College Santa Cruz';
var PROGRAM       = '[Your program, e.g., Bachelor of Science in Information Technology]';
var RESEARCHERS   = '[Researcher 1], [Researcher 2], [Researcher 3]';
var ADVISER       = '[Capstone adviser]';
var CONTACT_EMAIL = '[Contact email for questions about this study]';
// ──────────────────────────────────────────────────────────────────────────

var FORM_TITLE = 'Evaluation Questionnaire for IT Experts: DTI OneLink';

var SCALE = [
  '5 - Strongly Agree',
  '4 - Agree',
  '3 - Neither Agree nor Disagree',
  '2 - Disagree',
  '1 - Strongly Disagree',
  'N/A'
];

var PARTS = [
  {
    title: 'Part I. Functional suitability',
    help: 'Whether the system provides the functions needed for task and records management, and whether they give correct results.',
    items: [
      ['1.1', 'The system provides the functions needed to create, assign and monitor tasks.'],
      ['1.2', 'The system provides the functions needed to log and look up records in the masterlist.'],
      ['1.3', 'Task status (To Do, In Progress, For Review, Returned, Completed) changes correctly as work moves forward.'],
      ['1.4', 'Tasks past their due date are shown as overdue.'],
      ['1.5', 'Task progress percentages shown match the progress that was entered.'],
      ['1.6', 'Submitting proof of completion sends the task for review.'],
      ['1.7', 'Approving or returning a submission produces the expected result for the employee.'],
      ['1.8', 'Records nearing or past their retention period are flagged correctly.'],
      ['1.9', 'Notifications are sent for the events they describe (assignment, due dates, review results).'],
      ['1.10', 'Dashboard and report figures match the underlying task and record data.'],
      ['1.11', 'Each role (Super Admin, Admin, Employee) has the functions its work requires.']
    ]
  },
  {
    title: 'Part II. Performance efficiency',
    help: 'How quickly the system responds and how well it handles its workload.',
    items: [
      ['2.1', 'Pages load within an acceptable time.'],
      ['2.2', 'Saving a task, record or update completes within an acceptable time.'],
      ['2.3', 'Searching and filtering return results within an acceptable time.'],
      ['2.4', 'Uploading a proof-of-completion file completes within an acceptable time.'],
      ['2.5', 'Response time stays acceptable when several users are signed in at once.'],
      ['2.6', 'The system remains usable on a slow or mobile internet connection.']
    ]
  },
  {
    title: 'Part III. Compatibility',
    help: 'How well the system works with other software and alongside other users.',
    items: [
      ['3.1', 'The system works the same in common browsers (e.g., Chrome, Edge, Firefox, Safari).'],
      ['3.2', 'Exported masterlists open correctly in Microsoft Excel or similar software.'],
      ['3.3', 'Emails sent by the system (verification codes, notices) display correctly in common email services.'],
      ['3.4', "Two or more users working at the same time do not overwrite or lose each other's changes."]
    ]
  },
  {
    title: 'Part IV. Usability',
    help: 'How easily the intended users, including staff with little technical background, can learn and use the system.',
    items: [
      ['4.1', 'A first-time user can find the main functions without help.'],
      ['4.2', 'Labels, buttons and messages use plain, understandable words.'],
      ['4.3', 'Navigation between modules is consistent across pages.'],
      ['4.4', 'The system asks for confirmation before actions that are hard to undo.'],
      ['4.5', 'Error messages explain what went wrong and how to fix it.'],
      ['4.6', 'Forms prevent or flag invalid input before it is saved.'],
      ['4.7', 'Text size, colour contrast and button size are adequate for reading and clicking.'],
      ['4.8', 'The layout adjusts properly on phones and tablets.'],
      ['4.9', 'The visual design is consistent throughout the system.']
    ]
  },
  {
    title: 'Part V. Reliability',
    help: 'Whether the system stays available, handles errors and protects data from loss.',
    items: [
      ['5.1', 'The system was available whenever it was accessed during testing.'],
      ['5.2', 'The system did not crash or show unhandled error pages during testing.'],
      ['5.3', 'Invalid actions (e.g., wrong input, missing fields) are handled without data loss.'],
      ['5.4', 'Saved data remains correct after signing out and back in.'],
      ['5.5', 'The system recovers normally after a lost internet connection.'],
      ['5.6', "There is a documented way to back up and restore the system's data."]
    ]
  },
  {
    title: 'Part VI. Security',
    help: 'Whether the system protects its data and allows access only to the right people.',
    items: [
      ['6.1', 'Users can see only the tasks and records their role and division permit.'],
      ['6.2', 'Pages cannot be opened by changing the web address without the proper role.'],
      ['6.3', 'Passwords are protected and are not shown or sent in readable form.'],
      ['6.4', 'New accounts must verify their email and replace the default password before use.'],
      ['6.5', 'Repeated wrong passwords lead to a temporary account lock.'],
      ['6.6', "Password reset requires proof of access to the account's email."],
      ['6.7', 'Data entered into forms cannot be used to change or read other data without permission (e.g., SQL injection, script injection).'],
      ['6.8', 'Uploaded files are checked before they are accepted.'],
      ['6.9', 'Actions on tasks are recorded with who did them and when.'],
      ['6.10', 'The connection to the system is encrypted (HTTPS).']
    ]
  },
  {
    title: 'Part VII. Maintainability',
    help: 'How easily the system can be understood, corrected and changed later. Choose N/A for every statement if you did not review the source code or technical documents.',
    items: [
      ['7.1', 'The source code is organized into clear, separate parts (e.g., controllers, views, services).'],
      ['7.2', 'The source code is readable and commented where needed.'],
      ['7.3', 'A change to one module can be made without affecting unrelated modules.'],
      ['7.4', 'Database changes are managed in a controlled way (e.g., migrations).'],
      ['7.5', 'Errors can be traced through logs or error messages.'],
      ['7.6', 'Routine upkeep (e.g., replacing an email key, adding a user) can be done without a programmer.']
    ]
  },
  {
    title: 'Part VIII. Portability',
    help: 'How easily the system can be used on different devices or moved to another environment.',
    items: [
      ['8.1', 'The system works on desktop computers, laptops, tablets and phones without installing extra software.'],
      ['8.2', 'The system works on common operating systems (e.g., Windows, Android, iOS).'],
      ['8.3', 'The system could be moved to another web host or server with reasonable effort.'],
      ['8.4', 'The deployment process is documented or automated.']
    ]
  },
  {
    title: 'Part IX. Overall rating and open-ended questions',
    help: 'Your overall judgement after testing, then your comments in your own words.',
    items: [
      ['9.1', 'Overall, the system meets the needs of task and records management at DTI Laguna Provincial Office.'],
      ['9.2', 'Overall, the system is ready to be used by the office.']
    ]
  }
];

var OPEN_QUESTIONS = [
  'What problems, errors or unexpected behaviour did you find? Include the page and what you did, if you can.',
  'Which functions or parts of the system, if any, did you find difficult to use or understand?',
  'What security concerns, if any, did you observe?',
  "Which functions, if any, are missing or not needed for the office's work?",
  'Which parts of the system, if any, worked well?',
  'Other comments or recommendations'
];

function createQuestionnaireForm() {
  var form = FormApp.create(FORM_TITLE);
  form.setDescription(
    'Dear Respondent,\n\n' +
    'We are ' + PROGRAM + ' students of ' + SCHOOL + ', currently conducting our capstone project entitled "' +
    PROJECT_TITLE + '."\n\n' +
    'As part of the system evaluation, we respectfully request your assistance as an IT expert in assessing the ' +
    'system against the software quality characteristics of ISO/IEC 25010. The questionnaire takes about ' +
    '15 to 20 minutes after you have tested the system. Your ratings and comments will help us measure the ' +
    'quality of the system and improve it.\n\n' +
    'Thank you for your time and expertise.\n\n' +
    'Respectfully,\n' + RESEARCHERS + '\n' + 'Researchers\n\n' +
    'Noted by: ' + ADVISER + ', Capstone Adviser'
  );
  form.setCollectEmail(false);
  form.setProgressBar(true);
  form.setShowLinkToRespondAgain(false);
  form.setConfirmationMessage('Thank you. Your evaluation has been recorded and will be used only for this capstone project.');

  // Informed consent (Data Privacy Act of 2012, RA 10173)
  form.addSectionHeaderItem()
    .setTitle('Informed consent')
    .setHelpText(
      'Your participation is voluntary, and you may stop at any time without any consequence. ' +
      'The information you provide will be used only for the evaluation of this capstone project and will be ' +
      'kept confidential in accordance with the Data Privacy Act of 2012 (Republic Act No. 10173). ' +
      'Results will be reported only as summaries; no name will be published. ' +
      'For questions about this study, contact: ' + CONTACT_EMAIL + '.'
    );
  var consent = form.addMultipleChoiceItem();
  consent.setTitle('Do you agree to take part in this evaluation?')
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
      '1. Use the system before answering. Try each role you were given an account for (Super Admin, Admin, Employee).\n' +
      '2. Rate each statement based on what you observed while testing.\n' +
      '3. Choose N/A if you did not test that feature or it does not apply. N/A is not counted in the scores.\n' +
      '4. There are no right or wrong answers. Your honest rating helps improve the system.\n' +
      '5. Write any problem you found, even a small one, in the last part.'
    );
  form.addTextItem().setTitle('Name (optional)');
  form.addTextItem().setTitle('Position / designation').setRequired(true);
  form.addTextItem().setTitle('Organization / office').setRequired(true);
  form.addTextItem()
    .setTitle('Years of experience in IT')
    .setValidation(FormApp.createTextValidation().requireNumberGreaterThanOrEqualTo(0)
      .setHelpText('Please enter a number.').build())
    .setRequired(true);
  form.addTextItem()
    .setTitle('Area of expertise')
    .setHelpText('e.g., software development, networking, security')
    .setRequired(true);
  form.addDateItem().setTitle('Date of evaluation').setRequired(true);
  form.addTextItem().setTitle('Device and browser used for testing').setRequired(true);

  // Rating scale explanation
  form.addSectionHeaderItem()
    .setTitle('Rating scale')
    .setHelpText(
      '5 - Strongly Agree: the statement is fully true of the system.\n' +
      '4 - Agree: the statement is mostly true, with minor exceptions.\n' +
      '3 - Neither Agree nor Disagree: the statement is about as true as it is untrue.\n' +
      '2 - Disagree: the statement is mostly not true.\n' +
      '1 - Strongly Disagree: the statement is not true at all.\n' +
      'N/A - Not tested, or does not apply. Not counted.'
    );

  // One page per Part, each a Likert grid (one row per statement).
  PARTS.forEach(function (part) {
    form.addPageBreakItem().setTitle(part.title).setHelpText(part.help);
    form.addGridItem()
      .setTitle(part.title.replace(/^Part [IVX]+\.\s*/, '') + ': rate each statement')
      .setRows(part.items.map(function (it) { return it[0] + '  ' + it[1]; }))
      .setColumns(SCALE)
      .setRequired(true); // one answer per row; N/A is available
  });

  // Open-ended questions (optional), on the same page as the Part IX rating.
  form.addSectionHeaderItem()
    .setTitle('Open-ended questions')
    .setHelpText('Please answer in your own words. These are optional.');
  OPEN_QUESTIONS.forEach(function (q, i) {
    form.addParagraphTextItem().setTitle((i + 1) + '. ' + q);
  });

  // Responses go to a Google Sheet for computing the means.
  var sheet = SpreadsheetApp.create(FORM_TITLE + ' (Responses)');
  form.setDestination(FormApp.DestinationType.SPREADSHEET, sheet.getId());

  Logger.log('EDIT link (review the form):  ' + form.getEditUrl());
  Logger.log('SEND link (give to the expert): ' + form.getPublishedUrl());
  Logger.log('Responses sheet: ' + sheet.getUrl());
}

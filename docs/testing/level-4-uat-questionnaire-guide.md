# Level 4 — UAT Questionnaire Guide and Results

DTI OneLink: An Integrated Task and Records Management System for DTI Laguna Provincial
Office. This document distinguishes three different kinds of Level 4 evidence, per the
task's own framing — they are not interchangeable and should not be blended in the
manuscript:

1. **Role-based task completion** — observed by a facilitator watching a participant do a
   real task (see `level-4-uat-task-scripts.md`). Not covered in this document.
2. **User feedback / acceptance decision** — a participant's written comments and their
   Accept / Accept with revisions / Not accepted decision (see
   `level-4-client-acceptance-form.md`). Not covered in this document.
3. **ISO/IEC 25010 questionnaire results** — the Likert-scale ratings, covered below.

---

## There are two different questionnaires in this project — do not conflate them

- **`tools/google-forms/CreateQuestionnaireForm.gs`** generates a Google Form titled
  *"Evaluation Questionnaire for IT Experts: DTI OneLink."* Its Part VII (Maintainability)
  and Part VIII (Portability) ask technical questions (source code organization, EF Core
  migrations, readable logs) that only someone who reviewed the codebase could answer. It
  has up to 11 items in a single part (Functional suitability) and 6 in Performance
  efficiency.
- **The PDF actually administered** (`User Acceptance Survey (ISO/IEC 25010): DTI
  OneLink`, the one respondents filled in on paper) is a **shorter, plain-language
  instrument for general DTI Laguna staff** — SuperAdmin/Admin/Employee, by division
  (OPD/BDD/CPD/FAU). Its Part VII and VIII ask staff-friendly questions instead ("Staff
  accounts, roles and divisions can be updated by our office without outside help," not
  "the source code is organized into clear, separate parts"). It has 5 items in Functional
  suitability, 4 in Performance efficiency, and ends with an **Acceptance decision**
  (Accept / Accept with revisions / Not accepted) and a signature line that the `.gs`
  script does not have at all.

**The raw response data (`Respondents.xlsx`) matches the PDF's item numbering exactly, not
the `.gs` script's.** The `.gs` script is for a separate, not-yet-run IT Expert evaluation
(see "IT Expert" row in `level-4-uat-task-scripts.md`) — treat it as a to-do, not as
something already reflected in the 7-respondent results below.

---

## Raw data: what's actually available, and how it was used

`Respondents.xlsx` is a **tally sheet** — for each of the PDF survey's 36 rating items, it
records how many of the 7 respondents chose each rating (5/4/3/2/1), using numeric counts
per rating column. This is genuine completed response data, not a researcher's post-hoc
summary — so per the task instruction, it was used to independently compute each item's
and each part's mean directly, rather than just re-stating
`docs/implementation-results.md`'s draft figures.

**Correction history:** an earlier pass of this sheet, read via tally marks (`IIIII` style),
had three issues — items 1.1 and 4.6 each showed one tally mark too many (n=8 for 7
respondents), item 9.3 was mislabeled "2.3," and several Maintainability/Portability items
showed n=6 instead of 7. The source spreadsheet has since been corrected (confirmed by the
researcher against the original paper forms) and now shows exactly n=7 for every one of
the 36 items, with item 9.3 correctly labeled. The tables below reflect that **corrected**
tally — the anomalies described in earlier drafts of this document no longer apply.

**Method:** for each item, mean = Σ(rating × count of respondents choosing it) ÷ 7. Each
part's mean is the mean of its items' means, matching the convention
`docs/implementation-results.md` already uses. The overall mean is the mean of the 9 part
means.

### Computed results (independently verified from the corrected raw tally data)

| Part | Items | Computed mean | Previously reported (`implementation-results.md`) | Agreement |
| --- | --- | --- | --- | --- |
| I. Functional suitability | 1.1–1.5 | **4.71** | 4.71 (Functionality) | Exact match |
| II. Performance efficiency | 2.1–2.4 | **4.75** | 4.75 | Exact match |
| III. Compatibility | 3.1–3.3 | **4.57** | 4.57 | Exact match |
| IV. Usability | 4.1–4.6 | **4.67** | 4.67 | Exact match |
| V. Reliability | 5.1–5.4 | **4.46** | 4.46 | Exact match |
| VI. Security | 6.1–6.5 | **4.63** | 4.63 | Exact match |
| VII. Maintainability | 7.1–7.3 | **4.71** | 4.71 | Exact match |
| VIII. Portability | 8.1–8.3 | **4.76** | 4.71 | Close |
| IX. Overall acceptance | 9.1–9.3 | **4.71** | 4.71 (Overall Acceptance) | Exact match |
| **Overall (mean of the 9 part means)** | | **4.67** | **4.66** | Close |

This near-perfect agreement with the previously reported draft figures, now that the three
transcription errors are fixed, is strong confirmation that both the original draft and
this independent recomputation are sound.

### Per-item detail (corrected tally, N = 7 throughout)

**Part I. Functional suitability**

| Item | Statement | Ratings (5/4/3/2/1) | Mean |
| --- | --- | --- | --- |
| 1.1 | Has the functions needed to manage tasks | 4/2/1/0/0 | 4.43 |
| 1.2 | Has the functions needed to manage the records masterlist | 6/1/0/0/0 | 4.86 |
| 1.3 | Shows correct information (status, progress, due/retention dates) | 6/1/0/0/0 | 4.86 |
| 1.4 | Functions fit how the office actually works | 5/2/0/0/0 | 4.71 |
| 1.5 | Notifications reach the right people about the right things | 5/2/0/0/0 | 4.71 |

**Part II. Performance efficiency**

| Item | Statement | Ratings | Mean |
| --- | --- | --- | --- |
| 2.1 | Pages open quickly enough for daily use | 5/2/0/0/0 | 4.71 |
| 2.2 | Saving a task, update or record finishes quickly | 5/2/0/0/0 | 4.71 |
| 2.3 | Searching gives results quickly | 6/1/0/0/0 | 4.86 |
| 2.4 | Handles the office's number of staff/tasks/records | 5/2/0/0/0 | 4.71 |

**Part III. Compatibility**

| Item | Statement | Ratings | Mean |
| --- | --- | --- | --- |
| 3.1 | Works in the office's normal browser | 5/2/0/0/0 | 4.71 |
| 3.2 | Exported masterlist usable in other office files | 3/4/0/0/0 | 4.43 |
| 3.3 | Usable alongside other work programs | 4/3/0/0/0 | 4.57 |

**Part IV. Usability**

| Item | Statement | Ratings | Mean |
| --- | --- | --- | --- |
| 4.1 | Can tell what each screen is for | 4/2/1/0/0 | 4.43 |
| 4.2 | Learnable with a short orientation | 6/0/1/0/0 | 4.71 |
| 4.3 | Buttons/menus/messages are clear | 6/0/1/0/0 | 4.71 |
| 4.4 | Confirms before hard-to-undo actions | 4/3/0/0/0 | 4.57 |
| 4.5 | Screens look clean and consistent | 6/1/0/0/0 | 4.86 |
| 4.6 | Text/buttons large enough to read and click | 5/2/0/0/0 | 4.71 |

**Part V. Reliability**

| Item | Statement | Ratings | Mean |
| --- | --- | --- | --- |
| 5.1 | Worked without errors during the demonstration | 3/3/1/0/0 | 4.29 |
| 5.2 | Would be available whenever needed during work hours | 4/3/0/0/0 | 4.57 |
| 5.3 | A data-entry mistake doesn't cause lost work | 4/3/0/0/0 | 4.57 |
| 5.4 | Office data can be backed up and recovered | 4/2/1/0/0 | 4.43 |

**Part VI. Security**

| Item | Statement | Ratings | Mean |
| --- | --- | --- | --- |
| 6.1 | Each person sees only their role's tasks/records | 4/3/0/0/0 | 4.57 |
| 6.2 | Only authorized people can change a task or record | 5/2/0/0/0 | 4.71 |
| 6.3 | Records who did what and when | 5/2/0/0/0 | 4.71 |
| 6.4 | Sign-in protects the account (password/OTP/lockout) | 5/2/0/0/0 | 4.71 |
| 6.5 | Would trust the system with office information | 3/4/0/0/0 | 4.43 |

**Part VII. Maintainability**

| Item | Statement | Ratings | Mean |
| --- | --- | --- | --- |
| 7.1 | Accounts/roles/divisions updatable without outside help | 4/3/0/0/0 | 4.57 |
| 7.2 | Problem messages help understand what went wrong | 5/2/0/0/0 | 4.71 |
| 7.3 | Could be updated as the office's needs change | 6/1/0/0/0 | 4.86 |

**Part VIII. Portability**

| Item | Statement | Ratings | Mean |
| --- | --- | --- | --- |
| 8.1 | Usable on desktop/laptop/tablet/phone | 6/1/0/0/0 | 4.86 |
| 8.2 | Needs only a browser, nothing to install | 6/1/0/0/0 | 4.86 |
| 8.3 | Could replace the current way of tracking tasks/records | 5/1/1/0/0 | 4.57 |

**Part IX. Overall acceptance**

| Item | Statement | Ratings | Mean |
| --- | --- | --- | --- |
| 9.1 | Overall meets the office's needs | 5/2/0/0/0 | 4.71 |
| 9.2 | Would use the system in regular work | 4/3/0/0/0 | 4.57 |
| 9.3 | Would recommend adoption | 6/1/0/0/0 | 4.86 |

---

## What to do before the defense

1. ~~Pull the 7 original paper forms and recount items 1.1 and 4.6.~~ **Done** — the
   source spreadsheet has been corrected and all 36 items now total exactly 7 respondents.
2. ~~Fix the "2.3" → "9.3" label.~~ **Done** — item 9.3 is now correctly labeled in
   `Respondents.xlsx`.
3. **Keep collecting the remaining 13 of 20 targeted respondents.** Both this document and
   `docs/implementation-results.md` describe n=7/20 as a limitation, not a finished
   evaluation — that framing should stay in the final manuscript.

## The IT Expert evaluation has not been run yet

No response data exists for `tools/google-forms/CreateQuestionnaireForm.gs`. Its
project-detail placeholders (`PROGRAM`, `RESEARCHERS`, `ADVISER`, `CONTACT_EMAIL`) are
still unfilled template text, confirming it has not yet been deployed to a real IT expert.
See the "IT Expert" role script in `level-4-uat-task-scripts.md` for the task-completion
side of this still-outstanding evaluation.

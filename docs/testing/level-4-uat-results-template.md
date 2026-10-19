# Level 4 — UAT Results Template

DTI OneLink: An Integrated Task and Records Management System for DTI Laguna Provincial
Office. A blank template to fill in **during and after real UAT sessions**. Nothing in
this file is pre-filled with assumed or estimated outcomes.

---

## Session log

| Session # | Date | Division | Facilitator | Participants present | Notes |
| --- | --- | --- | --- | --- | --- |
| 1 | _______ | _______ | _______ | _______ | _______ |
| 2 | _______ | _______ | _______ | _______ | _______ |

(Add rows as needed. One row per distinct sitting, not per participant — several
participants may be observed in the same session.)

---

## 1. Role-based task completion (facilitator-observed)

Copy one row per participant per task from `level-4-uat-task-scripts.md`'s UAT IDs. Do not
fill in a row unless a facilitator actually watched that participant attempt that task.

| UAT ID | Participant (initials or ID) | Role | Division | Completed without help? | Completion time | Assistance given | Issues observed | Result |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| | | | | | | | | Pending UAT |

**Result values:** `Pending UAT` (not yet attempted) · `Completed unaided` · `Completed
with assistance` (describe the assistance given) · `Not completed` (describe why) ·
`Not attempted this session`.

### Completion summary (fill in only after all sessions conclude)

| Role | Tasks scripted | Attempted | Completed unaided | Completed with assistance | Not completed | Not attempted |
| --- | --- | --- | --- | --- | --- | --- |
| SuperAdmin / OPD | 5 | — | — | — | — | — |
| Admin / Division Chief | 6 | — | — | — | — | — |
| Employee | 5 | — | — | — | — | — |
| IT Expert | 3 | — | — | — | — | — |

---

## 2. User feedback (open-ended, from the PDF survey's comment section)

One row per participant who left written comments. Quote them; do not paraphrase into a
more positive or more negative form than written.

| Participant (initials/ID) | Role | Division | Q1: changes required | Q2: processes not covered | Q3: confusing/difficult | Q4: most useful features | Q5: other comments |
| --- | --- | --- | --- | --- | --- | --- | --- |
| | | | | | | | |

---

## 3. ISO/IEC 25010 questionnaire results

Do not re-key individual ratings here — that computation lives in
`level-4-uat-questionnaire-guide.md`, which already reflects the 7 responses collected so
far (with two flagged anomalies pending verification against the original paper forms).
When additional responses come in, update that document's raw tallies and recompute there,
then update this summary row:

| Measure | Value | As of |
| --- | --- | --- |
| Respondents reflected in the computed means | 7 of 20 targeted | 2026-10-10 (see `level-4-uat-questionnaire-guide.md`) |
| Overall weighted mean (mean of 9 part means) | 4.67 (corrected tally, no anomalies) | 2026-10-10 |
| IT Expert questionnaire responses | 0 | — (form not yet generated/sent) |

---

## 4. Known limitations of this UAT round (carry into the manuscript)

- Only 7 of the 20 targeted DTI Laguna personnel have been surveyed; the remaining 13 were
  not available before this draft's cut-off.
- No role-based task-completion session has been run yet — every row in Section 1 above is
  still blank.
- The IT Expert technical evaluation has not been run at all; `CreateQuestionnaireForm.gs`
  still needs its placeholders filled in and the form generated and sent.
- No client acceptance decision has been recorded yet (see
  `level-4-client-acceptance-form.md`).

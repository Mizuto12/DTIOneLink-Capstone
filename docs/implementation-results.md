# Implementation Results

DTI OneLink: An Integrated Task and Records Management System for DTI Laguna Provincial
Office. This document covers (1) the ISO/IEC 25010 user-acceptance evaluation results,
and (2) an honest note on operational before/after results, which — unlike the
evaluation survey — have not yet been formally measured.

## ISO/IEC 25010 Evaluation Results

### Respondents

The evaluation instrument was distributed to DTI Laguna Provincial Office's full
targeted population of 20 personnel. **Seven (7) respondents** completed and returned
the survey within the data-gathering period so far — a 35% response rate. The remaining
thirteen were not available to respond before the cut-off for this draft and are not
represented in the figures below.

> **Note for the final manuscript:** report this honestly as n = 7 of 20, and state it
> as a limitation of the evaluation (small sample, single data-gathering session) rather
> than projecting or estimating responses for staff who were not surveyed. If more
> responses come in before your defense, re-run the computation below with the updated
> counts — do not blend real and assumed data in the same table.

### Evaluation Instrument

The system was evaluated using a researcher-made questionnaire based on the **ISO/IEC
25010** software quality model, covering nine characteristics: Functionality,
Performance Efficiency, Compatibility, Usability, Reliability, Security,
Maintainability, Portability, and Overall Acceptance. Each characteristic was broken
down into 3–6 indicator items, rated on a 5-point Likert scale (5 = Strongly Agree, 1 =
Strongly Disagree, with an N/A option excluded from the computation).

Responses were scored using the weighted mean:

```
Weighted Mean = Σ(rating × number of respondents who chose that rating) / respondents who rated that item
```

with the following interpretation scale:

| Mean Range | Descriptive Rating | Descriptive Interpretation |
|---|---|---|
| 4.21 – 5.00 | Strongly Agree | Excellent |
| 3.41 – 4.20 | Agree | Very Satisfactory |
| 2.61 – 3.40 | Neutral | Satisfactory |
| 1.81 – 2.60 | Disagree | Fair |
| 1.00 – 1.80 | Strongly Disagree | Poor |

### Results per Quality Characteristic

| Characteristic | Weighted Mean | Descriptive Rating |
|---|---|---|
| Functionality | 4.71 | Strongly Agree / Excellent |
| Performance Efficiency | 4.75 | Strongly Agree / Excellent |
| Compatibility | 4.57 | Strongly Agree / Excellent |
| Usability | 4.67 | Strongly Agree / Excellent |
| Reliability | 4.46 | Strongly Agree / Excellent |
| Security | 4.63 | Strongly Agree / Excellent |
| Maintainability | 4.71 | Strongly Agree / Excellent |
| Portability | 4.76 | Strongly Agree / Excellent |
| Overall Acceptance | 4.71 | Strongly Agree / Excellent |
| **Overall Weighted Mean** | **4.67** | **Strongly Agree / Excellent** |

Figures above are computed directly from the corrected raw response tally — an earlier
draft of this table used a tally with three transcription errors, since fixed and
re-verified against the original paper forms.

### Discussion

All nine ISO/IEC 25010 characteristics scored within the **Strongly Agree / Excellent**
range (4.21–5.00), with an overall weighted mean of **4.67**. Among respondents,
**Portability** (4.76) scored highest, followed by **Performance Efficiency** (4.75),
while **Reliability** (4.46) scored comparatively lowest — driven mainly by item 5.1
("the system worked without errors during the demonstration"), where ratings were more
spread out than on other items (three 5s, three 4s, one 3) rather than concentrated at
the top. This suggests Reliability-related behavior during live demonstrations is the
area most worth revisiting before full rollout, even though it still falls in the
"Excellent" band.

The results indicate that, among the DTI Laguna personnel who were able to evaluate the
system, DTI OneLink was very positively received across functional correctness,
performance, compatibility, usability, reliability, security, maintainability,
portability, and overall acceptance. However, because only 7 of the 20 targeted
personnel responded, these results should be read as an early, favorable indication
rather than a conclusive office-wide evaluation. The researchers recommend collecting
responses from the remaining personnel — particularly staff who were not present during
the on-site demonstration — before treating this evaluation as final.

### Supporting Evidence

The research team presented the Task Board and live dashboard to DTI Laguna OPD staff,
division personnel, and faculty panelists at the Provincial Office, giving respondents
direct, hands-on exposure to the system before rating it — contextual evidence of the
evaluation process, not data in itself.

## Operational Before/After Results — Not Yet Available

A formal, timed before-and-after comparison (e.g., average time to locate a record,
number of missed task follow-ups per month, under the previous manual process versus
with DTI OneLink) has **not** been conducted. Unlike the ISO/IEC 25010 evaluation above,
no quantitative operational data has been collected from a real, sustained pilot period
yet — the on-site session so far was a one-time demonstration and survey, not a
multi-week parallel run.

To produce this comparison honestly rather than estimating it, the office would need to
run DTI OneLink alongside its existing manual process for a defined pilot period and
record, for the same workload, metrics such as:

- Time to locate a specific record under the manual process vs. DTI OneLink's search.
- Number of task deadlines missed or discovered late per month, before and after
  notifications were in use.
- Number of records whose retention status required manual lookup vs. flagged
  automatically by the system.

Until that pilot period happens and these are measured, this section should not claim
specific improvement percentages — doing so without real measurements would misrepresent
the evaluation. The ISO/IEC 25010 results above remain the system's primary evidence of
acceptability until an operational pilot is run.

# Scenario Readiness Plan

DTI OneLink: An Integrated Task and Records Management System for DTI Laguna Provincial
Office. A scenario-by-scenario plan for closing the gaps identified in the current
testing and evaluation status, before the system is presented as ready for full,
unsupervised office use. Status values are grounded in `docs/testing/*.md` — nothing
here is marked Covered without a corresponding executed test case.

## A. Core Workflow Scenarios (highest priority — most-used features)

| Scenario | Status | Plan |
| --- | --- | --- |
| OPD directive creation (direct-Admin, division, whole-office) | Not Covered (unit-tested only) | Execute TC-TASK-001–003 live against the running app |
| Admin task assignment within division, negative cross-division assignment | Not Covered | Execute TC-TASK-004/005 |
| Subtasks, recurrence, reassignment, OPD-assigned task protections | Not Covered | Execute TC-TASK-006–011 |
| Status workflow cannot be skipped or reopened | Not Covered at Level 3 (unit-tested only) | Execute TC-TASK-012/013 |
| Filters, division filter visibility, Kanban/Timeline views | Not Covered | Execute TC-TASK-015–018 |
| Priority suggestion rule and boundaries | Not Covered at Level 3 | Execute TC-TASK-019/020 |
| Proof upload, rejection of missing/disguised files, approve/return, resubmission, comments | Not Covered | Execute TC-PROOF-001–011 |

**Plan:** run all of TC-TASK and TC-PROOF live (the same HTTP-execution method already
used for Auth/Records/Notifications), using the working Admin and Employee sessions
already established. This is the single highest-value gap to close, since it covers the
features staff will use every day.

## B. Dashboards and Reporting

| Scenario | Status | Plan |
| --- | --- | --- |
| Office-wide vs. division-scoped dashboard counts | Not Covered | Execute TC-REPORT-001/002 |
| Whole Office task assigned to the Admin personally may be missing from their own dashboard | Flagged candidate, not confirmed | Execute TC-REPORT-003; if confirmed, log as a defect and fix before the defense |
| Employee own-assignment-only dashboard | Not Covered | Execute TC-REPORT-004 (needs a working Employee session — already available) |
| Records Summary and Delay Risk Indicator placement per role | Not Covered | Execute TC-REPORT-005/006 |

## C. Performance and Load Scenarios

| Scenario | Status | Plan |
| --- | --- | --- |
| Normal single-user response time | Covered (warm run: 0/20 errors, 1.46s avg) | No action needed |
| Cold start after idle | Covered, acceptable (0/20 errors, 15.3s avg) | No action needed |
| **Concurrent login rush (e.g., Monday 8am office-wide sign-in)** | **Covered, and failing**: mid-load run showed 6/20 (30%) errors, up to 139s | **Highest-priority infrastructure risk.** Options: (1) upgrade the hosting tier to one that doesn't idle-sleep between requests, (2) add a scheduled "keep-alive" ping so the app never fully cold-starts before business hours, (3) document this as a known limitation and advise the office to stagger login. Re-run the JMeter test after whichever fix is applied, using the same tool/parameters, to get a real before/after comparison. |

## D. Security and Misuse Scenarios

| Scenario | Status | Plan |
| --- | --- | --- |
| Privilege escalation (Admin creating SuperAdmin/other-division accounts), forged role-change requests, CSRF, unauthenticated access to protected routes | Covered — all rejected correctly; one inconsistency (`/Tasks` 403 vs. redirect) found and fixed | No action needed |
| Records visibility scoping (owner-only, even same-division, even for SuperAdmin) | Covered, confirmed by live execution | Get a product decision on whether owner-only (not division-wide) is actually the intended design — currently working as coded, but worth confirming intent before the defense |
| Admin-triggered password reset vs. PROJECT-STATUS.md's "ruled out of scope" note | Covered (feature works) | Needs a scope decision, not a code fix — resolve before citing scope boundaries in the manuscript |

## E. Operational / Infrastructure Scenarios (no dedicated IT staff)

| Scenario | Status | Plan |
| --- | --- | --- |
| Office-side internet outage | Known limitation, by design (no local-network fallback) | Document plainly in the Implementation Plan; not something to fix, but must not be left implicit |
| Hosting/email service outage (MonsterASP, Brevo) | Not tested | At minimum, confirm what the user sees if Brevo is down (e.g., OTP email delayed/not sent) and that it fails safely, not silently |
| Data loss / need to restore | No in-app backup/restore feature; relies entirely on the hosting provider and SQL Server's own facilities | Confirm in writing what backup guarantees MonsterASP actually provides before relying on this as an answer during the defense |
| Credential and maintenance handover after the capstone concludes | **Not done** | Document and hand over GitHub, MonsterASP, and Brevo access to whoever will maintain the system (the office or STI College Santa Cruz), with written instructions — this is a real operational gap, not just a documentation nicety |

## F. Acceptance and Sign-off Scenarios

| Scenario | Status | Plan |
| --- | --- | --- |
| ISO/IEC 25010 survey | Partial (7 of 20 targeted respondents) | Continue collecting the remaining 13 before treating the overall mean as final |
| IT Expert technical evaluation | Not run | Fill in `CreateQuestionnaireForm.gs` placeholders, generate and send the form |
| Facilitator-observed role-based task completion | Not run | Schedule sessions using `docs/testing/level-4-uat-task-scripts.md` |
| Formal client acceptance decision | Not recorded | Obtain a signed decision via `docs/testing/level-4-client-acceptance-form.md` once the above are far enough along |

## Priority Order

1. **Execute TC-TASK and TC-PROOF live** (Section A) — the biggest gap relative to how
   much staff will actually use these features day to day.
2. **Resolve the mid-load performance failure** (Section C) — a measured 30% error rate
   is a credibility risk if raised during the defense without a plan already in motion.
3. **Complete the credential handover** (Section E) — specific to this office's lack of
   IT staff; low effort, meaningfully reduces post-capstone risk.
4. Everything else (Sections B, D confirmations, F) can proceed in parallel but does not
   block presenting the system as a working, evaluated pilot — it blocks presenting it
   as a *finished, office-wide-ready* product.

---
target: Views/ dashboards + core workflow + wwwroot css/js
total_score: 30
max_score: 40
na_heuristics: 
p0_count: 0
p1_count: 1
target_identity: "file:C:\\Users\\chandrei\\Desktop\\DTIOneLink-Capstone\\Views\\ (Admin, SuperAdmin, Employee dashboards + core workflow views) and wwwroot\\css,js"
timestamp: 2026-10-26T15-00-57Z
slug: ds-core-workflow-views-and-wwwroot-css-js-45ce9ca4
---
## Design Health Score

| # | Heuristic | Score | Key Issue |
|---|-----------|-------|-----------|
| 1 | Visibility of System Status | 4 | Skeleton loaders, aria-busy, live counts, SignalR live-refresh |
| 2 | Match System / Real World | 4 | Division codes, real workflow-stage names, human due-date phrasing |
| 3 | User Control and Freedom | 3 | Modals cancel cleanly; disposal-status buttons have no confirm step |
| 4 | Consistency and Standards | 4 | One modal pattern, one badge/tone system, one icon set everywhere |
| 5 | Error Prevention | 2 | Delete/Deactivate confirm with consequences; disposal transitions don't |
| 6 | Recognition Rather Than Recall | 3 | Filters persist via GET query string across refresh/bookmark |
| 7 | Flexibility and Efficiency | 2 | No bulk actions anywhere; one-row-at-a-time for approve/disposal |
| 8 | Aesthetic and Minimalist Design | 2 | SuperAdmin landing + Tasks toolbar front-load too much at once |
| 9 | Error Recovery | 4 | Consistent `showNotice()` pattern, field-level validation, dismissible banners |
| 10 | Help and Documentation | 2 | Contextual "?" hints exist but are a tiny, unlabeled, easy-to-miss affordance |
| **Total** | | **30/40** | **Good** |

## Design Specificity Verdict

**LLM assessment**: Not a generic template skin. Division names (BDD/CPD/FAU/OPD), the Task Type taxonomy, records-management jargon (Filing System, Access Control, Retention Period, an RA 9470 reference), and the masterlist "Prepared by / Reviewed by / Noted by" signatory block all mirror DTI Laguna's actual paper workflow. Code comments explain layout decisions in terms of real office behavior, which is unusual intentionality for hand-rolled Razor/CSS with no front-end framework.

**Deterministic scan**: `impeccable detect --json Views` ran clean — exit code 0, zero findings, across all 33 `.cshtml` files. No hardcoded-color, z-index, or static anti-pattern violations detected. (CSS-only files are out of scope for this detector per the tool's own rule.)

**Visual overlays**: Not available this run — no browser automation tool is exposed in this session, and per an explicit earlier decision this audit is static-code-only (no dev server was started). Fallback signal: code-only review, no live-rendered visual inspection.

## Overall Impression

The app is built with real craft for its actual users — consistent modal/badge/icon systems, GET-persisted filters, and a genuinely excellent deactivation-confirmation flow. The gap is a risk-model inversion: the app's own stated scope treats records disposal as the one event worth a notification, yet disposal status changes are the one high-stakes action with zero confirmation, while lower-stakes actions (Delete, Deactivate) already have the safety pattern built and reused elsewhere in the same file. The second-biggest opportunity is density on the two most-trafficked screens (SuperAdmin dashboard, Tasks toolbar), which runs against the project's own "simple UI for older, non-technical staff" goal.

## What's Working

1. **Deactivation confirmation** (`Views/UserManagement/Index.cshtml:503-529`) — named person, explicit consequences, Cancel, and a "view their tasks first" escape hatch. This is the gold-standard pattern the rest of the app should reuse.
2. **Contextual "?" help-hints** for records jargon (`Views/Records/Index.cshtml:97-121`) — demystifies domain terms right where they're needed, without a help page or onboarding tour, fitting the no-IT-support constraint.
3. **Filters as GET query params** (`Views/Tasks/_TaskList.cshtml:48`) — filter/sort state survives refresh, back-navigation, and bookmarking, which matters a lot for users who refresh unpredictably.

## Priority Issues

**[P1] No confirmation on records-disposal status changes.**
Why it matters: `wwwroot/js/records.js:82-94, 236-238` fires `MarkForDisposal`/`MarkArchived`/`MarkDisposed` on a single click with zero confirmation, while the less-consequential-looking Delete (`records.js:353-363`) already requires a typed confirm. A records officer with no IT backstop who mis-clicks has no safety net for the one action the project's own memory notes call out as "the" event worth notifying about.
Fix: route the three disposal transitions through the same `showNotice({tone:'error', cancelText:...})` confirm pattern already built and used in the same file for Delete.
Suggested command: `$impeccable harden`

**[P2] Tasks list toolbar exceeds working-memory limits.**
Why it matters: `Views/Tasks/_TaskList.cshtml:22-134` shows 7 workflow-status tiles plus search, sort, and up to 4 filter dropdowns simultaneously — 12+ controls at once on the single most-trafficked Admin/SuperAdmin page, well past the ≤4-per-decision-point guideline for a non-technical audience.
Fix: keep tiles + search + sort as the default view; collapse Division/Employee/Due-Date filters behind a "More filters" disclosure.
Suggested command: `$impeccable distill`

**[P2] SuperAdmin dashboard front-loads too much at once.**
Why it matters: `Views/SuperAdmin.cshtml:72-259` stacks 8 summary numbers, a 5-column kanban, and 2 rail widgets before any collapsing kicks in — the first screen every SuperAdmin sees daily.
Fix: extend the collapsed-with-preview pattern already used well for Overdue Tasks/Employee Workload (lines 262-281, 346-364) to Status Distribution/Completion Trend.
Suggested command: `$impeccable layout`

**[P2] Task Type jargon is gated behind a tiny unlabeled "?" rather than shown inline.**
Why it matters: `Views/Tasks/Create.cshtml:71-74` — "Direct Admin Task"/"Department Directive"/"Whole Office" materially change which fields appear (lines 94-143), but the difference is explained only via a tooltip a first-time Admin may never notice before picking wrong.
Fix: show a one-line inline description under the selected option, not just on hover/tap of a "?".
Suggested command: `$impeccable clarify`

**[P3] Login label/placeholder mismatch.**
Why it matters: `Views/Account/Login.cshtml:54,59` — the label reads "Username" while the placeholder in the same field reads "Enter your Email," which can make a first-time, non-technical user unsure what to type.
Fix: make label and placeholder agree (relevant either way the email-login decision in your handoff checklist resolves).
Suggested command: `$impeccable clarify`

## Persona Red Flags

**Jordan (confused first-timer — the real primary audience here)**: Hits the Login label/placeholder mismatch immediately. Lands on Task Management and sees 7 status tiles + 4 filter dropdowns + search + sort at once with no "start here" cue. Creating a task, must pick a Task Type whose real differences are explained only behind a 1-character "?". If Jordan is a records officer and fat-fingers "Mark Disposed" on the wrong row, nothing catches the mistake — the one moment in the app that's least recoverable is also the least guarded. On the positive side, the Records entry form's collapsed-by-default state and help-hints mean Jordan isn't hit with the full jargon-heavy form unless they choose to open it.

**Sam (accessibility-dependent)**: Task rows use `role="link" tabindex="0" aria-label"` rather than an unlabeled clickable `<tr>` — a deliberate, non-default a11y pattern. Modals use native `<dialog>` with automatic focus/Escape handling and mark background content `inert`/`aria-hidden`. Status/priority is always paired with a text label alongside color, not color-only. Risk: several secondary explanations rely on `title` attributes (division full name on chips, risk-badge reason) — `title` tooltips are inconsistently exposed by screen readers and invisible on touch, though the primary label is always present so this is a supplementary channel, not the only one.

## Minor Observations

- Native `<dialog>` everywhere instead of a JS modal library is a good minimal-maintenance choice given there's no IT staff to keep a library updated after handover.
- The masterlist signatory grid mirrors actual DTI paperwork sign-off convention — strong domain-specific authoring.
- "Restore to Retention" is a good reactive safety net after a Disposed mistake, but it's reactive, not preventive — doesn't substitute for a confirm step before the mistake happens.
- Icon-font loading degrades gracefully with a 10s timeout fallback (`EmployeeLayout.cshtml:34-42`), though it wasn't verified whether icon-only buttons have sufficient alternative text during that fallback window.

## Questions to Consider

1. If a records officer clicks "Mark Disposed" on the wrong row, what currently stops a government record from being labeled disposed by mistake? Right now, nothing.
2. The Tasks toolbar shows every possible filter, always, rather than defaulting to the few that matter day-to-day — is that actually discoverable for a first-timer, or did it just never get prioritized?
3. Several load-bearing explanations (Task Type differences, Access Control meaning) live behind a one-character "?" — would a first-time user find that before or after submitting the wrong choice?

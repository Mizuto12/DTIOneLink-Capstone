---
target: Full system UI/UX (dashboards, Tasks, Records, Reports, User Management)
total_score: 28
max_score: 40
na_heuristics: 
p0_count: 1
p1_count: 2
target_identity: "file:C:\\Users\\chandrei\\Desktop\\DTIOneLink-Capstone\\Views\\ (full system: dashboards, Tasks, Records, Reports)"
timestamp: 2026-10-21T16-09-50Z
slug: views-full-system-dashboards-tasks-records-reports
---
Method: dual-agent (A: a0bc68e52b9bd12ce · B: a5648d416f6d6f14e)

## Design Health Score

| # | Heuristic | Score | Key Issue |
|---|-----------|-------|-----------|
| 1 | Visibility of System Status | 3 | Good skeleton/loading states almost everywhere, but several icon-only controls (column "more" menu, task-edit pencil) render with no confirmable wired behavior |
| 2 | Match System / Real World | 4 | Plain, DTI-specific language throughout ("Needs resubmission," "Awaiting review") — a real strength |
| 3 | User Control and Freedom | 3 | Cancel/close present on most modals, but some icon actions (Kanban edit pencil) have no apparent destination |
| 4 | Consistency and Standards | 3 | Status colors/avatars/workflow order match across dashboards, but Employee TaskManagement calls the same status "Rejected" while everywhere else calls it "Returned for Correction" |
| 5 | Error Prevention | 3 | Good confirm-with-consequences text on deletes, but a bulk "Delete all completed" action has no confirm guard visible in the markup itself |
| 6 | Recognition Rather Than Recall | 3 | Labeled icons mostly, plus excellent plain-language role descriptions in User Management; a few icon-only buttons remain |
| 7 | Flexibility and Efficiency | 2 | No bulk actions beyond one case, no saved filters, no shortcuts — thin for an Admin triaging many tasks |
| 8 | Aesthetic and Minimalist Design | 3 | Dense but purposeful dashboards; no progressive disclosure, so first-time scanning is heavy |
| 9 | Error Recovery | 3 | Consistent, dismissible success/error banners; generic errors don't always name the specific field/record |
| 10 | Help and Documentation | 1 | No in-app help or onboarding anywhere reviewed — the heuristic most at risk for an IT-support-free office |
| **Total** | | **28/40** | **Good** (band: 70%) |

## Design Specificity Verdict

**LLM assessment**: This is clearly built for DTI Laguna's actual workflow, not a generic admin template. Division short-codes (OPD/BDD/CPD/FAU) are baked into filters and tags; the five-stage task workflow (To Do → In Progress → For Review → Returned → Completed) is mirrored consistently across dashboards; Records module fields map to real government records-management forms (Medium, Filing System, Access Control, Retention Period); the default-password-then-confirm-email onboarding matches a real IT-constrained office. This is domain-fitted work, not a reskinned template.

**Deterministic scan**: `impeccable detect` found **28 findings, all in `wwwroot/css/*`** — zero findings in any Razor view (Views/SuperAdmin.cshtml, Views/Shared, Views/Employee, Views/Tasks, Views/Records, Views/Reports, Views/UserManagement) or in `wwwroot/js`. Breakdown: `overused-font` (Inter) ×18 across 7 files, `side-tab` (border-left accent) ×4, `layout-transition` (animating width/height) ×4, `border-accent-on-rounded` ×2. The detector itself flagged these as stylistic "AI-slop" tells rather than functional defects, and in this product's context (an internal staff tool prioritizing legibility for older, non-technical users) Inter and the border accents are a reasonable, defensible choice, not a real problem. The `layout-transition` findings (animating `width`/`height` instead of `transform`) are the one category worth a quick look if those elements are large or toggle often — otherwise low-priority.

**Visual overlays**: Not available this run — browser injection against the live production site (`dtionelink.runasp.net`) was deliberately skipped as inappropriate on a real government system without dedicated test credentials. Assessment B instead used read-only `WebFetch` on the two pages reachable without login (root, `/Account/Login`); nothing concerning surfaced there, but raw HTML (title, viewport meta, lang attribute, label/input association) could not be verified through that tool's markdown conversion.

## Overall Impression

The system's bones are good: a coherent five-stage workflow, consistent status language and color-coding across all three roles' dashboards, and genuinely thoughtful plain-language copy in the one place it matters most (User Management's role descriptions). The real gaps are not visual — the detector found almost nothing wrong in the markup itself — they're in **consistency** (one screen uses a different word for the same status than everywhere else), **trust at high-stakes moments** (irreversible deletes rely on a jarring native browser `confirm()` instead of the app's own styled dialog, which already exists and works well on the Records page), and **the complete absence of in-app help** for an office that has no IT staff to ask instead. The single biggest opportunity: unify on the patterns already built and proven elsewhere in the app (the Records notice-dialog, the "Returned for Correction" vocabulary) instead of leaving one older screen out of step.

## What's Working

- **Records' reusable notice-dialog** (title + message + optional numbered steps + OK/Cancel) is a disciplined, humane confirmation pattern built specifically for a non-technical audience — it just isn't used everywhere yet.
- **User Management's plain-language role copy** ("Admin (Division Chief) — Assigns tasks to the employees of their division") turns an abstract permission model into something an older staffer understands immediately. Exactly right for this audience.
- **Cross-dashboard consistency**: the same five-stage workflow, tone colors, and avatar-initials pattern recur identically across SuperAdmin, Admin, and Employee dashboards and the Tasks list — a real, hard-won backbone that most systems this size don't bother to hold onto.

## Priority Issues

**[P0] "Rejected" vs. "Returned for Correction" is the same status under two different names**
- **Where**: `Views/Employee/TaskManagement.cshtml` (column labeled "Rejected") vs. `Views/Shared/_DashboardContent.cshtml`, `Views/SuperAdmin.cshtml`, and `Views/Tasks/*` (all say "Returned"/"Returned for Correction")
- **Why it matters**: This is the exact lane where an employee's rejected work lands — the single moment they most need the system to feel coherent. Older staff relying on pattern-matching rather than a deep mental model will reasonably wonder if these are two different things.
- **Fix**: Rename the column to "Returned" in TaskManagement.cshtml to match every other surface.
- **Suggested command**: `$impeccable clarify`

**[P1] Destructive actions use a native browser `confirm()` instead of the app's own dialog**
- **Where**: `Views/Tasks/MainTaskDetails.cshtml` (task delete, stop-repeating)
- **Why it matters**: A plain OS confirm box reads as an error or a half-finished feature, and is trivially muscle-memory-dismissed ("just click OK") — exactly the wrong feel for an irreversible delete, and it undermines the error-prevention intent entirely.
- **Fix**: Reuse the Records page's existing `#notice-dialog` pattern app-wide instead of `window.confirm(...)`.
- **Suggested command**: `$impeccable harden`

**[P1] No in-app help anywhere in the reviewed surfaces**
- **Where**: Confirmed absent across SuperAdmin, Tasks, Records, Reports, and User Management
- **Why it matters**: This office has no IT staff (per the project's own context) and includes older, non-technical users. `title=` tooltips are invisible on touch devices and discoverable by nobody who isn't already hovering a mouse.
- **Fix**: One persistent "Help" affordance per module, or inline "?" hints on the genuinely jargon-y concepts (Retention Period, Access Control, Task Type).
- **Suggested command**: `$impeccable onboard`

**[P2] Icon-only controls that may not do anything**
- **Where**: The column "more" (⋯) menu on Employee TaskManagement's Kanban columns, and the pencil edit icon on task cards
- **Why it matters**: These render identically whether or not they're wired up, so a user can't tell what's actually clickable versus decorative — a trust problem as much as a usability one.
- **Fix**: Either implement real behavior behind them or remove them until they do something.
- **Suggested command**: `$impeccable clarify`

**[P3] SuperAdmin dashboard has no progressive disclosure**
- **Where**: `Views/SuperAdmin.cshtml` — 8 stat cards, a status bar, a trend chart, a Kanban, an overdue table, and a workload table all in one continuous scroll
- **Why it matters**: Not wrong for an Operate surface, but it's a heavy first scan, and the cognitive-load check below shows real strain here.
- **Fix**: Make "Employee Workload" and "Overdue Tasks" collapsible sections, collapsed by default.
- **Suggested command**: `$impeccable layout`

## Cognitive Load

- **SuperAdmin dashboard**: 3 of 8 checklist items fail — chunking (8 stat cards with no grouping), one-thing-at-a-time (6 distinct sections compete on one page with no collapse), and minimal-choices (division filter + 5 Kanban columns + 2 tables, all live decision surfaces at once).
- **Tasks Index**: 2 of 8 fail — chunking (7 filter controls visible simultaneously with no progressive disclosure) and a working-memory wrinkle where the workflow-tile row and the status dropdown both filter by status but read as two different mental models.

## Emotional Journey

Best moment: reviewing a submission (`Tasks/Review.cshtml`) — clear "Approve" vs. "Return for Correction" choices with the employee's remarks and proof visible before deciding; low ambiguity, good closure. Worst moments: an employee opening their "Returned"/"Rejected" column sees only "Needs resubmission" with no visible reason inline — the sting of rejection isn't softened with context at the point of discovery, it has to be chased down via a click-through. The native `confirm()` dialogs on deletion compound this: at the highest-stakes moment in the app, the UI suddenly looks unfinished.

## Persona Red Flags

**Jordan (confused first-timer)**: Lands on the Tasks list and sees workflow tiles (All/To Do/In Progress/.../Completed) directly above a separate row of filter dropdowns — two differently-styled controls that both filter, with nothing explaining the tiles are clickable filters too. The Create Task modal's "Task Type" options read as policy text ("Department Directive — sent to the department") that assumes domain knowledge Jordan may not have yet, with no inline example.

**Sam (accessibility-dependent user)**: Several interactive rows (task cards, Kanban rows, Gantt rows) use `role="link" tabindex="0" data-href="..."` divs instead of real `<a>` tags, which only work for a keyboard/screen-reader user if JS wires an Enter/Space handler — unconfirmed from markup alone, and if it's missing, Sam simply cannot open a task card without a mouse. Icon-only menu buttons have no visible text for anyone relying on magnification rather than a screen reader.

**Riley (deliberate stress-tester)**: The "Delete all completed tasks" bulk action and the OPD task-delete confirm are both one native `confirm()` away from permanently losing real records, with no undo grace period anywhere reviewed. A reflexive double-click or confirm-click-through loses data for good.

## Minor Observations

- Reports' filter/period selector widgets show no visible keyboard-navigation affordance in the markup.
- "Save and Download" wording on the Masterlist dialog may read as doing more than just saving.
- Priority badges repeat the word "Priority" next to a color dot that already says the same thing — minor verbosity.
- User Management's one-time "temporary password" banner (shown once, never stored) is a nice, worth-preserving security/UX touch.
- `layout-transition` findings (animating `width`/`height` on `tasks-index.css`, `employeetaskupdate.css`, `completion-trend.css`) are cheap to swap for `transform`-based equivalents if those elements are large or toggle often.

## Questions to Consider

- If an older staffer can't tell the "more" menu from a decorative icon, should a column with no real menu action simply drop the button rather than ship a placeholder?
- Given no IT support exists on-site, should every destructive action funnel through the one confirmation-dialog component already built for Records, instead of mixing native `confirm()` and custom dialogs across modules?
- Is "Rejected" vs. "Returned for Correction" a sign that the Employee TaskManagement screen predates the five-stage vocabulary finalized elsewhere — and if so, what else on that screen might be similarly stale?
- Would one persistent "What does this mean?" glossary (Retention Period, Access Control, Task Type, Division Directive) meaningfully reduce the help burden this office has no staff to cover?

---
target: Task Management / Main Task Details
total_score: 29
max_score: 40
na_heuristics: 
p0_count: 1
p1_count: 2
target_identity: "file:C:\\Users\\chandrei\\Desktop\\DTIOneLink-Capstone\\Views\\Tasks\\MainTaskDetails.cshtml"
target_fingerprint: "sha256:88255d4890ac4b3c356da63f51d56edeac3e9e47697a9325a1d28f6db7b46fc9"
target_path: "C:\\Users\\chandrei\\Desktop\\DTIOneLink-Capstone\\Views\\Tasks\\MainTaskDetails.cshtml"
timestamp: 2026-10-19T06-11-42Z
slug: views-tasks-maintaskdetails-cshtml
---
## Design Health Score

| # | Heuristic | Score | Key Issue |
|---|-----------|-------|-----------|
| 1 | Visibility of System Status | 3 | Solid banners; no pre-flight on whether a button's action can succeed |
| 2 | Match System / Real World | 4 | Terminology matches the org's real vocabulary throughout |
| 3 | User Control and Freedom | 2 | All three consequential actions rely on raw `window.confirm()`, no undo |
| 4 | Consistency and Standards | 4 | Reuses the app's card/table/badge/pill vocabulary cleanly |
| 5 | Error Prevention | 2 | Delete's eligibility is checked only *after* submit, not before showing the button |
| 6 | Recognition Rather Than Recall | 4 | Everything needed is visible in the summary cards |
| 7 | Flexibility and Efficiency | 3 | Reasonable for this audience; no bulk actions needed here |
| 8 | Aesthetic and Minimalist Design | 3 | Responsible Admin card gets crowded (label + name + select + button) |
| 9 | Error Recovery | 3 | Plain-language errors, but none suggest a next step |
| 10 | Help and Documentation | 1 | No explanation for the unconditional empty Subtasks card; one line of help total |
| **Total** | | **29/40** | **Good** |

## Design Specificity Verdict

**LLM assessment**: The copy is genuinely authored for this org — "Department Directive," "Responsible Admin," "OPD," and the rollup-math explanation in `.mtd-note` all read as written for DTI OneLink specifically. But the visual system is a straight reuse of `tasks-index.css`'s generic admin-table components, carrying over affordances that don't fit: `.task-row` applies `cursor: pointer` and a hover scale to every row, but the only actually-clickable thing in each row is a small pencil icon. The page also never displays the Main Task's own type (Direct Admin Task / Department Directive / Whole Office) anywhere, despite nearly every conditional on the page branching on it.

**Deterministic scan**: Clean — the Impeccable detector returned 0 findings against the markup file (exit code 0). The manual evidence pass found 14+ hardcoded hex colors across `main-task-details.css` and `tasks-index.css` that bypass the existing `--mtd-*`/`var(--...)` custom-property system — a token-discipline gap underneath an otherwise consistent-looking page. It also confirmed both destructive actions (Delete, Stop Repeating) use raw `onsubmit="return confirm(...)"`, and caught one stray static inline style (`style="margin:0;"` on line 161) that isn't data-driven like the other three.

**Visual overlays**: Not available this run — no dev server was running, and this page requires an authenticated SuperAdmin session plus a real task ID in the route. No browser evidence was fabricated.

## Overall Impression

A functionally solid, domain-literate rollup page let down by two things: it inherited generic-admin-table visual habits (clickable-looking rows, no type label) rather than being purpose-built for its own three-shaped data model, and it shows every user a scary "cannot be undone" delete confirmation without first checking whether delete can even succeed. The single biggest opportunity is making the page honest about what kind of Main Task you're looking at.

## What's Working

1. The Reassign confirm dialog names both the task and the newly-chosen admin back to the user before committing — exactly the right amount of friction for an irreversible, office-wide handoff.
2. `.mtd-note` gives one well-placed, plain-language sentence explaining why the rollup status behaves the way it does.
3. The Review button is scoped to only pending submissions — it doesn't always show up and risk doing nothing.

## Priority Issues

**[P0] The Subtasks card renders unconditionally, even for task types that structurally never have subtasks**
- Why it matters: For a Direct Admin Task or Whole Office task, the OPD sees a permanently-empty "Subtasks (0) — No subtasks yet" card on every single one, with zero explanation. For staff with no IT support, that reads as a bug.
- Fix: Suppress the Subtasks card entirely (not just show it empty) when `task.TaskType != DepartmentDirective`.
- Suggested command: $impeccable clarify

**[P1] Delete's eligibility isn't checked before the confirm dialog fires**
- Why it matters: The button and its "cannot be undone" warning show unconditionally; the server-side `hasStarted` refusal only happens after the user has already faced the scary dialog and submitted.
- Fix: Pass the eligibility flag into the view so the button disables (or the confirm text says "already started, can't be deleted") before the irreversible-delete warning appears.
- Suggested command: $impeccable harden

**[P1] No visible indicator of the Main Task's own type**
- Why it matters: Every structural difference on the page hinges on `task.TaskType`, but that value itself is never shown. A first-time OPD user has to infer which of the three task shapes they're looking at.
- Fix: Add a small badge near the eyebrow — "Department Directive" / "Direct Admin Task" / "Whole Office Task" — reusing the existing status-badge pill pattern.
- Suggested command: $impeccable clarify

**[P2] Clickable-looking rows that aren't clickable**
- Why it matters: Inherited from `tasks-index.css`, every subtask/employee row gets `cursor: pointer` and a hover highlight, but the only working control is a small pencil icon. Older, non-technical users are especially likely to click the big obvious row and get nothing.
- Fix: Either make the whole row a link to Edit, or scope-override the pointer/hover affordance off for this page's rows.
- Suggested command: $impeccable layout

**[P3] Completed progress never turns green here**
- Why it matters: `tasks-index.css` already defines `.progress-fill.progress-green`/`.progress-slate`, but every bar on this page uses only the default blue gradient regardless of being at 100%.
- Fix: Apply the existing green/slate classes based on status here, or remove the dead CSS if no page uses it.
- Suggested command: $impeccable polish

## Persona Red Flags

**Jordan (first-timer)**: Can't tell which of the three task types she's looking at (no label). Tries clicking a subtask row because it visibly highlights — nothing happens. Opens a Direct Admin Task and sees an empty, unexplained "Subtasks" card right below a populated employee table. If she clicks Delete on a started task, she faces the "cannot be undone" dialog, submits, and only then is told it's blocked.

**Riley (stress tester)**: 0-subtask case is exactly the P0 bug. 20-subtask case has no vertical cap, pagination, search, or filter. A very long task name + long department string share one unconstrained flex row with the Delete button, risking header crowding before the phone breakpoint. The one case that does hold up: the delete-refused error banner renders right at the top, directly above the button that caused it.

## Minor Observations

- The breadcrumb's `taskCode` ("TASK-0004") is the one ticket-system-style term on an otherwise plain-language page.
- Add Subtask's input silently reformats capitalization (`data-capitalize="title"`) with no visible indication to the user.
- The Reassign `<select>` caps at 220px while each option appends a department name — longer names will truncate depending on browser/OS.
- Stop Repeating and Delete use two different visual treatments for "danger" — a small inconsistency in how the page signals destructive actions.
- A static (non-data-driven) inline `style="margin:0;"` on the "Assigned Employees" heading — everywhere else in the file, inline styles are legitimately dynamic (progress-bar widths).
- 14+ hardcoded hex colors across the two stylesheets bypass the `--mtd-*` custom-property system already in place.

## Questions to Consider

- If a Department Directive's subtasks have their own assignees, how does the OPD actually review a subtask-level submission from this page — the per-person Review button only appears when there are no subtasks?
- Given the whole page branches on task type, why was showing that type itself the one thing left out?
- Was showing Delete unconditionally a deliberate "let them try, the server will explain" choice, or did the server-side `hasStarted` check just never get threaded back into the view?

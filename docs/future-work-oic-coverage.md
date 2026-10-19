# Future Work: Temporary OIC / Acting-Admin Coverage

DTI OneLink: An Integrated Task and Records Management System for DTI Laguna Provincial
Office. This note answers a question raised during adviser consultation: what happens to
an Employee's supervision when their Division Chief (Admin) is on leave?

## What exists today

**Nothing automatic.** Every Admin account is permanently tied to exactly one
`Department`, and every department-scoped check in the system — task visibility, records
filtering, dashboard counts, user management — reads that single value directly from the
session (`HttpContext.Session.GetString("UserDepartment")`) or a controller-local
`CurrentUserDepartment()` helper. There is no concept of a user temporarily gaining
supervisory access to a second division.

If a Division Chief goes on leave, their division's tasks and records simply stay scoped
to their account. No other Admin sees them unless a SuperAdmin manually changes that
Admin's department via `UserManagementController.ChangeStanding` — but that is a
**permanent reassignment**, not a temporary OIC designation: it moves the Admin out of
their original division entirely, with no mechanism to revert automatically when they
return. It is not a substitute for what an OIC designation actually needs to do.

## The real business rule (confirmed, not assumed)

Per direct discussion with the capstone adviser: only another Division Chief (Admin) can
be designated OIC for a division, and the Office of the Provincial Director (SuperAdmin)
is the one who decides who that is.

## Why this isn't built yet

Department scoping is not centralized. A direct count across the codebase found **21
separate places across 7 controllers** that each read the department value independently:
`TasksController`, `RecordsController`, `ReportsController`,
`Controllers/Admin/DashboardController.cs`, `EmployeeController`,
`UserManagementController`, and `AccountController`. Making OIC coverage work for real —
so a covering Admin genuinely sees and can act on the covered division's tasks — means
updating every one of those checks consistently, with no missed spot.

This is a cross-cutting authorization change, not a self-contained feature, and it
targets exactly the kind of surface this project's own testing has worked hardest to
verify is correct (cross-division and cross-user access boundaries — see
`docs/testing/level-3-system-test-cases.md`, TC-UM-004/007/008, TC-REC-005/006). A single
missed call site here would be a silent data leak between divisions, not a visible bug —
a materially more severe failure mode than the kind already found and fixed elsewhere in
this project. Building and shipping it without dedicating real, separate testing time to
that specific risk would be the wrong trade this close to the defense, especially given
it was never part of the system's original scoped requirements — it surfaced as a
hypothetical question, not a confirmed DTI Laguna requirement.

## Recommended design, for when this is built

1. A new `OicAssignments` table: `CoveringUserId`, `CoveredDepartment`, `StartDate`,
   `EndDate`, `AssignedByUserId` (always a SuperAdmin), mirroring the audit-trail pattern
   already used by `TaskAssignment` (`AssignedByUserId`, `ReassignedFromUserId`).
2. A SuperAdmin-only page to create/revoke an assignment — matching the existing
   SuperAdmin-only gating pattern in `ChangeStanding`.
3. **One centralized scoping helper** (e.g. `EffectiveDepartments(userId)` returning the
   user's home department plus any currently-active covered department) that all 21
   existing call sites are updated to use, instead of reading the session value directly.
   Centralizing it in one place is what makes the authorization change auditable and
   testable, rather than 21 independent edits that could drift out of sync later.
4. **Dedicated Level 3 test cases** specifically for the cross-division boundary this
   introduces: a covering Admin can see the covered division's tasks/records; a covering
   Admin cannot see a *third*, uninvolved division's data; access reverts the moment the
   assignment's end date passes or a SuperAdmin revokes it early.

This is scoped as future work, not a current limitation to apologize for — DTI OneLink's
existing single-department-per-Admin model was itself a deliberate, reasonable design
choice for the capstone's original scope, and this document exists so that choice and its
boundary are stated plainly rather than discovered by surprise.

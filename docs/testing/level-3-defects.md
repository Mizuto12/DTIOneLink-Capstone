# Level 3 — Defect Log

This log is for defects found **during Level 3 system-test execution** (black-box, via the
UI). It is separate from the existing defect log in
`docs/testing/four-levels-of-testing.md` (DEF-001–DEF-004), which were found during Level 1
unit testing, Level 2 integration testing, and a manual security review — not Level 3 — and
are not duplicated here.

**Process, per the task instructions:** a defect entry is created before any fix is
proposed; nothing here is marked Fixed/Retested unless that work actually happened and was
actually retested with the same test case.

## Confirmed defects (Level 3, via actual execution)

| Defect ID | Found in (Test Case ID) | Description | Severity | Found | Fixed | Retest |
| --- | --- | --- | --- | --- | --- | --- |
| L3-DEF-001 | TC-AUTH-009 | **Unauthenticated access to `/Tasks` returns a bare HTTP 403 instead of redirecting to login.** Reproduced with a real HTTP request (no session cookie at all): `curl -i http://localhost:5199/Tasks` → `403 Forbidden`. The same request against `/UserManagement` and `/Records` correctly returns `302 Found` → `/` (the login page), matching the documented requirement (FR-05 in `four-levels-of-testing.md`, IT-05 in the automated suite: "No session at all requests `/UserManagement` → Redirect to login, not a 403"). Root cause: `TasksController.Index` (and every other action in the controller — Create, Edit, approve/return submission, etc.) manually calls `CanAccessTaskManagement()`/`IsOfficeWideTaskManager()` and returns `StatusCode(403)` on failure, without first checking whether a session exists at all — unlike `RequireLoginAttribute`/`RequirePermissionAttribute`, used elsewhere, which check login first and only 403 when a session exists but lacks the needed permission. | Medium — not a security hole (access is still correctly denied either way), but it's a real inconsistency with the system's own stated and tested behavior, and gave an anonymous visitor no path back to login. | 2026-10-09 (this Level 3 pass, via live HTTP execution) | **Fixed 2026-10-09** — added `[RequireLogin]` at the class level on `TasksController` (`Controllers/TasksController.cs`), matching the pattern already used by `NotificationsController`. This runs before every existing action's own permission check, for every action in the controller, not just `Index`. Build verified clean (0 errors). | **Retested 2026-10-09, Passed** — `curl -i http://localhost:5199/Tasks` with no session now returns `302 Found` → `Location: /`, identical to `/UserManagement` and `/Records`. A valid SuperAdmin session was also confirmed to still reach `/Tasks` normally (`200 OK`) after the fix, confirming legitimate access was not broken. |

## Candidates: resolved or still pending

| Candidate | Source | Corresponding test case | Status |
| --- | --- | --- | --- |
| Records visibility is owner-only, not division-wide, **even for SuperAdmin** | Code review, **confirmed by live execution** | TC-REC-006 | **Confirmed true, not a defect** — `GET /Records/GetAll?q=R4A-LAG-FAU-GAS-06` as SuperAdmin returned `[]` for a record owned by a different user. This appears to be a deliberate design choice (the code comment says it applies to SuperAdmin on purpose), not a bug — worth a product decision/confirmation before the defense on whether this is the intended behavior, but it is not logged as a defect. |
| Records visibility is owner-only even within the *same* division | Code review, **confirmed by live execution** | TC-REC-005 | **Confirmed true, not a defect** — using two working Consumer Protection Division Employee accounts (`elijahlucena`, `bongpalitao`), `GET /Records/GetAll?q=...` as one returned `[]` for a record the other had just created. Owner-only scoping applies within a division too, consistent with TC-REC-006. |
| Admin dashboard may not show a Whole Office task assigned to the Admin personally | Code review only | TC-REPORT-003 | Not yet executed |
| Admin-triggered password reset exists despite a scope ruling saying it was excluded | Code review, **confirmed by live execution** | TC-AUTH-013 | **Confirmed true, not a defect in itself** — `POST /UserManagement/ResetPassword` is real and working (issued a genuine temporary password for `elijahlucena`). This remains a documentation/scope conflict (see traceability matrix C-2): the feature works correctly, but `PROJECT-STATUS.md` says it was ruled out of scope. Still needs a scope decision, not a code fix. |

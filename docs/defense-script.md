# Capstone Defense Script

DTI OneLink: An Integrated Task, and Records Management for the Department of Trade
and Industry Laguna Provincial Office. A spoken-delivery script with anticipated panel
questions and honest, prepared answers — not a copy of the manuscript, a guide for what
to actually say.

---

## Opening (30–45 seconds)

"Good morning/afternoon, panel. We're presenting DTI OneLink, an integrated task and
records management system built for the DTI Laguna Provincial Office. Before this
system, task follow-ups were handled through phone calls, chats, and paper routing
slips, and records were tracked in spreadsheets with no centralized retention
monitoring. Our system centralizes both of those into one role-based web platform —
and everything we'll show today is the actual running system, not mockups."

## Project Context and Objectives (1–2 minutes)

"Our general objective was to design, develop, and implement a centralized platform
that streamlines task assignment, monitoring, and records handling for the office. That
breaks into four specific objectives: a Task Management Module with assignment,
deadlines, and priority; a Records Management Module with indexing, retrieval, and
retention monitoring; an automated notification system; and an Administrative
Dashboard for office-wide and division-level visibility. We'll walk through each of
these and show it actually working."

## Methodology — Why Agile, Sprint-Based (be ready to explain this clearly)

**What to say:**

"We used the Agile Methodology, organized into Sprints moving through seven stages:
Plan, Design, Develop, Test, Deploy, Review, and Launch. We chose Agile over a
Waterfall approach for three concrete reasons specific to this project:

1. **Our requirements came from a single client contact with DTI Laguna, and they
   evolved as we learned more about the office's actual workflow.** A single large
   upfront design phase would have meant building against requirements we'd have to
   throw away once we understood, for example, that record visibility needed to be
   owner-scoped rather than division-wide. Agile let us adjust after each Sprint
   instead of after the whole project.
2. **Each Sprint delivered one working, testable increment** — a module, a workflow
   rule, or a fix — which we could verify end-to-end (unit, integration, and
   system-level testing) before building the next piece on top of it. This caught
   real defects early rather than at the end, when they're expensive to fix.
3. **Continuous deployment was part of our process, not a separate final step** — every
   Sprint that passed testing went live through our GitHub Actions pipeline to
   MonsterASP. That only works cleanly with small, frequent increments, which is an
   Agile strength and a Waterfall weakness."

**If pressed "why not Waterfall / why not Scrum formally / why not another framework":**

"Waterfall assumes requirements are fully known and stable before development starts.
Ours weren't — they came from an evolving understanding of a real office's workflow, and
we needed the flexibility to revise scope (for example, deciding retention monitoring
should notify rather than automatically dispose of records) as we learned more. We
didn't run formal Scrum ceremonies like sprint retrospectives with a dedicated Scrum
Master — ours was a lightweight, sprint-based Agile process sized for a four-person
team on a capstone timeline, not a full enterprise Scrum implementation."

## Design of Software, System, Product, and/or Processes

"Each Sprint moved through our seven stages. In Plan, we picked one feature or defect
to address. In Design, we worked out schema and workflow changes before writing code
— for example, deciding how a task reassignment should carry over progress and notify
both the old and new assignee. Develop is where we implemented it, in ASP.NET Core MVC
with Entity Framework Core over SQL Server. Test is where we verified it — unit tests,
integration tests, and live system testing. Deploy pushed it to our live environment
automatically through GitHub Actions. Review is where we checked what succeeded and
what needed to carry forward. And Launch is where the feature became part of the
system DTI Laguna staff actually used, which fed back into the next Sprint's Plan
stage."

## Implementation Plan

"Because DTI Laguna has no dedicated IT staff, we deliberately built this to run on
managed cloud hosting rather than office-owned hardware — MonsterASP for the
application, Microsoft SQL Server for the database, Brevo for email delivery. Day-to-day
use needs nothing beyond a web browser. Our conversion strategy is a pilot evaluation,
not a scheduled parallel cutover, because the system doesn't replace one single prior
system — it replaces an informal mix of spreadsheets and memory-based follow-ups that
different divisions handled inconsistently."

## Implementation Results — ISO/IEC 25010 Evaluation

"We evaluated the system using an ISO/IEC 25010-based instrument covering nine quality
characteristics. As of this draft, seven of our twenty targeted respondents have
completed the survey, giving an overall weighted mean of 4.67, interpreted as Strongly
Agree / Excellent, with every characteristic in that same range. Reliability scored
comparatively lowest at 4.46 — we'll explain why below."

**If asked "why only 7 of 20 respondents":**

"The remaining thirteen weren't available within our data-gathering window — DTI Laguna
staff have their own operational duties, and we didn't want to rush or pressure
responses just to hit a number. We're continuing to collect the remaining responses and
are reporting this honestly as a limitation, not projecting results for people who
haven't actually used the system."

**If asked "why did Reliability score lowest":**

"It was driven mainly by one item — 'the system worked without errors during the
demonstration' — where ratings were more spread out than other items, including one
Neutral response. It still falls in the Excellent band, but it tells us demonstration-day
reliability is the area most worth attention before full rollout, and we've already
identified it as a recommendation rather than waiting for it to come up as a
surprise."

## Testing — What We Can Say With Confidence

"We tested at four levels: unit tests (285 cases across the service and controller
layers), integration tests, black-box system testing executed live against the running
application, and the user acceptance evaluation just described. At the system-testing
level, we verified authentication, user and role management, records scoping, and
notification delivery — 32 cases passed, zero failed, with one real defect found and
fixed during execution: an unauthenticated request to our Tasks route was returning a
bare 403 instead of redirecting to login, inconsistent with every other protected route.
We fixed it the same day and retested it."

**If asked "is everything tested":**

"Not yet — and we'll say that plainly rather than imply otherwise. Our Task Management
and Proof/Review workflows have been verified at the unit and integration level but not
yet executed as live system tests the way Authentication and Records have been. That's
a known, documented gap in our testing plan, not something we're hiding."

**If asked about performance / load:**

"We ran JMeter load tests and found a real issue: under a mid-load scenario, 30% of
requests failed with response times up to 139 seconds, caused by our hosting tier's cold
start behind an idle period. A warm run showed 0 errors and sub-2-second responses. This
is a known risk tied to our free/shared hosting tier, and our plan to address it is
either a keep-alive mechanism or a hosting tier upgrade before full office-wide rollout —
we're not presenting this as solved, we're presenting it as identified and planned for."

## Scope and Limitations — Defend the Boundaries as Decisions, Not Gaps

"Our scope was deliberately bounded to a single office, to decision-support rather than
automated decision-making — for example, our priority suggestion is a fixed rule based
on days until due date, not a machine-learning model — to internal users only, and to
task and records functions rather than financial or HR systems. These were intentional
design decisions to keep the project feasible within a capstone timeframe while still
solving the problems DTI Laguna actually raised during requirements gathering."

**If asked "does records retention follow the National Archives of the Philippines
schedule, since that's in your objectives":**

Be honest here rather than overstating it: "Our Records Module lets staff enter a
retention period for each record and flags it as due-soon or ended so the office can act
on it — it does not look up or enforce the specific NAP Records Disposition Schedule
automatically; the retention period is entered by the office based on whatever schedule
applies to that record series. That's a gap between how our objective was originally
worded and what we actually built, and we'd rather state that clearly than claim
automated NAP compliance we can't demonstrate."

(This question is very likely to come up, since the wording is in your own Chapter 1.
Consider softening that objective's wording before the defense to say the system
"supports office-defined retention periods consistent with the office's own records
disposition practice" rather than claiming NAP-schedule compliance directly — that
removes the mismatch instead of just having an answer ready for it.)

**If asked "why is records visibility owner-only, even within the same division, even
for the Super Admin":**

"That was a deliberate scoping decision in the code, confirmed through live testing —
each user sees only the records they personally logged. We can discuss whether that's
the right call for the office's actual workflow, since a Division Chief might
reasonably want to see all of their division's records, but as built today, it's
consistently owner-scoped, and we verified that consistency rather than assuming it."

## Conclusion (closing statement)

"Taken together, our testing and evaluation support the conclusion that DTI OneLink
fulfills its general and specific objectives within the scope we defined: a working Task
Management Module, a Records Module with user-defined retention tracking, automated
notifications, and an Administrative Dashboard giving the Office of the Provincial
Director and division chiefs a consolidated, real-time view. We recommend completing the
remaining user acceptance responses, running a full operating-cycle pilot to validate
retention and recurring-task behavior against real workloads, and establishing a backup
and credential-handover procedure consistent with the office's lack of dedicated IT
staff — and we're already positioned to act on all three."

---

## Quick Q&A Reference Sheet

| Likely Question | Short Answer |
| --- | --- |
| Why Agile/Sprint, not Waterfall? | Requirements evolved from one client contact; small testable increments caught defects early; continuous deployment needs frequent small releases |
| Why only 7/20 UAT respondents? | Honest limitation, remaining respondents still being collected, not projected |
| Why did Reliability score lowest? | One demonstration-reliability item had a wider rating spread; still "Excellent," flagged as a real recommendation |
| Is the whole system tested? | No — Auth/Records/Notifications are live-tested; Task/Proof workflows are unit/integration-tested only, a disclosed gap |
| Does retention follow NAP schedules? | No — it's an office-entered retention period with due-soon/ended flags, not automated NAP-schedule lookup; recommend rewording the objective before the defense |
| Why is priority suggestion not AI? | Deliberate scope decision — decision-support via a fixed rule, not automated decision-making |
| Can a Division Chief see all their division's records? | No, visibility is owner-only by design — open question worth discussing with the panel, not a bug |
| What about performance under load? | Measured 30% failure under mid-load due to hosting cold-start; plan is a keep-alive mechanism or tier upgrade before full rollout |
| What happens if the office's internet goes down? | System is unusable — accepted trade-off since there's no local-server fallback, given the office has no IT staff to maintain one |
| Who maintains this after you graduate? | Credential handover (GitHub, MonsterASP, Brevo) to the office or the school is planned but not yet completed — a real, disclosed open item |

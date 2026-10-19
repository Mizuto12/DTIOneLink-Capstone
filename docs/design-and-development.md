# Design of Software, System, Product, and/or Processes

DTI OneLink: An Integrated Task and Records Management System for DTI Laguna Provincial
Office.

## Development Methodology

The development of DTI OneLink followed the **Agile Methodology**, organized into
repeating Sprints, each moving through seven stages: **Plan, Design, Develop, Test,
Deploy, Review, and Launch** (see Figure 1. Agile Methodology). Rather than building the
entire system in one continuous phase, the project was divided into Sprints, each
delivering one working feature, module, or fix — aligned with DTI Laguna Provincial
Office's actual task-coordination and records-management needs — before the next Sprint
began.

### Plan

At the start of each Sprint, the team identified a single feature, module, or defect to
address — for example, "add task reassignment within a division" or "fix the login
outage caused by a SQL dependency conflict." Scope was deliberately kept narrow per
Sprint so that each change could be fully understood, tested, and reasoned about on its
own.

### Design

Before coding began, the team worked out how the feature fit the existing system: what
database schema changes it needed (expressed later as an EF Core migration), which role
(SuperAdmin, Admin, or Employee) it affected, and how it fit the existing workflow rules
— for example, deciding that a task reassignment should carry over progress and notify
both the outgoing and incoming assignee, before any of that was implemented.

### Develop

The planned design was implemented directly against the existing codebase: an ASP.NET
Core MVC application with Entity Framework Core over Microsoft SQL Server, using
session-based authentication and three roles (SuperAdmin, Admin, Employee). Each Sprint
produced a working increment of the software that was functional and aligned with the
overall project objectives.

### Test

Each Sprint's output was verified before being considered done — through unit tests,
integration tests, and, where the change affected user-facing behavior, a black-box
system-testing pass executed against the running application. Defects found during this
step were logged, fixed, and retested with the same test case before being closed,
rather than assumed fixed.

### Deploy

Every change that passed testing was published through a GitHub Actions workflow that
builds and deploys the application to MonsterASP (Web Deploy), so each Sprint reached
the live environment without manual file copying or server access.

### Review

At the end of each Sprint, the team reviewed what was completed — against commit
history, requirements documentation, and status notes — and, where applicable, against
feedback from DTI Laguna staff, to assess what succeeded and what needed to carry into
the next Sprint.

### Launch

Once reviewed, the increment became part of the live system actually available to DTI
Laguna users, closing the loop before the next Sprint's Plan stage began. The on-site
demonstration and the ongoing ISO/IEC 25010 user-acceptance evaluation are the clearest
example of this stage at a project-wide scale, rather than per individual Sprint.

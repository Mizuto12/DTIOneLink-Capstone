# Conclusion

DTI OneLink: An Integrated Task and Records Management System for the Department of
Trade and Industry Laguna Provincial Office was designed, developed, and evaluated to
address the fragmented task coordination, manual records management, delayed
follow-ups, and absence of centralized monitoring that previously characterized the
office's internal operations. The system integrates task assignment and monitoring,
records classification and retention tracking, automated notifications, role-based
dashboards, and administrative reporting into a single web-based platform built on
ASP.NET Core MVC with Microsoft SQL Server.

Functional testing across all six core modules — Log-in, User and Role Management, Task
Management, Records, Time Logs, and Notifications — confirmed that the system enforces
its intended access boundaries and workflow rules: role-based restrictions hold under
direct, system-level attempts to bypass them; the task status workflow cannot be
skipped or reopened out of sequence; account lockout, anti-forgery protection, and file
validation behave as specified; and notifications fire exactly once per qualifying
event rather than being duplicated or dropped. These results indicate that the system
meets its functional requirements as defined in Chapter 1 and that its security controls
hold up against the kinds of misuse an internal government office would realistically
encounter, such as privilege escalation attempts, forged form submissions, and disguised
file uploads.

User acceptance evaluation, conducted with seven DTI Laguna personnel using an
ISO/IEC 25010-based instrument, produced an overall weighted mean of 4.67, interpreted
as "Strongly Agree / Excellent," with every one of the nine quality characteristics —
Functionality, Performance Efficiency, Compatibility, Usability, Reliability, Security,
Maintainability, Portability, and Overall Acceptance — falling within the same
excellent range. Reliability scored comparatively lowest at 4.46, suggesting it is the
area most worth attention in any post-deployment refinement. Because only seven of the
twenty targeted personnel were able to respond within the evaluation period, these
results should be read as a strong early indication of the system's acceptability
rather than a conclusive, office-wide judgment; completing the evaluation with the
remaining personnel remains a recommended next step before full office-wide rollout.

Taken together, the testing and evaluation results support the conclusion that DTI
OneLink successfully fulfills its general and specific objectives: it provides a
working Task Management Module with assignment, monitoring, and deadline prioritization;
a Records Management Module that classifies and indexes records with a user-defined
retention period, flagging entries as due-soon or ended so the office can act on them
manually; an automated notification system that removes the office's
previous dependence on manual, memory-based follow-up; and an Administrative Dashboard
that gives the Office of the Provincial Director and division chiefs a consolidated,
real-time view of task and records activity.

The system's scope was deliberately bounded — to a single office, to decision-support
rather than automated decision-making for employee recognition, to internal users only,
and to task and records functions rather than financial or human-resource systems — and
these boundaries should be read as intentional design decisions rather than
shortcomings; they kept the project feasible within the capstone timeframe while still
addressing the operational problems the DTI Laguna Provincial Office identified during
requirements gathering.

Overall, DTI OneLink demonstrates that a centralized, role-based, workflow-driven
platform can measurably improve the organization, accountability, and efficiency of
task and records management in a provincial government office. The researchers
recommend that, prior to full deployment, the office complete the user acceptance
evaluation with its remaining personnel, pilot the system for a full operating cycle to
validate the records retention and recurring-task features against real monthly and
quarterly workloads, and establish a backup and credential-handover procedure
consistent with the office's lack of dedicated IT staff. With these steps, DTI OneLink
is positioned to serve as a practical, sustainable digital solution for the DTI Laguna
Provincial Office's day-to-day administrative operations.

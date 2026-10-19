# Implementation Plan

DTI OneLink: An Integrated Task and Records Management System for DTI Laguna Provincial
Office.

## Overview of the System

DTI OneLink addresses the fragmented task follow-ups, manual spreadsheet-based records
tracking, and lack of centralized monitoring at DTI Laguna Provincial Office. The system
centralizes task assignment, monitoring, and deadline prioritization; records
classification and retention tracking; and automated in-app/email notifications into one
role-based web application for OPD, division chiefs, and employees across the Business
Development Division, Financial and Administrative Unit, and Consumer Protection
Division.

Because the office has no dedicated IT staff, the system was built to run on managed
cloud hosting rather than office-owned hardware, so day-to-day use needs nothing beyond
a web browser.

## Major Tasks in Implementation

1. **Requirements Gathering and Analysis** — Consulted OPD staff, division chiefs, and
   employees to identify task-coordination and records-retention pain points, and
   confirmed the system's boundaries (e.g., retention monitoring is notification-only,
   not automated disposal; attendance tracking is out of scope).

2. **System Design and Development** — Built on ASP.NET Core MVC with Entity Framework
   Core over Microsoft SQL Server, using session-based authentication and three roles
   (SuperAdmin, Admin, Employee). Schema changes are applied automatically through EF
   Core migrations at startup, with an idempotent-SQL fallback for the hosted
   environment.

3. **Continuous Deployment** — A GitHub Actions workflow builds and publishes every
   merged change to MonsterASP (Web Deploy); Brevo delivers OTP and notification emails.
   Neither requires the office to operate its own server or mail system.

4. **Internal Testing** — Verified the system through unit tests, integration tests, a
   black-box system-testing pass executed against the running application, and a
   user-acceptance evaluation.

5. **On-Site Demonstration and Evaluation** — Conducted a live demonstration and
   hands-on walkthrough at DTI Laguna Provincial Office, where staff used the Dashboard,
   Task Management, and Records Management modules before completing the ISO/IEC 25010
   evaluation instrument.

## Resources Needed

| Category | Resource |
| --- | --- |
| Client hardware | Any office computer, laptop, or mobile device with a modern web browser |
| Server hardware | None — hosting is managed by the provider (MonsterASP); the office owns no server |
| Hosting | MonsterASP (ASP.NET Core hosting), deployed via GitHub Actions |
| Database | Microsoft SQL Server, managed by the hosting provider |
| Email | Brevo (transactional email API) for OTP and notification delivery |
| Network | A standard office internet connection |
| Personnel | OPD (SuperAdmin), Division Chiefs (Admin), employees (Employee) — no dedicated IT staff needed |

## Conversion Strategy

**Pilot evaluation.** DTI OneLink does not replace one single prior system, so there is
no fixed-date parallel cutover. Adoption is validated through the live demonstration and
ongoing ISO/IEC 25010 survey (currently 7 of 20 targeted respondents), with the
remaining respondents, the IT Expert evaluation, and a signed client acceptance decision
as the gate before divisions retire their previous manual tracking methods.

## Site-Specific Requirements

- No local server room, UPS, or LAN infrastructure is needed — hosting is fully managed.
- Hosting and email credentials (GitHub, MonsterASP, Brevo) must be formally handed over
  to whoever maintains the system after the capstone concludes, since DTI Laguna has no
  in-house IT staff.
- The system is unusable during an office-side internet outage, a deliberate trade-off
  given the office's lack of IT staff to maintain local infrastructure.
- Backups rely on the hosting provider's and database server's own facilities; the
  application has no separate in-app backup/restore feature.

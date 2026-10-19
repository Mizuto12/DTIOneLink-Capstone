# Level 3 — Performance Testing Results

## Existing performance-test report (inspected first, per process)

`docs/testing/four-levels-of-testing.md` already contains a real, previously-executed
performance test — not fabricated for this document, and not re-run here:

> Apache JMeter, 20 concurrent threads, 1 loop each, against the live deployed site:
>
> | Run | Errors | Avg response | Max response |
> | --- | --- | --- | --- |
> | Cold start | 0/20 | 15.3 s | 27.4 s |
> | Mid-load (same host, different moment) | 6/20 (30%) | 30.0 s | 139.3 s |
> | Warm | 0/20 | 1.46 s | 2.1 s |

That document notes: "No app-level errors occurred in any run (every failure was a
connection timeout/reset at the network layer, not a 4xx/5xx from the app)." That existing
result already covers what this Level 3 pass would otherwise have tried to measure against
the live site.

## New testing performed in this pass

**Not executed.** Per the task's explicit constraint, a new load test against the live
MonsterASP production site requires explicit authorization that was not given in this
session, so none was run. No new test was run against a local/staging environment in this
pass either — that would require installing and configuring a load-testing tool (e.g. k6,
Apache JMeter, or a scripted concurrent-request harness) and agreeing on target endpoints,
concurrency, and duration first, none of which has happened yet.

**Status: Not Executed — requires an approved local/staging environment** (or explicit
authorization to test the production site again).

## If a new local/staging run is wanted

To run one properly (not invent numbers), the following would need to be decided and then
actually executed and captured from real tool output:

- **Tool/version**: not yet chosen (candidates: k6, Apache JMeter, a `wrk`/`hey`-based
  script).
- **Environment**: the local development instance only (`localhost\SQLEXPRESS02`
  database, confirmed separate from production — see the Level 3 traceability matrix),
  run with `dotnet run` locally.
- **Endpoints**: likely candidates based on what the UAT survey flagged (Reliability
  scored lowest among the ISO/IEC 25010 characteristics) — login, the Tasks list, and the
  Reports `Data` endpoint, since these are the heaviest database-querying pages.
- **Concurrency / duration**: not yet decided.
- **Metrics to report once genuinely run**: average and 95th-percentile response time,
  failure count and type (4xx/5xx vs. connection-level), and any environment limitation
  (e.g. local hardware is not representative of the shared hosting tier's behavior, so a
  local result cannot be directly compared to the existing live-site JMeter numbers above
  — it would only establish whether the *application code itself* has a performance
  problem independent of the hosting tier's cold-start behavior).

This section will be filled in with real tool output, not estimated numbers, once that
decision is made and the test is actually run.

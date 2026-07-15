# AW3 School and Path Performance Baseline

## Capture

- Source baseline: `58eb1a3`
- Runtime database sampled: `aw3_lineage_archive.db`
- Database timestamp: 2026-07-14
- Runtime state: eligible year 73, last completed world year 74
- World seed and settings: not captured
- Actor, city, and kingdom counts: not captured
- Camera state and simulation speed: not captured

Live frame metrics were not emitted by the old runtime. They remain `not captured` until
the instrumented build is run against the same save. Missing values are not acceptance
evidence.

## Runtime Timing

| Metric | p50 | p95 | max | Allocated bytes |
|---|---:|---:|---:|---:|
| Actor update | not captured | not captured | not captured | not captured |
| Kingdom updateAge | not captured | not captured | not captured | not captured |
| HistoricalSchoolRuntime frame | not captured | not captured | not captured | not captured |

## SQLite

- `PRAGMA journal_mode`: `delete`
- `PRAGMA synchronous`: `2` (`FULL`)
- 100 independent `DELETE/FULL` writes: 182.43 ms
- 100 independent `WAL/FULL` writes: 33.97 ms
- 100 independent `WAL/NORMAL` writes: 1.66 ms
- One batched `DELETE/FULL` transaction with 100 writes: 2.69 ms

The write benchmark was captured on the same database and machine before enabling WAL.

### Buffered Write Verification

- Capture date: 2026-07-15
- Provider: `System.Data.SQLite 1.0.99.0` / SQLite `3.9.2`
- Configuration: `WAL/NORMAL`, two warm-up batches, seven measured batches
- Workload: one transaction containing 100 parameterized inserts per sample
- Samples: `0.135`, `0.120`, `0.129`, `0.130`, `0.150`, `0.124`, `0.119` ms
- Median: `0.129 ms`
- Maximum: `0.150 ms`
- Acceptance ceiling: `2.08 ms`

This microbenchmark isolates the SQLite batch boundary. Runtime projection, actor work, and
UI invalidation remain part of the fresh-world acceptance run rather than this SQL number.

## School Ecology

The runtime database contains 87 active memberships. Fifty-two members meet the planned
teacher threshold of three membership years and reputation 10; no non-master member meets
the removed reputation-25 lecture threshold. The event archive contains 56 lectures and
78 school conversions.

| School | Active members | Teacher-eligible | Live canonical masters | Persisted leaders |
|---|---:|---:|---:|---:|
| bing | 5 | 3 | 1 | not captured |
| craftsman | 6 | 5 | 1 | not captured |
| dao | 1 | 0 | 1 | not captured |
| fa | 1 | 0 | 1 | not captured |
| historian | 5 | 4 | 1 | not captured |
| medical | 3 | 1 | 1 | not captured |
| merchant | 20 | 8 | 1 | not captured |
| ming | 3 | 2 | 1 | not captured |
| mo | 1 | 1 | 1 | not captured |
| nong | 5 | 3 | 1 | not captured |
| ru | 1 | 1 | 1 | not captured |
| syncretist | 2 | 0 | 1 | not captured |
| yinyang | 31 | 22 | 1 | not captured |
| zongheng | 3 | 2 | 1 | not captured |

Affiliation lifecycle counts are 36 `AtHome`, 47 `Resident`, 4 `Travelling`, and 12
historical dead rows. Formal vanilla affiliation mismatches are intentional for travelling
and resident scholars and are not repair targets.

## Path and Collection Metrics

- Path requests generated/reused: not captured
- Fast path steps: 0 before the Cultiway movement replacement
- Vanilla path steps: not captured
- School activity, retry, venue, candidate, death, and operation-key sizes: not captured
- Path request, worker, cursor, portal, passenger, and dock collection sizes: not captured

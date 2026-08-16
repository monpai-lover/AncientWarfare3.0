# Guest Office End Recovery Design

## Goal

Prevent expired historical-school guest officers from remaining permanently in
the `Serving` affiliation state after an asynchronous end write encounters a
state conflict. A recoverable conflict must not be retired as a terminal clean
failure, and existing stuck records must repair themselves through normal
annual processing.

## Current Failure

`GuestEndWriteOperation` prepares and commits the end transition on the
historical background writer. `PrepareEndInTransaction()` currently returns
`null` for several distinct conditions, including a stale affiliation snapshot,
multiple active central careers, or a career that does not match the guest
affiliation. The operation maps every `null` to `CleanFailure`.

The write buffer then retires the operation and `SchoolGuestOfficeService`
removes its pending entry. No durable state was changed, so the affiliation
remains `Serving`. The next annual scan creates a new end operation with a new
year in its key, producing the same warning indefinitely.

## Selected Approach

Use a structured preparation decision and reconcile against the current
database rows inside the same transaction.

The preparation result distinguishes:

- `Ready`: exactly one valid guest career matches the serving affiliation; close
  the career and affiliation atomically.
- `CareerAlreadyClosed`: the affiliation is still `Serving` but there is no
  active central career; close only the orphaned affiliation through the
  existing recovery path.
- `AlreadyEnded`: the durable affiliation is already closed; treat the request
  as an idempotent completion and adopt the committed state.
- `Retry`: the frozen affiliation is stale but a newer serving affiliation can
  be re-read and evaluated safely.
- `Conflict`: multiple active central careers or a unique career whose identity
  conflicts with the serving affiliation. Preserve the pending operation and
  report the exact conflict instead of silently retiring it.

## Data Flow

1. The annual guest service scan detects an expired term and registers one
   pending end operation.
2. The background transaction reads the authoritative affiliation and active
   central careers for the actor.
3. It classifies the current durable state using the preparation decision.
4. `Ready` and `CareerAlreadyClosed` commit the normal atomic end transition.
5. `AlreadyEnded` returns an idempotent committed result without a second
   mutation.
6. `Retry` releases the queued marker but retains the pending operation so the
   service retries with a refreshed snapshot.
7. `Conflict` also retains the pending operation, applies bounded backoff, and
   logs actor id, host id, active-career count, and the mismatching identity
   fields.
8. Only a durable committed or already-ended result removes the pending end.

## Existing Save Recovery

No direct save migration is required. A record already stuck in `Serving` is
encountered by the next annual service sweep. The authoritative database read
then selects the matching recovery path:

- zero active careers closes the orphan affiliation;
- one matching active career closes both records;
- an actual multi-career conflict remains pending and becomes diagnosable.

This preserves the current transactional ownership of the affiliation and
career tables.

## Error Handling

`CleanFailure` remains terminal only when the database proves that no requested
mutation can or should occur. Stale snapshots and unresolved career conflicts
are not terminal outcomes.

Retry remains bounded by the existing pending backoff. Repeated conflicts must
not create a new annual operation while the original pending operation is
retained. Diagnostic messages are emitted once per bounded retry interval, not
once per frame.

## Tests

Regression coverage will verify:

- an expired guest with one matching career closes atomically;
- an expired guest with no active career closes the orphan affiliation;
- an already-ended affiliation resolves idempotently;
- a stale frozen snapshot is re-read rather than retired;
- multiple active central careers remain pending and expose a conflict reason;
- a unique mismatching career remains pending rather than becoming
  `CleanFailure`;
- clean-failure cleanup cannot remove a pending end while its durable
  affiliation is still `Serving`;
- repeated annual processing does not create year-by-year retired operation
  keys for the same unresolved actor.

## Scope

The change is limited to guest-office end preparation, asynchronous result
classification, pending lifecycle handling, diagnostics, and focused tests. It
does not change guest appointment selection, term length, renewal probability,
court profiles, or unrelated school writes.

# AW3 History API Design

## Goal

Expose a stable, read-only history API for other history and AI mods. The API
must cover genealogy, biographies, chronicles, diplomacy history, and official
career history. Consumers must not depend on AW3's SQLite schema or internal
`core` types.

The first release also provides subscriptions for history records created after
subscription. It does not allow external mods to write history.

## Existing implementation boundary

The current data and read paths are:

- Genealogy: `ActorArchive`, `FamilyEdge`, `LineageGroup`, and `ShiBranch`,
  queried by `LineageQuery` and related projection services.
- Biography: `PersonBiography`, read by `HistoryQuery.ReadPerson`.
- Kingdom and city chronicles: `KingdomHistory` and `CityHistory`, read by
  `HistoryQuery.ReadKingdom` and `HistoryQuery.ReadCity`.
- Kingdom and city historical periods: rebuilt by `HistoryQuery` from
  chronicle events.
- Diplomacy: dialogue, proposal, marriage, operation, coalition, war, and
  settlement ledgers. Runtime relation modifiers remain current-state data,
  not historical events.
- Official careers: `CourtOfficer`, read and normalized by
  `OfficialCareerHistoryReadService`.

The existing query classes include UI-specific normalization and are mostly
internal. They remain implementation details behind the public API.

## Public surface

Add public types under:

```text
Code/api/history/
```

Namespace:

```csharp
AncientWarfare3.api.history
```

The primary entry point is:

```csharp
public static class AW3HistoryApi
```

Domain facades:

```csharp
AW3GenealogyApi
AW3BiographyApi
AW3ChronicleApi
AW3DiplomacyHistoryApi
AW3OfficialCareerApi
```

The public surface must expose DTOs only. It must not expose
`SQLiteConnection`, `SQLiteDataReader`, `HistoryEntry`, `Actor`, `Kingdom`,
`City`, or internal table items.

The facade exposes capability and lifecycle information:

```csharp
bool IsAvailable { get; }
string ApiVersion { get; }
long RuntimeDatabaseEpoch { get; }
```

Invalid IDs and unavailable archives return empty read-only results or a
`Try...` result of `false`; database exceptions do not escape to consumers.

## Query model

All event and domain queries accept a common bounded query object with:

- optional world-time lower and upper bounds;
- optional actor, kingdom, city, office, or county filters;
- optional event-domain and event-type filters;
- a positive limit with an implementation-defined hard maximum;
- an opaque cursor for stable pagination.

Paged results contain copied DTOs, `HasMore`, and `NextCursor`. The ordering is
stable and deterministic:

```text
WorldTime ASC, Domain ASC, Source ASC, RecordId ASC
```

The public API should offer both a unified event query and domain-specific
methods. Domain methods make the semantics clear, while the unified query lets
an AI build a single chronological timeline.

## Public DTOs

The common event DTO contains:

```csharp
RecordId
Domain
Source
ProjectionKey
EventType
WorldTime
WorldYear
YearText
SubjectId
TargetId
KingdomId
ContextKingdomId
SubjectName
Content
Category
```

The identity of a persisted record is the tuple `Domain + Source + RecordId`.
`ProjectionKey` is retained for deduplication of technical projections and is
not used as the sole identity.

Genealogy DTOs contain actor ID, display name, father ID, mother ID, lineage
ID, Shi branch ID, birth and death times, and alive state. Genealogy queries
include parents, children, ancestors, family-tree snapshots, lineage and Shi
branch information.

Biography DTOs preserve the stored event type, category, age, role snapshot,
king status, context kingdom, target, plain content, and historical year text.

Chronicle DTOs expose person, kingdom, and city events, plus reconstructed
kingdom reigns and city ownership periods. Reconstructed periods are read
models; they do not create or alter database records.

Diplomacy DTOs distinguish dialogue, proposal, marriage, diplomatic operation,
coalition, war, and peace-settlement records. Active relation modifiers and
runtime caches are not presented as historical events.

Career DTOs preserve kingdom, city, county, layer, office, actor, rank, grade,
appointment time, start year, end year, current state, and end reason. The API
must support both actor-centered history and office-centered history.

All collections returned by DTOs are copied and read-only from the consumer's
perspective. Names and text are historical snapshots where the database stores
snapshots; the API must not silently replace them with current live-world
objects.

## Subscription model

Subscriptions use a disposable handle:

```csharp
IDisposable Subscribe(
    AW3HistorySubscription filter,
    Action<AW3HistoryEvent> handler);
```

Filters can select domains, event types, actor ID, and kingdom ID. The first
release sends only records created after subscription. Existing records are
loaded with the query API, not replayed automatically.

History events are published only after the write transaction commits. A
rollback produces no event. The initial event sources are the canonical
history writes for biography, kingdom, city, diplomacy, and official-career
records, plus explicitly persisted genealogy changes. Pure state-cache changes
are excluded.

Event delivery is queued and drained on the main thread. A consumer callback
must be lightweight and should copy the event into its own AI queue. Each
consumer is isolated: an exception from one handler is logged and cannot stop
other handlers or historical persistence. `Dispose` is idempotent.

The subscription registry is cleared when AW3 shuts down or the world/archive
session changes. `RuntimeDatabaseEpoch` changes with the archive lifecycle so
consumers can invalidate cached data. Loading a save does not re-publish old
records.

## Thread and archive safety

Synchronous public queries return detached DTO snapshots. They do not return
live WorldBox objects or database handles. Main-thread calls may use the
current read path directly. Calls from another thread must use an isolated
read connection or the existing background-read infrastructure; the shared
operating connection must not be used concurrently by an external thread.

The public API must tolerate these states:

- AW3 has not initialized its archive;
- a save is being loaded or the archive is temporarily unavailable;
- an old save is missing newer optional columns;
- one malformed record is present.

Unavailable archives produce `IsAvailable == false` and empty results. A bad
row is skipped where possible. DTO conversion must complete before a result or
event is handed to an external mod.

## Data flow

```text
History write or persisted domain transition
        -> database transaction
        -> commit
        -> public DTO conversion
        -> queued subscription delivery
```

Queries use this path:

```text
external mod
        -> AW3 public facade
        -> internal adapter
        -> existing query/service
        -> detached public DTO/page
```

No external consumer may query the SQLite file directly. This allows table
names and internal normalization to evolve without breaking consumers.

## Validation

Add focused tests for:

- DTO conversion, null/default values, detached collections, and old schemas;
- genealogy parents, children, ancestors, and family snapshots;
- biography, kingdom, city, diplomacy, and career queries;
- time filtering and cursor pagination;
- stable ordering and record identity;
- post-commit publication and rollback suppression;
- subscription filtering, disposal, duplicate suppression, and handler
  isolation;
- save-load epoch changes and no replay of old records;
- public API source guards preventing SQLite and internal WorldBox types from
  appearing in the public contract.

The implementation must pass the focused rules tests, the relevant source
guards, `dotnet build AncientWarfare3.csproj`, and `git diff --check`.

## Author usage document

After implementation, add:

```text
docs/api/history-api-usage.md
```

It will show how another mod references AW3, checks availability, reads a
biography and genealogy snapshot, reads official careers, subscribes to new
events, disposes the subscription, handles archive epochs, and keeps heavy AI
work off the callback. The usage document will state the API version and the
supported event/domain constants.

## Non-goals

- No public SQLite connection or table-schema contract.
- No external write API in the first release.
- No automatic historical-event replay on subscription.
- No conversion of current relation caches into fake historical events.
- No unrelated refactor of existing UI history queries.

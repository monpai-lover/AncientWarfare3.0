# AW3 History API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a stable, read-only history API with domain queries, a unified chronological event stream, and post-commit subscriptions for other history AI mods.

**Architecture:** Public DTOs and facades live under `Code/api/history/` and never expose SQLite or WorldBox runtime objects. Internal adapters reuse existing `HistoryQuery`, `LineageQuery`, diplomacy ledgers, and `OfficialCareerHistoryReadService`; a separate event publisher receives explicit post-commit notifications and drains callbacks on the main thread.

**Tech Stack:** C#, .NET Framework 4.8, System.Data.SQLite, existing AW3 async read/write workers, NUnit-style executable rules tests in `Tests/AncientWarfare3.Rules.Tests`.

---

## Scope and working-tree rule

The repository currently contains unrelated user edits in:

```text
Code/core/pathfinding/AWPathDiagnostics.cs
Code/core/pathfinding/AWPathFinder.cs
Code/core/pathfinding/AWPathLifecycleRules.cs
Code/core/pathfinding/AWStreamingPathGenerator.cs
Code/core/policy/RuntimePerformanceDiagnostic.cs
```

Do not reset, reformat, stage, or commit those files. Every commit in this
plan must stage only the files listed for that task.

## File map

Create public contracts and facades in:

```text
Code/api/history/AW3HistoryApi.cs
Code/api/history/AW3HistoryContracts.cs
Code/api/history/AW3HistoryQuery.cs
Code/api/history/AW3HistoryPage.cs
Code/api/history/AW3HistorySubscription.cs
Code/api/history/AW3GenealogyApi.cs
Code/api/history/AW3BiographyApi.cs
Code/api/history/AW3ChronicleApi.cs
Code/api/history/AW3DiplomacyHistoryApi.cs
Code/api/history/AW3OfficialCareerApi.cs
```

Create internal adapters in:

```text
Code/core/historyapi/AW3HistoryCursorRules.cs
Code/core/historyapi/AW3HistoryReadConnection.cs
Code/core/historyapi/AW3HistoryDtoMapper.cs
Code/core/historyapi/AW3HistoryReadService.cs
Code/core/historyapi/AW3HistoryEventPublisher.cs
Code/core/historyapi/AW3HistorySubscriptionRegistry.cs
```

Add focused test sources and source guards in:

```text
Tests/AncientWarfare3.Rules.Tests/HistoryApiContractTests.cs.txt
Tests/AncientWarfare3.Rules.Tests/HistoryApiCursorRulesTests.cs.txt
Tests/AncientWarfare3.Rules.Tests/HistoryApiSubscriptionRulesTests.cs.txt
Tests/HistoryApiPublicSurfaceSourceGuard.ps1
```

Add the author guide after the public surface is stable:

```text
docs/api/history-api-usage.md
```

## Task 1: Define the public contract

**Files:**

- Create the public contract files under `Code/api/history/`.
- Test: `Tests/AncientWarfare3.Rules.Tests/HistoryApiContractTests.cs.txt`.
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj` to link the contract files and test source.
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt` to add the `--history-api-contract` test switch.

- [ ] **Step 1: Add failing contract tests.**

Write tests that instantiate a page and event, verify default IDs and strings,
verify that a supplied list is copied, and verify that query limits reject zero
and negative values while clamping values above the hard maximum. The test
must also assert that the public types do not contain fields or properties of
types `SQLiteConnection`, `Actor`, `Kingdom`, or `City`.

```csharp
public void HistoryPageCopiesItemsAndCursor()
{
    var source = new List<AW3HistoryEvent> {
        new AW3HistoryEvent(1L, "chronicle", "KingdomHistory", 1L,
            "founded", 1.0, 1, "year 1", -1L, -1L, 10L, 10L,
            "Realm", "founded", "life", "")
    };
    AW3HistoryPage<AW3HistoryEvent> page =
        AW3HistoryPage<AW3HistoryEvent>.Create(source, true, "next");
    source.Clear();

    Assert.AreEqual(1, page.Items.Count);
    Assert.IsTrue(page.HasMore);
    Assert.AreEqual("next", page.NextCursor);
}
```

- [ ] **Step 2: Run the focused test and verify it fails.**

Run from `F:\WorldBox New Mod\AncientWarfare3.0`:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --history-api-contract
```

Expected: compilation failure because the public history contract does not yet
exist.

- [ ] **Step 3: Implement the immutable public contract.**

Define the following stable types:

```csharp
public static class AW3HistoryApi
{
    public const string ApiVersion = "1.0";
    public static bool IsAvailable { get; }
    public static long RuntimeDatabaseEpoch { get; }
    public static AW3HistoryPage<AW3HistoryEvent>
        ReadEvents(AW3HistoryQuery query);
    public static IDisposable Subscribe(
        AW3HistorySubscription filter,
        Action<AW3HistoryEvent> handler);
}

public sealed class AW3HistoryQuery
{
    public double WorldTimeFrom { get; }
    public double WorldTimeTo { get; }
    public long ActorId { get; }
    public long KingdomId { get; }
    public long CityId { get; }
    public string OfficeId { get; }
    public int Limit { get; }
    public string Cursor { get; }

    public static AW3HistoryQuery ForActor(long actorId);
    public static AW3HistoryQuery ForKingdom(long kingdomId);
}

public sealed class AW3HistoryPage<T>
{
    public IReadOnlyList<T> Items { get; }
    public bool HasMore { get; }
    public string NextCursor { get; }
}

public sealed class AW3HistorySubscription
{
    public static AW3HistorySubscription All { get; }
    public static AW3HistorySubscription ForKingdom(long kingdomId);
}
```

Use `-1` for absent numeric filters, empty strings for absent text filters, a
maximum page size of 512, and a copied `ReadOnlyCollection<T>` for every page.
The event DTO must contain the identity, domain, source, projection key,
event type, world time/year/text, subject/target/context IDs, names, content,
and category described in the design document.

- [ ] **Step 4: Run the focused test and verify it passes.**

Run the same `dotnet run -- --history-api-contract` command. Expected: PASS,
with no production game assembly or SQLite reference required by the pure
contract tests.

- [ ] **Step 5: Commit the public contract.**

```powershell
git add -- Code/api/history Tests/AncientWarfare3.Rules.Tests/HistoryApiContractTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "feat: add public history API contracts"
```

## Task 2: Implement cursor rules and isolated reads

**Files:**

- Create `Code/core/historyapi/AW3HistoryCursorRules.cs`.
- Create `Code/core/historyapi/AW3HistoryReadConnection.cs`.
- Create `Code/core/historyapi/AW3HistoryDtoMapper.cs`.
- Create `Tests/AncientWarfare3.Rules.Tests/HistoryApiCursorRulesTests.cs.txt`.
- Modify `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj` to link the three pure internal rule/mapper files used by tests.
- Modify `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt` to add the `--history-api-cursor` test switch.

- [ ] **Step 1: Add failing cursor and mapping tests.**

Test that cursor ordering compares `(WorldTime, Domain, Source, RecordId)` in
that order, encodes and decodes invariantly, rejects malformed cursors, and
that null database values map to `-1` or an empty string without throwing.

- [ ] **Step 2: Run the focused cursor test and verify it fails.**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --history-api-cursor
```

Expected: compilation failure for the missing cursor and mapper rules.

- [ ] **Step 3: Implement the cursor and connection rules.**

Encode cursor values as a versioned, opaque Base64 string containing invariant
binary or invariant text values. The decoder must reject a different version,
missing fields, `NaN`, and non-finite time values. `AW3HistoryReadConnection`
must capture the current database path and epoch, open
`LineageArchivePragmaService.SnapshotReadOnlyConnectionString(path)` on a
foreign thread, and discard the result if the epoch changes before conversion
finishes.

Extend the internal read context used by `HistoryQuery` and `LineageQuery` so
both can use a thread-local read connection. Existing main-thread behavior
must remain unchanged. No public type may expose the connection.

- [ ] **Step 4: Run cursor tests and the existing history tests.**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --history-api-cursor
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
```

Expected: PASS for both commands.

- [ ] **Step 5: Commit cursor and read-isolation support.**

```powershell
git add -- Code/core/historyapi Code/core/lineage/HistoryQuery.cs Code/core/lineage/LineageQuery.cs Tests/AncientWarfare3.Rules.Tests/HistoryApiCursorRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "feat: add isolated history API reads"
```

## Task 3: Add genealogy, biography, and chronicle adapters

**Files:**

- Create `Code/core/historyapi/AW3HistoryReadService.cs`.
- Create `Code/api/history/AW3GenealogyApi.cs`.
- Create `Code/api/history/AW3BiographyApi.cs`.
- Create `Code/api/history/AW3ChronicleApi.cs`.
- Modify `Code/api/history/AW3HistoryApi.cs` to delegate unified reads.
- Add focused cases to `Tests/AncientWarfare3.Rules.Tests/HistoryApiContractTests.cs.txt`.

- [ ] **Step 1: Add failing adapter tests.**

Use fixed in-memory adapter rows or test doubles for the mapper and assert that
the public results preserve father/mother IDs, role snapshots, categories,
target IDs, historical year text, and reign start/end values. Assert that a
reconstructed reign is returned as a DTO and does not write a row.

- [ ] **Step 2: Run the test and verify it fails.**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --history-api-contract
```

Expected: failures for the missing genealogy, biography, and chronicle
facades.

- [ ] **Step 3: Implement the three domain facades.**

Map the existing read paths without changing their UI behavior:

```csharp
AW3GenealogyApi.GetParents(long actorId)
AW3GenealogyApi.GetChildren(long actorId)
AW3GenealogyApi.GetAncestors(long actorId, int maxDepth)
AW3GenealogyApi.GetFamilyTree(long actorId)

AW3BiographyApi.GetEntries(long actorId, AW3HistoryQuery query)

AW3ChronicleApi.GetKingdomEvents(long kingdomId, AW3HistoryQuery query)
AW3ChronicleApi.GetCityEvents(long cityId, AW3HistoryQuery query)
AW3ChronicleApi.GetReigns(long kingdomId)
AW3ChronicleApi.GetCityPeriods(long cityId)
```

`HistoryEntry` must be copied into public biography/chronicle DTOs. Keep UI
normalization inside the internal adapter and do not replace historical names
with live names in the public result. Apply cursor filtering after the common
sort and before page construction.

- [ ] **Step 4: Run history and build checks.**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --history-api-contract
dotnet build AncientWarfare3.csproj
```

Expected: PASS and a successful AW3 build.

- [ ] **Step 5: Commit the three read domains.**

```powershell
git add -- Code/api/history Code/core/historyapi Tests/AncientWarfare3.Rules.Tests/HistoryApiContractTests.cs.txt
git commit -m "feat: expose genealogy and chronicle history"
```

## Task 4: Add diplomacy and official-career adapters

**Files:**

- Create `Code/api/history/AW3DiplomacyHistoryApi.cs`.
- Create `Code/api/history/AW3OfficialCareerApi.cs`.
- Modify `Code/core/historyapi/AW3HistoryReadService.cs` and
  `Code/core/historyapi/AW3HistoryDtoMapper.cs`.
- Add `Tests/AncientWarfare3.Rules.Tests/HistoryApiDiplomacyCareerTests.cs.txt`.
- Modify `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`.
- Modify `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt` to add the `--history-api-diplomacy-career` test switch.

- [ ] **Step 1: Add failing diplomacy and career tests.**

Cover dialogue pairs, proposal status, marriage start/end, operation result,
coalition lifetime, war/peace ledger identity, and all career fields including
county, layer, office, rank, grade, current state, end reason, and appointed
time. Add a test asserting that `DiplomaticRelationModifier` rows and runtime
caches are not emitted as historical events.

- [ ] **Step 2: Run the focused test and verify it fails.**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --history-api-diplomacy-career
```

Expected: compilation or assertion failure before the adapters exist.

- [ ] **Step 3: Implement explicit ledger readers.**

Use a read connection supplied by the internal service. Reuse
`DiplomacyConversationService` where its result is a historical conversation
record; otherwise use parameterized SQL adapters for the persisted diplomacy
ledgers. Do not return active-only relation modifiers. Map
`OfficialCareerHistoryReadService.Read` into a public DTO and add an actor-
centered query by reading the same `CourtOfficer` rows with an actor filter.

Expose:

```csharp
AW3DiplomacyHistoryApi.GetEvents(long kingdomId, AW3HistoryQuery query)
AW3DiplomacyHistoryApi.GetEventsBetween(long firstId, long secondId,
    AW3HistoryQuery query)
AW3OfficialCareerApi.GetHistory(long actorId, AW3HistoryQuery query)
AW3OfficialCareerApi.GetOfficeHistory(long kingdomId, string layer,
    string officeId, AW3HistoryQuery query)
```

Normalize pair IDs in both directions so the same diplomatic relationship is
not split into two queries. Bound every ledger query and map missing optional
columns to documented defaults.

- [ ] **Step 4: Run focused tests and build.**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --history-api-diplomacy-career
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
dotnet build AncientWarfare3.csproj
```

Expected: all commands pass.

- [ ] **Step 5: Commit diplomacy and career reads.**

```powershell
git add -- Code/api/history Code/core/historyapi Tests/AncientWarfare3.Rules.Tests/HistoryApiDiplomacyCareerTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "feat: expose diplomacy and career history"
```

## Task 5: Implement post-commit subscriptions

**Files:**

- Create `Code/core/historyapi/AW3HistoryEventPublisher.cs`.
- Create `Code/core/historyapi/AW3HistorySubscriptionRegistry.cs`.
- Create `Tests/AncientWarfare3.Rules.Tests/HistoryApiSubscriptionRulesTests.cs.txt`.
- Modify `Code/core/lineage/HistoryWriter.cs` to attach publication metadata to canonical history writes.
- Modify `Code/core/db/HistoricalWriteService.cs` only where committed callbacks are needed to publish async history rows.
- Modify the canonical diplomacy persistence services and official-career persistence path to publish after successful commits.
- Modify `Code/api/history/AW3HistoryApi.cs` to expose subscription methods.
- Modify `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt` to add the `--history-api-subscriptions` test switch.

- [ ] **Step 1: Add failing subscription tests.**

Test the registry in isolation:

```csharp
public void FailedCommitDoesNotPublish()
{
    var registry = new AW3HistorySubscriptionRegistryForTests();
    int count = 0;
    using (registry.Subscribe(AW3HistorySubscription.All,
        item => count++))
    {
        registry.PublishCommitted(TestEvent());
        registry.PublishFailed(TestEvent());
    }
    Assert.AreEqual(1, count);
}
```

Also test domain/actor/kingdom filters, handler exception isolation,
idempotent disposal, and that no callback runs while a write transaction is
still open.

- [ ] **Step 2: Run the focused test and verify it fails.**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --history-api-subscriptions
```

Expected: compilation failure for the missing registry and publisher.

- [ ] **Step 3: Implement the registry and queued delivery.**

Store subscriptions behind a lock and return an idempotent disposable handle.
On publication, copy the matching handler list and enqueue immutable event
objects. Drain the queue from the existing main-thread update path. Catch and
log each handler exception independently. Add a bounded queue policy that
keeps the oldest committed events and records an overflow warning rather than
blocking the historical writer.

The publisher must use `Domain + Source + RecordId` as the record identity and
discard a duplicate `ProjectionKey` for the same logical write. It must not
scan tables every frame.

- [ ] **Step 4: Connect all canonical write paths.**

For `HistoryWriter`, attach the table and allocated event ID to the existing
synchronous fallback or committed callback, then read and map the committed
row before publication. For direct diplomacy writes, publish immediately after
the successful insert when no transaction is used, and from the transaction
completion path when a transaction is used. For official-career appointment
and close operations, publish after the existing persistence callback reports
commit. Genealogy notifications use the committed biography birth record as
the canonical parent-link event; no per-frame FamilyEdge scan is introduced.

- [ ] **Step 5: Add lifecycle invalidation.**

Clear the registry and pending event queue from `AWAsyncWorldLifecycle` when a
world/archive is cleared or restarted. Expose the current
`LineageArchiveManager.RuntimeDatabaseEpoch` through the public facade. Do not
replay old rows when a save is loaded.

- [ ] **Step 6: Run subscription tests and source checks.**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --history-api-subscriptions
pwsh -File Tests/HistoryApiPublicSurfaceSourceGuard.ps1
dotnet build AncientWarfare3.csproj
```

Expected: PASS for all commands.

- [ ] **Step 7: Commit subscriptions.**

```powershell
git add -- Code/api/history Code/core/historyapi Code/core/lineage/HistoryWriter.cs Code/core/db/HistoricalWriteService.cs Code/core/lineage/DiplomacyConversationService.cs Code/core/lineage/DiplomacyProposalService.cs Code/core/lineage/DiplomaticMarriageService.cs Code/core/lineage/DiplomaticOperationService.cs Code/core/lineage/DiplomaticCoalitionService.cs Code/core/court/OfficialCareerPersistence.cs Code/core/asyncwork/AWAsyncWorldLifecycle.cs Tests/AncientWarfare3.Rules.Tests/HistoryApiSubscriptionRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt Tests/HistoryApiPublicSurfaceSourceGuard.ps1
git commit -m "feat: add history event subscriptions"
```

## Task 6: Write the external-mod author guide

**Files:**

- Create `docs/api/history-api-usage.md`.
- Add a source guard test if the documentation checker requires it.

- [ ] **Step 1: Add the complete C# usage example.**

The guide must show assembly reference, availability and epoch checks, a
biography query, parent and ancestor queries, kingdom and diplomacy history,
official career history, subscription filtering, and disposal:

```csharp
using AncientWarfare3.api.history;

public sealed class MyHistoryAi
{
    private System.IDisposable _subscription;
    private long _epoch = -1L;

    public void Start(long actorId, long kingdomId)
    {
        if (!AW3HistoryApi.IsAvailable)
            return;

        _epoch = AW3HistoryApi.RuntimeDatabaseEpoch;
        _subscription = AW3HistoryApi.Subscribe(
            AW3HistorySubscription.ForKingdom(kingdomId),
            OnHistoryEvent);

        var biography = AW3BiographyApi.GetEntries(actorId,
            AW3HistoryQuery.ForActor(actorId));
        var parents = AW3GenealogyApi.GetParents(actorId);
        var careers = AW3OfficialCareerApi.GetHistory(actorId,
            AW3HistoryQuery.ForActor(actorId));
    }

    private void OnHistoryEvent(AW3HistoryEvent item)
    {
        // Copy item data into an AI queue; do not run heavy inference here.
    }

    public void Update()
    {
        if (_epoch != AW3HistoryApi.RuntimeDatabaseEpoch)
        {
            // Clear cached IDs and re-read after the new save is ready.
            _epoch = AW3HistoryApi.RuntimeDatabaseEpoch;
        }
    }

    public void Stop()
    {
        _subscription?.Dispose();
        _subscription = null;
    }
}
```

Replace the example's convenience constructors with the final names from the
public contract and document every supported domain/event constant. Explain
that results are detached snapshots, callbacks run on the main thread, old
events are not replayed, and external mods must not read AW3's SQLite file.

- [ ] **Step 2: Review the guide against the compiled public contract.**

Run:

```powershell
rg -n "AW3HistoryApi|AW3GenealogyApi|AW3BiographyApi|AW3ChronicleApi|AW3DiplomacyHistoryApi|AW3OfficialCareerApi" docs/api/history-api-usage.md Code/api/history
```

Expected: every type and method in the example exists with the same spelling
and parameter order in `Code/api/history/`.

- [ ] **Step 3: Commit the author guide.**

```powershell
git add -- docs/api/history-api-usage.md
git commit -m "docs: add history API usage guide"
```

## Task 7: Final verification

**Files:** No source changes unless a failing verification identifies a
specific contract defect.

- [ ] **Step 1: Run all focused history API tests.**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --history-api-contract
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --history-api-cursor
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --history-api-diplomacy-career
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --history-api-subscriptions
```

Expected: PASS.

- [ ] **Step 2: Run existing history and source-guard tests.**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
pwsh -File Tests/HistoryApiPublicSurfaceSourceGuard.ps1
```

Expected: PASS.

- [ ] **Step 3: Build and check whitespace.**

```powershell
dotnet build AncientWarfare3.csproj
git diff --check
```

Expected: successful build and no whitespace errors.

- [ ] **Step 4: Confirm only intended files are committed.**

```powershell
git status -sb
git show --stat --oneline HEAD
```

Expected: the five pre-existing path/performance files remain uncommitted and
untouched by the API commits.

## Plan self-review

- Spec coverage: public facade, immutable DTOs, domain queries, unified event
  ordering, post-commit subscriptions, lifecycle epochs, isolated reads,
  error handling, tests, and author documentation each have an explicit task.
- No placeholder steps: every implementation step names concrete files,
  methods, data defaults, commands, and expected outcomes.
- Type consistency: `AW3HistoryQuery`, `AW3HistoryPage<T>`,
  `AW3HistoryEvent`, `AW3HistorySubscription`, and the five domain facades are
  introduced in Task 1 and used with the same names in later tasks.
- Scope: current-state relation caches, direct database access, external
  history writes, and unrelated path/performance edits remain outside scope.

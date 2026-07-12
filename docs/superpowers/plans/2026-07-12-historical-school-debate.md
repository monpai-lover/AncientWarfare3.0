# Historical School Debate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在现有历史先师降临、游说、真实弟子和城市学派账本基础上，加入可复现的同城学派辩论、影响力传导和最小机构创设入口。

**Architecture:** 新增纯规则层 `HistoricalSchoolDebateRules`，负责议题、配对、比分、结果和账本 delta；新增 `HistoricalSchoolDebateService`，负责年度预算、真实 actor 筛选、状态效果和历史记录；`HistoricalSchoolStore` 以单一 SQLite 事务同时写 `SchoolDebate`、`SchoolEvent` 和 `CitySchoolLedger`。现有 `CitySchoolSnapshotService` 仍是官场/UI 的读取入口，辩论不直接创建城市学派或修改人物国籍。

**Tech Stack:** C#/.NET Framework 4.8, Unity/WorldBox actor-city APIs, SQLite archive, existing `HistoricalSchoolRuntime`, `CitySchoolSnapshotService`, and temporary rule harness `F:\tmp\AW3HistoricalSchoolRuleTests`.

---

### Task 1: Add failing pure debate rules

**Files:**
- Create: `Code/core/schools/HistoricalSchoolDebateRules.cs`
- Modify: `Code/core/schools/HistoricalSchoolState.cs` (only if a value object is needed)
- Test: `F:\tmp\AW3HistoricalSchoolRuleTests\Program.cs`

- [ ] **Step 1: Write RED assertions** for deterministic topic selection, stable actor pairing, score bounds, five outcome bands, and bounded ledger deltas. The assertions must cover: common topic wins over fallback, no common/related topic returns no debate, swapping actor order swaps the winner, equal scores produce `Draw`, and a single result cannot add more than the configured momentum cap.
- [ ] **Step 2: Run the harness** with `dotnet run --project F:\tmp\AW3HistoricalSchoolRuleTests\AW3HistoricalSchoolRuleTests.csproj`; confirm it fails because the rule type/methods are absent.
- [ ] **Step 3: Implement only pure APIs** with these signatures:

```csharp
public static string SelectTopic(HistoricalSchoolMasterDefinition pFirst,
    HistoricalSchoolMasterDefinition pSecond, IEnumerable<string> pCityTopics);
public static HistoricalSchoolDebatePair SelectPair(IEnumerable<HistoricalSchoolDebateCandidate> pCandidates);
public static HistoricalSchoolDebateScore Score(HistoricalSchoolDebateCandidate pCandidate,
    string pTopic, HistoricalSchoolLedgerSnapshot pLedger);
public static SchoolDebateOutcome ResolveOutcome(double pFirstScore, double pSecondScore);
public static HistoricalSchoolLedgerDelta LedgerDelta(SchoolDebateOutcome pOutcome,
    bool pFirstWon, int pYear);
```

- [ ] **Step 4: Run the harness** again and require `AW3 historical school rules passed`.
- [ ] **Step 5: Commit** the pure rules and harness assertions with `test: define historical school debate rules`.

### Task 2: Add transactional debate and ledger persistence

**Files:**
- Modify: `Code/core/schools/HistoricalSchoolStore.cs`
- Modify: `Code/core/db/SchoolDebateTableItem.cs` only if a missing field is required by the transaction
- Modify: `Code/core/db/CitySchoolLedgerTableItem.cs` only if a bounded version/source field is required
- Test: `F:\tmp\AW3HistoricalSchoolRuleTests\Program.cs` source assertions for one transaction and all three writes

- [ ] **Step 1: Add RED source assertions** requiring a `TryRecordDebateAndLedger` API, `SQLiteTransaction`, duplicate key guard by city/year/actor pair, and rollback on any failed insert.
- [ ] **Step 2: Implement store APIs**:

```csharp
public static bool TryRecordDebateAndLedger(HistoricalSchoolDebateRecord pDebate,
    HistoricalSchoolLedgerDelta pFirstDelta, HistoricalSchoolLedgerDelta pSecondDelta,
    double pWorldTime);
public static HistoricalSchoolLedgerSnapshot LoadLedger(long pCityId, string pSchoolId);
public static bool HasDebateForYear(long pCityId, long pFirstActorId,
    long pSecondActorId, int pYear);
```

The transaction must allocate IDs before opening the transaction, re-check the duplicate predicate inside it, insert the debate row, insert the corresponding `SchoolEvent` row, upsert both affected ledger rows with clamped values, commit, and only then let runtime caches change. A failed insert must rollback every row and return `false`.
- [ ] **Step 3: Run the harness** and require the transaction/source checks to pass.
- [ ] **Step 4: Run `dotnet build AncientWarfare3.csproj -c Debug --no-restore`** before proceeding.
- [ ] **Step 5: Commit** with `feat: persist school debates and ledger deltas`.

### Task 3: Implement bounded real-actor debate selection

**Files:**
- Create: `Code/core/schools/HistoricalSchoolDebateService.cs`
- Modify: `Code/core/schools/HistoricalSchoolActionService.cs` only to remove any duplicate debate responsibility
- Modify: `Code/content/schools/HistoricalSchoolContent.cs` for debate log/status payloads
- Modify: `Code/core/schools/HistoricalSchoolRuntime.cs` to call the service once per world year
- Test: `F:\tmp\AW3HistoricalSchoolRuleTests\Program.cs`

- [ ] **Step 1: Write RED source assertions** for `ProcessYear`, the per-city budget, real actor checks, `HistoricalAffiliationService.IsPresentForInfluence`, and no `createNewUnit`.
- [ ] **Step 2: Implement candidate collection** by iterating living members in each city, resolving residence through `HistoricalAffiliationService.ResidenceCity`, rejecting dead/baby/serving/travelling actors, and grouping by school. A historical master or `SchoolLineageService.IsQualifiedTeacher` member is eligible; the same actor cannot be paired twice in a year.
- [ ] **Step 3: Implement deterministic pair selection**: order cities by id, order school groups by ledger presence and id, order actors by id, and call `HistoricalSchoolDebateRules.SelectPair`. Enforce one debate per city/year and a global annual budget before expensive scoring.
- [ ] **Step 4: Apply status/history after persistence only**. Add `DebateStatusId` to both actors, call `HistoricalSchoolStore.TryRecordDebateAndLedger`, then write the existing debate world log and both person biographies/city history. If persistence returns `false`, do not add status or mutate snapshots.
- [ ] **Step 5: Mark city caches dirty** with `CitySchoolSnapshotService.MarkDirty` and `SchoolMapModeService.DirtyMapIfActive` after commit, so influence/tooltips refresh on the next read.
- [ ] **Step 6: Run the harness and Debug build**; require the rules pass and zero compiler warnings/errors.
- [ ] **Step 7: Commit** with `feat: run historical school debates`.

### Task 4: Add the minimal historical institution hook

**Files:**
- Modify: `Code/core/schools/HistoricalSchoolStore.cs`
- Modify: `Code/core/schools/HistoricalSchoolDebateService.cs`
- Modify: `Code/core/schools/HistoricalSchoolActionService.cs`
- Test: `F:\tmp\AW3HistoricalSchoolRuleTests\Program.cs`

- [ ] **Step 1: Write RED assertions** for one institution per `InstitutionId`/city/school, real founder actor, and ledger `institutions` bounded bonus.
- [ ] **Step 2: Add `TryFoundInstitution`** that requires a canonical master, matching definition `InstitutionId`, the same city, and a lecture/debate threshold recorded in `SchoolEvent`; reject duplicate active institutions. Insert `SchoolInstitution` and its event in one transaction.
- [ ] **Step 3: Call the hook from the annual debate/action service** with a small budget after a successful lecture or debate; never create an institution without a real historical master or without a city.
- [ ] **Step 4: Mark the city snapshot dirty** after a successful institution commit and verify duplicate calls are idempotent.
- [ ] **Step 5: Run tests/build and commit** with `feat: found historical school institutions`.

### Task 5: Integration, performance, and verification

**Files:**
- Modify: `docs/superpowers/specs/2026-07-12-historical-school-debate-design.md` only if verified behavior changes the design
- Test: `F:\tmp\AW3HistoricalSchoolRuleTests\Program.cs`, `F:\tmp\AW3PathfindingRuleTests\AW3PathfindingRuleTests.csproj`

- [ ] **Step 1: Add a source audit** proving `HistoricalSchoolRuntime.OnWorldYear` reaches debate processing, every candidate is an existing actor, and no random city school assignment was added.
- [ ] **Step 2: Run the full verification set:**

```powershell
dotnet run --project F:\tmp\AW3HistoricalSchoolRuleTests\AW3HistoricalSchoolRuleTests.csproj
dotnet build AncientWarfare3.csproj -c Debug --no-restore
dotnet build AncientWarfare3.csproj -c Release --no-restore
dotnet run --project F:\tmp\AW3PathfindingRuleTests\AW3PathfindingRuleTests.csproj
git diff --check
```

- [ ] **Step 3: Inspect `git status --short`** and stage only production files and the plan/spec; keep user-deleted `Tests/` and `Verification/` files unstaged.
- [ ] **Step 4: Request a final code review** focused on transaction/cache ordering, actor authenticity, annual scan budgets, and cross-national residence invariants.
- [ ] **Step 5: Commit** the verified integration as `feat: connect school debate influence flow`; do not push unless explicitly requested.


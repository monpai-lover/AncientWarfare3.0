# King Death Succession Root-Cause Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 根治国王死亡时的重复继位准备、无变化继承人重复维护、死亡帧争议全量计算、谱系 N+1 查询和科举同步事务，使 AW3 死王热路径稳定在 2 ms 目标内。

**Architecture:** 原版 `KingdomBehCheckKing` 保持唯一继位安装控制器；AW3 在稳定统治期通过主线程内存索引和每周期一个国家的有界队列预计算候选与争议快照，死亡时只捕获标量幂等上下文。争议和科举写入复用 `HistoricalWriteService`，后台仅处理不可变标量和 SQL，主线程完成回调按世界代数与修订号验收。

**Tech Stack:** C# / Harmony / WorldBox runtime / System.Data.SQLite / AW3 historical write worker / PowerShell source guards / .NET 9 rule tests

---

## File Map

- Create `Code/core/lineage/KingSuccessionPreparationRules.cs`: 无 WorldBox 依赖的幂等键、修订戳、消费判定和无变化判定。
- Create `Code/core/lineage/SuccessionRelationshipIndex.cs`: 活人父子、姓、氏主线程索引和增量重建游标。
- Create `Code/core/lineage/SuccessionPreparationService.cs`: 脏国家队列、稳定期候选/争议快照、死亡上下文及原版继位门。
- Create `Code/core/lineage/SuccessionDisputePersistence.cs`: 自定义历史写入 envelope 及一次事务内的争议/城市行写入。
- Create `Code/core/court/CivilServiceRulerDeathPersistence.cs`: 科举死王 compare-and-set 自定义 envelope。
- Modify `Code/core/lineage/HeirService.cs`: 候选计算改用运行时索引；相同继承人签名直接返回；移除死亡帧争议准备。
- Modify `Code/core/lineage/InheritanceCandidateService.cs`: 热路径父子、姓、氏查询改用运行时索引。
- Modify `Code/core/lineage/SuccessionDisputeService.cs`: 从预计算事实创建运行时快照并异步持久化。
- Modify `Code/core/court/CivilServiceExamService.cs`: 建立等待玩家排名的国家索引，死王路径不查 SQLite。
- Modify `Code/patch/AW_ActorDeathPatch.cs`: 死王只捕获标量上下文、使索引失效和提交轻量通知。
- Modify `Code/patch/AW_MandateSuccessionPatch.cs`: 唯一消费准备快照；未准备好则让原版行为稍后重试。
- Modify `Code/patch/AW_HeirPatch.cs`: `SuccessionTool` 只读取已发布候选；新王安装后完成并清理上下文。
- Modify `Code/core/performance/AWAuthorityCycleService.cs`: 在既有 token gate 后每周期处理一个脏国家；这是唯一调度接入点。
- Modify `Code/core/court/CourtDirectionService.cs`, `Code/core/lineage/InheritanceLawService.cs`, `Code/patch/AW_BirthPatch.cs`, `Code/patch/AW_ChroniclePatch.cs`: 触发精确修订失效。
- Modify `Code/core/db/LineageArchiveIndexRules.cs`: 加入科举死王 CAS 所需部分索引。
- Test `Tests/AncientWarfare3.Rules.Tests/KingSuccessionPreparationRulesTests.cs.txt`。
- Test `Tests/AncientWarfare3.Rules.Tests/SuccessionRelationshipIndexRulesTests.cs.txt`。
- Test `Tests/AncientWarfare3.Rules.Tests/SuccessionDisputePersistenceSqlTests.cs.txt`。
- Test `Tests/AncientWarfare3.Rules.Tests/CivilServiceRulerDeathPersistenceSqlTests.cs.txt`。
- Test `Tests/KingDeathSuccessionPerformanceSourceGuard.ps1`。
- Test `Tests/CultiwayPerfSchedulerNonRegressionSourceGuard.ps1`。

## Protected Cultiway Perf Baseline

本计划不得修改以下文件；哈希以分支提交 `ed30836` 的工作树内容为准：

```text
57D6FF4A9E4B7AE65C01DD3E9CAB847944DB6181272737FDA6A9C94513B1F2DF  Code/patch/AW_FramePrioritySchedulerPatch.cs
816BDD74B747636C997BE053FE8610763BFD7D5F6BD6C88DA6822A55233CACF4  Code/core/performance/AWCooperativeSimulationRunner.cs
3DA5988A81FD07F0364C5C831B9CE027B06923925DF6E98192C6784F3FC5E667  Code/core/performance/AWCooperativeBatchRunner.cs
A417C19A3F701706949FBC85C7F0AEF4B23B4B47F8008CAFC871E8F5EA34A372  Code/core/performance/AWCooperativeActorParallelJobRunner.cs
07BE0EA942A0AA1A3782C4DD62947517F66364DECFACA557B05EA202CABB041E  Code/core/performance/AWFrameSchedulerRules.cs
2809229EC394DD8C5CE9319A4D31AEC63198E7C5265C9246EF0C13F8F17A4E97  Code/core/performance/AWSimulationStepContext.cs
```

`F:/WorldBox New Mod/Cultiway-Reborn-perf` 只用于静态对照。禁止把继位工作塞入 `CooperativeSimulationRunner` 的 stage switch、Actor 并行 job、`MapBox.Update` 表现边界、save drain、abort 或 clear-world 顺序。

### Task 1: 锁定调度器移植基线并细分继位性能测量

**Files:**
- Create: `Tests/CultiwayPerfSchedulerNonRegressionSourceGuard.ps1`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Code/core/policy/ActorDeathPerformanceRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ActorRacePerformanceRulesTests.cs.txt`

- [ ] **Step 1: 写失败的调度非回归守卫**

守卫必须校验上面的六个 SHA-256，并验证唯一允许的扩展关系：`AW_FramePrioritySchedulerPatch` 只调用 `ProcessNativeCycle`，`AWCooperativeSimulationRunner` 只调用 `ProcessCooperativeCycle`，两者都进入 `AWAuthorityCycleService.ProcessCycle` 的 token gate。

```powershell
$expected = @{
  'Code/patch/AW_FramePrioritySchedulerPatch.cs' = '57D6FF4A9E4B7AE65C01DD3E9CAB847944DB6181272737FDA6A9C94513B1F2DF'
  'Code/core/performance/AWCooperativeSimulationRunner.cs' = '816BDD74B747636C997BE053FE8610763BFD7D5F6BD6C88DA6822A55233CACF4'
  'Code/core/performance/AWCooperativeBatchRunner.cs' = '3DA5988A81FD07F0364C5C831B9CE027B06923925DF6E98192C6784F3FC5E667'
  'Code/core/performance/AWCooperativeActorParallelJobRunner.cs' = 'A417C19A3F701706949FBC85C7F0AEF4B23B4B47F8008CAFC871E8F5EA34A372'
  'Code/core/performance/AWFrameSchedulerRules.cs' = '07BE0EA942A0AA1A3782C4DD62947517F66364DECFACA557B05EA202CABB041E'
  'Code/core/performance/AWSimulationStepContext.cs' = '2809229EC394DD8C5CE9319A4D31AEC63198E7C5265C9246EF0C13F8F17A4E97'
}
foreach ($entry in $expected.GetEnumerator()) {
  $actual = (Get-FileHash -Algorithm SHA256 $entry.Key).Hash
  if ($actual -ne $entry.Value) { throw "protected scheduler drift: $($entry.Key)" }
}
$authority = Get-Content -Raw 'Code/core/performance/AWAuthorityCycleService.cs'
if ($authority -notmatch 'if \(!pGate\.TryEnter\(pCycleToken, allowed\)\) return;') {
  throw 'authority work must remain behind the existing cycle-token gate'
}
Write-Host 'Cultiway perf scheduler non-regression guard passed.'
```

- [ ] **Step 2: 运行守卫并确认基线通过**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File Tests/CultiwayPerfSchedulerNonRegressionSourceGuard.ps1`

Expected: `Cultiway perf scheduler non-regression guard passed.`

- [ ] **Step 3: 增加可区分死亡捕获和延迟准备的诊断 ID**

保留现有 `king_heir_prepare` 兼容字段，新增连续阶段名常量并在后续服务中使用：

```csharp
public static class KingSuccessionPerformanceStage
{
    public const string DeathCapture = "king_succession_death_capture";
    public const string CandidateSnapshot = "king_succession_candidate_snapshot";
    public const string DisputeFacts = "king_succession_dispute_facts";
    public const string DisputeEnqueue = "king_succession_dispute_enqueue";
    public const string CivilLookup = "king_civil_service_lookup";
    public const string CivilEnqueue = "king_civil_service_enqueue";
}
```

- [ ] **Step 4: 把新守卫接入测试项目并运行基线测试**

在 `.csproj` 的 `BeforeTargets="Build"` target 中加入该脚本。运行：

`dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release`

Expected: `Rule tests passed.` 且调度守卫通过。

- [ ] **Step 5: 提交**

```powershell
git add Code/core/policy/ActorDeathPerformanceRules.cs Tests/CultiwayPerfSchedulerNonRegressionSourceGuard.ps1 Tests/AncientWarfare3.Rules.Tests/ActorRacePerformanceRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
git commit -m "test: freeze succession scheduler baseline"
```

### Task 2: 建立死王幂等状态机

**Files:**
- Create: `Code/core/lineage/KingSuccessionPreparationRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/KingSuccessionPreparationRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: 写失败测试覆盖重复捕获、过期快照和单次消费**

```csharp
internal static class KingSuccessionPreparationRulesTests
{
    public static void Run()
    {
        var key = new KingSuccessionKey(7, 20, 101);
        var state = new KingSuccessionPreparationState();
        True(state.TryCapture(key, revision: 4, candidateId: 202),
            "first death capture is accepted");
        Equal(false, state.TryCapture(key, 4, 202),
            "same dead king cannot prepare twice");
        state.Publish(key, revision: 4, candidateId: 202,
            mode: "registered");
        True(state.TryConsume(key, revision: 4, out var prepared),
            "matching snapshot is consumed");
        Equal(202L, prepared.CandidateId,
            "published candidate survives consumption");
        Equal(false, state.TryConsume(key, 4, out _),
            "one death context is consumed once");

        var stale = new KingSuccessionKey(7, 21, 102);
        state.TryCapture(stale, revision: 8, candidateId: 203);
        state.Publish(stale, revision: 7, candidateId: 203,
            mode: "registered");
        Equal(false, state.TryConsume(stale, 8, out _),
            "stale revision cannot install a successor");
    }
}
```

- [ ] **Step 2: 运行并确认编译失败**

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release`

Expected: FAIL，缺少 `KingSuccessionKey` 与 `KingSuccessionPreparationState`。

- [ ] **Step 3: 实现纯状态模型**

```csharp
public readonly struct KingSuccessionKey : IEquatable<KingSuccessionKey>
{
    public KingSuccessionKey(long pWorldGeneration, long pKingdomId,
        long pPredecessorId)
    {
        WorldGeneration = pWorldGeneration;
        KingdomId = pKingdomId;
        PredecessorId = pPredecessorId;
    }
    public long WorldGeneration { get; }
    public long KingdomId { get; }
    public long PredecessorId { get; }
    public bool Equals(KingSuccessionKey pOther) =>
        WorldGeneration == pOther.WorldGeneration &&
        KingdomId == pOther.KingdomId &&
        PredecessorId == pOther.PredecessorId;
    public override bool Equals(object pValue) =>
        pValue is KingSuccessionKey other && Equals(other);
    public override int GetHashCode() =>
        ((WorldGeneration * 397) ^ KingdomId).GetHashCode() * 397 ^
        PredecessorId.GetHashCode();
}

public sealed class KingSuccessionPreparationState
{
    private readonly Dictionary<KingSuccessionKey, Entry> _entries = new();

    public bool TryCapture(KingSuccessionKey pKey, long revision,
        long candidateId)
    {
        if (_entries.ContainsKey(pKey)) return false;
        _entries.Add(pKey, new Entry
        {
            CapturedRevision = revision,
            PublishedRevision = -1L,
            CandidateId = candidateId,
            Mode = string.Empty,
            Published = false
        });
        return true;
    }

    public void Publish(KingSuccessionKey pKey, long revision,
        long candidateId, string mode)
    {
        if (!_entries.TryGetValue(pKey, out Entry entry)) return;
        entry.PublishedRevision = revision;
        entry.CandidateId = candidateId;
        entry.Mode = mode ?? string.Empty;
        entry.Published = true;
        _entries[pKey] = entry;
    }

    public bool TryConsume(KingSuccessionKey pKey, long revision,
        out PreparedSuccession pPrepared)
    {
        pPrepared = default;
        if (!_entries.TryGetValue(pKey, out Entry entry) ||
            !entry.Published || entry.CapturedRevision != revision ||
            entry.PublishedRevision != revision)
            return false;
        _entries.Remove(pKey);
        pPrepared = new PreparedSuccession(entry.CandidateId, entry.Mode);
        return true;
    }

    public void Clear() => _entries.Clear();

    private struct Entry
    {
        internal long CapturedRevision;
        internal long PublishedRevision;
        internal long CandidateId;
        internal string Mode;
        internal bool Published;
    }
}

public readonly struct PreparedSuccession
{
    public PreparedSuccession(long pCandidateId, string pMode)
    {
        CandidateId = pCandidateId;
        Mode = pMode ?? string.Empty;
    }
    public long CandidateId { get; }
    public string Mode { get; }
}
```

实现中 `Entry` 和 `PreparedSuccession` 只保存数字、布尔和字符串，不保存 `Actor`、`Kingdom` 或 `City`。

- [ ] **Step 4: 运行测试并提交**

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release`

Expected: `Rule tests passed.`

```powershell
git add Code/core/lineage/KingSuccessionPreparationRules.cs Tests/AncientWarfare3.Rules.Tests/KingSuccessionPreparationRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "feat: add idempotent king succession state"
```

### Task 3: 给相同继承人增加真正的无变化快路径

**Files:**
- Modify: `Code/core/lineage/HeirService.cs`
- Create: `Tests/KingHeirNoOpSourceGuard.ps1`
- Modify: `Tests/AncientWarfare3.Rules.Tests/InheritanceLawRulesTests.cs.txt`

- [ ] **Step 1: 写失败测试定义完整签名相等条件**

测试 `candidateId + mode + referenceKingId + dirtyRevision` 全相同才允许跳过；任何一个字段变化都必须执行维护。

```csharp
True(HeirSelectionSignatureRules.IsUnchanged(
    202, "registered", 101, 9,
    202, "registered", 101, 9), "identical selection is a no-op");
Equal(false, HeirSelectionSignatureRules.IsUnchanged(
    202, "registered", 101, 9,
    203, "registered", 101, 9), "candidate change is not skipped");
Equal(false, HeirSelectionSignatureRules.IsUnchanged(
    202, "registered", 101, 9,
    202, "registered", 102, 9), "new king refresh is not skipped");
```

- [ ] **Step 2: 在任何副作用之前放置 gate**

`StoreHeirSelection` 必须先读取旧签名，并在调用以下函数前直接返回当前 actor：`FormerHeirService.ClearSnapshot`、`RoyalAsylumService.RecallForSuccession`、`RecallForeignSelectedHeir`、`ClearOldHeirFlag`、`EnsureRoyalHeirLineage`、`EnsurePersonalSchool`、`ArchiveActor`、`ReconcileTargets`。删除 `RefreshHeirAndReturn` 和旧死亡准备调用者中位于 `StoreHeirSelection` 之前的 `ClearOldHeirFlag`，把它移动到下述 gate 之后，确保相同继承人不会先扫描所有国家再命中快路径。

```csharp
long heirId = pSelection.Actor?.data?.id ?? -1L;
long referenceKingId = ResolveReferenceKingId(pKingdom, pKingdom.king);
long dirtyRevision = SuccessionPreparationService.CurrentRevision(
    pKingdom.id);
if (HeirSelectionSignatureRules.IsUnchanged(
        previousHeirId, previousMode, signedKingId, storedRevision,
        heirId, pSelection.Mode, referenceKingId, dirtyRevision))
    return previousHeir;
ClearOldHeirFlag(pKingdom);
```

- [ ] **Step 3: 运行规则和源代码顺序守卫**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File Tests/KingHeirNoOpSourceGuard.ps1`

Expected: `King heir no-op source guard passed.`

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release`

Expected: `Rule tests passed.`

- [ ] **Step 4: 提交**

```powershell
git add Code/core/lineage/HeirService.cs Tests/KingHeirNoOpSourceGuard.ps1 Tests/AncientWarfare3.Rules.Tests/InheritanceLawRulesTests.cs.txt
git commit -m "perf: skip unchanged heir maintenance"
```

### Task 4: 建立活人谱系索引并消除继位 N+1 SQLite 查询

**Files:**
- Create: `Code/core/lineage/SuccessionRelationshipIndex.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/SuccessionRelationshipIndexRulesTests.cs.txt`
- Modify: `Code/core/lineage/HeirService.cs`
- Modify: `Code/core/lineage/InheritanceCandidateService.cs`
- Modify: `Code/patch/AW_BirthPatch.cs`
- Modify: `Code/patch/AW_ActorDeathPatch.cs`
- Modify: `Code/core/performance/AWAuthorityCycleService.cs`

- [ ] **Step 1: 写失败的纯索引测试**

覆盖注册、重复注册、死亡移除、父子迁移、姓/氏迁移和重建未完成时拒绝服务。

```csharp
var index = new SuccessionRelationshipIndexState();
index.BeginRebuild();
index.Upsert(new SuccessionActorFacts(10, 1, -1, 100, 200, true));
index.Upsert(new SuccessionActorFacts(11, 10, 2, 100, 201, true));
index.CompleteRebuild();
SequenceEqual(new long[] { 11 }, index.ChildrenOf(10));
SequenceEqual(new long[] { 10, 11 }, index.LineageMembers(100));
index.Remove(11);
Equal(0, index.ChildrenOf(10).Count);
```

- [ ] **Step 2: 实现主线程索引和有界重建**

```csharp
internal readonly struct SuccessionActorFacts
{
    internal SuccessionActorFacts(long pActorId, long pFatherId,
        long pMotherId, long pLineageId, long pShiId, bool pAlive)
    {
        ActorId = pActorId;
        FatherId = pFatherId;
        MotherId = pMotherId;
        LineageId = pLineageId;
        ShiId = pShiId;
        Alive = pAlive;
    }
    internal long ActorId { get; }
    internal long FatherId { get; }
    internal long MotherId { get; }
    internal long LineageId { get; }
    internal long ShiId { get; }
    internal bool Alive { get; }
}

internal sealed class SuccessionRelationshipIndexState
{
    private readonly Dictionary<long, SuccessionActorFacts> _facts = new();
    private readonly Dictionary<long, HashSet<long>> _children = new();
    private readonly Dictionary<long, HashSet<long>> _lineages = new();
    private readonly Dictionary<long, HashSet<long>> _shi = new();

    internal bool IsReady { get; private set; }
    internal void BeginRebuild() { Clear(); }
    internal void CompleteRebuild() { IsReady = true; }

    internal void Upsert(SuccessionActorFacts pFacts)
    {
        Remove(pFacts.ActorId);
        if (!pFacts.Alive || pFacts.ActorId < 0L) return;
        _facts[pFacts.ActorId] = pFacts;
        Add(_children, pFacts.FatherId, pFacts.ActorId);
        Add(_lineages, pFacts.LineageId, pFacts.ActorId);
        Add(_shi, pFacts.ShiId, pFacts.ActorId);
    }

    internal void Remove(long pActorId)
    {
        if (!_facts.TryGetValue(pActorId, out SuccessionActorFacts facts))
            return;
        _facts.Remove(pActorId);
        Remove(_children, facts.FatherId, pActorId);
        Remove(_lineages, facts.LineageId, pActorId);
        Remove(_shi, facts.ShiId, pActorId);
    }

    internal bool TryGetFather(long pActorId, out long pFatherId)
    {
        if (_facts.TryGetValue(pActorId, out SuccessionActorFacts facts))
        {
            pFatherId = facts.FatherId;
            return pFatherId >= 0L;
        }
        pFatherId = -1L;
        return false;
    }

    internal IReadOnlyList<long> ChildrenOf(long pActorId) =>
        Read(_children, pActorId);
    internal IReadOnlyList<long> LineageMembers(long pLineageId) =>
        Read(_lineages, pLineageId);
    internal IReadOnlyList<long> ShiMembers(long pShiId) =>
        Read(_shi, pShiId);

    internal void Clear()
    {
        _facts.Clear();
        _children.Clear();
        _lineages.Clear();
        _shi.Clear();
        IsReady = false;
    }

    private static void Add(Dictionary<long, HashSet<long>> pIndex,
        long pKey, long pActorId)
    {
        if (pKey < 0L) return;
        if (!pIndex.TryGetValue(pKey, out HashSet<long> ids))
        {
            ids = new HashSet<long>();
            pIndex.Add(pKey, ids);
        }
        ids.Add(pActorId);
    }

    private static void Remove(Dictionary<long, HashSet<long>> pIndex,
        long pKey, long pActorId)
    {
        if (pKey < 0L || !pIndex.TryGetValue(pKey,
                out HashSet<long> ids)) return;
        ids.Remove(pActorId);
        if (ids.Count == 0) pIndex.Remove(pKey);
    }

    private static IReadOnlyList<long> Read(
        Dictionary<long, HashSet<long>> pIndex, long pKey)
    {
        if (!pIndex.TryGetValue(pKey, out HashSet<long> ids))
            return Array.Empty<long>();
        var result = new long[ids.Count];
        ids.CopyTo(result);
        Array.Sort(result);
        return result;
    }
}
```

运行时静态适配层持有一个上述 state 和 `Actor[] + cursor`。`BeginRebuild` 调用 `manager.checkContainer()` / `prepareArray()` 后捕获数组与 count；`ProcessAuthorityCycle` 每次最多转换 128 个 Actor 为 `SuccessionActorFacts`，cursor 到末尾后调用 `CompleteRebuild`。出生、死亡和谱系身份改变直接调用同一个 `Upsert`/`Remove`，因此不产生第二套数据结构。

所有 WorldBox 对象读取发生在 `ProcessAuthorityCycle`、出生或死亡的主线程回调内。索引内部仅存 ID；不把对象传入 worker。

- [ ] **Step 3: 替换继位候选热路径查询**

`HeirService` 与 `InheritanceCandidateService` 的继位准备路径改用 `SuccessionRelationshipIndex`。索引未就绪时返回“准备未完成”，禁止回退 `LineageQuery` 或 `World.world.units` 全量扫描；非继位 UI/历史查询仍可继续使用原查询。

- [ ] **Step 4: 把增量维护接到现有生命周期**

出生父母确定后调用 `OnBorn`；`Actor.die` 尚可读取 data 时调用 `OnDying`；`AWAuthorityCycleService.Reset` 调用 `Reset`；`ProcessCycle` token gate 之后调用一次 `ProcessAuthorityCycle`。

- [ ] **Step 5: 运行 SQL 热路径守卫和规则测试**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File Tests/DeathSqlHotPathGuard.ps1`

Expected: `Death SQL hot-path guard passed.`

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release`

Expected: `Rule tests passed.`

- [ ] **Step 6: 提交**

```powershell
git add Code/core/lineage/SuccessionRelationshipIndex.cs Code/core/lineage/HeirService.cs Code/core/lineage/InheritanceCandidateService.cs Code/patch/AW_BirthPatch.cs Code/patch/AW_ActorDeathPatch.cs Code/core/performance/AWAuthorityCycleService.cs Tests/AncientWarfare3.Rules.Tests/SuccessionRelationshipIndexRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "perf: index live succession relationships"
```

### Task 5: 在稳定统治期预计算继承人与争议事实

**Files:**
- Create: `Code/core/lineage/SuccessionPreparationService.cs`
- Modify: `Code/core/performance/AWAuthorityCycleService.cs`
- Modify: `Code/core/court/CourtDirectionService.cs`
- Modify: `Code/core/lineage/InheritanceLawService.cs`
- Modify: `Code/patch/AW_BirthPatch.cs`
- Modify: `Code/patch/AW_ChroniclePatch.cs`
- Modify: `Code/core/lineage/HeirService.cs`
- Modify: `Code/core/lineage/SuccessionDisputeService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/KingSuccessionPreparationRulesTests.cs.txt`

- [ ] **Step 1: 写失败测试覆盖修订失效和每周期预算**

```csharp
var queue = new SuccessionDirtyQueue();
queue.MarkDirty(1); queue.MarkDirty(1); queue.MarkDirty(2);
Equal(2, queue.Count, "dirty kingdom ids are coalesced");
SequenceEqual(new long[] { 1 }, queue.Take(1),
    "one authority cycle processes one kingdom");
Equal(false, SuccessionSnapshotRules.IsCurrent(
    snapshotRevision: 5, currentRevision: 6,
    snapshotKingId: 10, currentKingId: 10,
    candidateAlive: true, candidateInRealm: true),
    "changed facts invalidate a cached snapshot");
```

- [ ] **Step 2: 实现脏队列、修订和不可变快照**

```csharp
internal sealed class SuccessionPreparationSnapshot
{
    internal long WorldGeneration;
    internal long KingdomId;
    internal long KingId;
    internal long Revision;
    internal long CandidateId;
    internal string Mode;
    internal long LegitimateClaimantId;
    internal long MilitaryClaimantId;
    internal long CivilClaimantId;
    internal long[] SupportCityIds = Array.Empty<long>();
}
```

`SuccessionPreparationService` 使用 `Dictionary<long,long>` 保存国家修订、`Queue<long> + HashSet<long>` 合并脏国家、`Dictionary<long,SuccessionPreparationSnapshot>` 保存快照、Task 2 的状态机保存死王上下文。其公开给运行时的精确契约是：`MarkDirty(Kingdom)` 只递增该国修订并入队；`ProcessAuthorityCycle(1)` 只出队一个国家且只在关系索引 ready 时建立快照；`CaptureDeath` 以 `AWAsyncRuntime.WorldGeneration/kingdom.id/king.data.id` 建键并复制当前快照 ID；`TryPublishForNativeSuccession` 同时验证世界代数、国家、死王、修订、候选存活和候选归属；`OnSuccessorInstalled` 消费上下文并让争议持久化；`Reset` 清空四个容器。任何验证失败都重新标脏并返回 `false`，不执行同步补算。

- [ ] **Step 3: 接入精确失效源**

继承法变化、出生/死亡、继承人变化、官员/将领变化、城市 leader 和归属变化只标记受影响国家。复用 `CourtDirectionService.MarkDirty` 的既有调用覆盖官员/将领变化，在其内部追加 `SuccessionPreparationService.MarkDirty(pKingdom)`；城市转移同时标记旧国和新国。

- [ ] **Step 4: 在唯一安全扩展点加入预算调用**

只修改 `AWAuthorityCycleService.ProcessCycle`，位置必须在：

```csharp
if (!pGate.TryEnter(pCycleToken, allowed)) return;
SuccessionRelationshipIndex.ProcessAuthorityCycle();
SuccessionPreparationService.ProcessAuthorityCycle(pKingdomBudget: 1);
```

不得修改本计划列出的六个受保护调度文件。

- [ ] **Step 5: 运行测试和调度哈希守卫**

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release`

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File Tests/CultiwayPerfSchedulerNonRegressionSourceGuard.ps1`

Expected: 两者通过。

- [ ] **Step 6: 提交**

```powershell
git add Code/core/lineage/SuccessionPreparationService.cs Code/core/performance/AWAuthorityCycleService.cs Code/core/court/CourtDirectionService.cs Code/core/lineage/InheritanceLawService.cs Code/patch/AW_BirthPatch.cs Code/patch/AW_ChroniclePatch.cs Code/core/lineage/HeirService.cs Code/core/lineage/SuccessionDisputeService.cs Tests/AncientWarfare3.Rules.Tests/KingSuccessionPreparationRulesTests.cs.txt
git commit -m "perf: precompute succession snapshots"
```

### Task 6: 让原版成为唯一继位控制器并删除重复准备

**Files:**
- Modify: `Code/patch/AW_ActorDeathPatch.cs`
- Modify: `Code/patch/AW_MandateSuccessionPatch.cs`
- Modify: `Code/patch/AW_HeirPatch.cs`
- Modify: `Code/core/lineage/HeirService.cs`
- Create: `Tests/KingDeathSuccessionPerformanceSourceGuard.ps1`
- Modify: `Tests/AncientWarfare3.Rules.Tests/MandateSuccessionRegressionRulesTests.cs.txt`

- [ ] **Step 1: 写失败源代码守卫**

守卫必须拒绝 `AW_ActorDeathPatch` 和 `AW_MandateSuccessionPatch` 中任何 `PrepareSuccessionBeforeKingDeath` 调用，拒绝死亡补丁中的 `SuccessionDisputeService.Prepare`、`SQLiteCommand`、`BeginTransaction`、`LineageQuery` 和 `World.world.units`。

- [ ] **Step 2: 把死亡前缀改成常数时间捕获**

```csharp
if (dyingKing)
{
    DyingKingActorId = __instance.data.id;
    TryRunDeathStage(__instance,
        ActorDeathPerformanceStage.KingHeirPreparation,
        "king succession capture", () =>
        SuccessionPreparationService.CaptureDeath(
            dyingKingdom, __instance));
    ChronicleEvents.OnKingDied(dyingKingdom, __instance);
}
```

- [ ] **Step 3: 让原版王国行为只消费一次候选**

```csharp
if (!UsesManagedLineage(pKingdom) || !pKingdom.hasKing()) return true;
Actor king = pKingdom.king;
if (king?.data == null || king.isAlive()) return true;
if (!SuccessionPreparationService.TryPublishForNativeSuccession(
        pKingdom, king))
{
    __result = BehResult.Continue;
    return false;
}
if (!AW3MultiplayerSuccessionFacade.TryDefer(pKingdom, king)) return true;
__result = BehResult.Continue;
return false;
```

准备好时返回 `true`，让原版继续执行 `clearKingData -> SuccessionTool -> move to capital -> Kingdom.setKing`。未准备好时只延迟本次行为，不自行安装国王。

- [ ] **Step 4: `SuccessionTool` 只读已发布候选**

`AW_HeirPatch.GetKingFromRoyalClan_Prefix` 不再触发 `GetHeir` 重算；改用 `SuccessionPreparationService.TryGetPublishedCandidate`，并在 `SetKing_Postfix` 成功后调用 `OnSuccessorInstalled`。

- [ ] **Step 5: 运行继位守卫与测试**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File Tests/KingDeathSuccessionPerformanceSourceGuard.ps1`

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File Tests/MandateSuccessionRuntimeSourceGuard.ps1`

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release`

Expected: 所有命令通过。

- [ ] **Step 6: 提交**

```powershell
git add Code/patch/AW_ActorDeathPatch.cs Code/patch/AW_MandateSuccessionPatch.cs Code/patch/AW_HeirPatch.cs Code/core/lineage/HeirService.cs Tests/KingDeathSuccessionPerformanceSourceGuard.ps1 Tests/AncientWarfare3.Rules.Tests/MandateSuccessionRegressionRulesTests.cs.txt
git commit -m "fix: make native succession the sole installer"
```

### Task 7: 将继承争议写入移出死亡帧

**Files:**
- Create: `Code/core/lineage/SuccessionDisputePersistence.cs`
- Modify: `Code/core/lineage/SuccessionDisputeService.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/SuccessionDisputePersistenceSqlTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: 写失败的 SQLite 原子性测试**

使用内存 SQLite 建表，验证 envelope 在一个事务内生成 `DISPUTE_ID`、插入一条 `SuccessionDispute` 和 N 条 `SuccessionDisputeCity`；第二次相同 operation key 不产生第二个有效争议；异常时两张表均为零新增。

- [ ] **Step 2: 实现仅含标量的自定义 envelope**

```csharp
internal sealed class SuccessionDisputeWriteEnvelope :
    HistoricalWriteEnvelope, IHistoricalCustomWriteEnvelope
{
    private readonly SuccessionDisputeWriteFacts _facts;
    internal SuccessionDisputeWriteEnvelope(long pSequence,
        string pOperationKey, AWAsyncStamp pStamp,
        SuccessionDisputeWriteFacts pFacts)
        : base(pSequence, pOperationKey, string.Empty,
            Array.Empty<HistoricalSqlValue>(), HistoricalWriteKind.Append,
            pStamp, "succession-dispute")
    { _facts = pFacts; }

    public object Execute(SQLiteConnection pConnection,
        SQLiteTransaction pTransaction)
    {
        return SuccessionDisputePersistence.Execute(
            pConnection, pTransaction, _facts);
    }
}
```

`SuccessionDisputePersistence.Execute` 在传入事务上执行三步：`SELECT COALESCE(MAX(DISPUTE_ID),0)+1 FROM SuccessionDispute`；使用现有 `Prepare` 的完整 25 列参数化 INSERT 插入争议；一次读取下一个 `ENTRY_ID` 后按 `facts.SupportCityIds` 顺序插入城市行。任一 `ExecuteNonQuery()!=1` 抛出 `InvalidOperationException`，由 `HistoricalSqliteBatchSink` 回滚整批。返回的 `SuccessionDisputeWriteResult` 只含 `DisputeId` 和复制后的 `long[] SupportCityIds`。

- [ ] **Step 3: 由稳定期快照提交，主线程回调按版本发布**

```csharp
bool accepted = HistoricalWriteService.TryEnqueueCustom(
    operationKey,
    (sequence, stamp) => new SuccessionDisputeWriteEnvelope(
        sequence, operationKey, stamp, facts),
    pOnCommitted: (sequence, outcome) =>
        SuccessionPreparationService.AcceptDisputeCommit(
            facts, outcome),
    pOnFailed: (sequence, error) =>
        SuccessionPreparationService.MarkDisputePersistencePending(facts));
```

回调必须核对 `AWAsyncRuntime.WorldGeneration`、国家、前王、后王和修订。队列暂不可用时保留内存 pending，在既有 save barrier flush；活跃游戏中不得同步回退。

- [ ] **Step 4: 删除 `SuccessionDisputeService.Prepare` 的同步事务**

候选和城市支持事实从 `SuccessionPreparationSnapshot` 获取，运行时争议先发布内存状态；数据库只负责持久化，不反向阻塞原版继位。

- [ ] **Step 5: 运行测试并提交**

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release`

Expected: `Rule tests passed.`

```powershell
git add Code/core/lineage/SuccessionDisputePersistence.cs Code/core/lineage/SuccessionDisputeService.cs Tests/AncientWarfare3.Rules.Tests/SuccessionDisputePersistenceSqlTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "perf: persist succession disputes asynchronously"
```

### Task 8: 将科举死王处理改成 O(1) 内存命中和异步 CAS

**Files:**
- Create: `Code/core/court/CivilServiceRulerDeathPersistence.cs`
- Modify: `Code/core/court/CivilServiceExamService.cs`
- Modify: `Code/core/db/LineageArchiveIndexRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/CivilServiceRulerDeathPersistenceSqlTests.cs.txt`
- Modify: `Tests/CivilServiceExamRuntimeSourceGuard.ps1`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: 写失败测试覆盖无会试和有会试两条路径**

验证没有 `PlayerRankingPending` 会话的国家直接返回且不创建 envelope；命中时更新内存 due set，并提交带旧状态条件的 CAS；重复死王通知只接受一次。

- [ ] **Step 2: 维护国家到待排名会话的运行时映射**

```csharp
private static readonly Dictionary<long, CivilServiceExamSessionRecord>
    PlayerRankingByKingdom = new();

private static void IndexSession(CivilServiceExamSessionRecord pSession)
{
    if (pSession?.PlayerRankingPending == true &&
        string.Equals(pSession.Mode, "imperial_exam",
            StringComparison.Ordinal) &&
        string.Equals(pSession.Stage, "ranking",
            StringComparison.Ordinal) &&
        string.Equals(pSession.Status, "ranking_pending",
            StringComparison.Ordinal))
        PlayerRankingByKingdom[pSession.KingdomId] = pSession;
    else if (pSession != null)
        PlayerRankingByKingdom.Remove(pSession.KingdomId);
}
```

`RebuildRuntime`、`Enqueue`、提交排名、取消会话和 stage transition 都同步维护此映射。

- [ ] **Step 3: 将死王函数改为无 SQL 快路径**

```csharp
public static void OnCurrentRulerDied(Kingdom pKingdom)
{
    if (AW3MultiplayerReplicaScope.IsApplying ||
        AW3MultiplayerReplicaScope.IsReplicaSession ||
        pKingdom?.data == null || pKingdom.id < 0L ||
        !PlayerRankingByKingdom.TryGetValue(pKingdom.id, out var session))
        return;
    long dueDay = CurrentWorldDay();
    DueSessions.Remove(new DueSession(session.NextDueWorldDay, session.Id));
    session.NextDueWorldDay = dueDay;
    session.PlayerRankingPending = false;
    DueSessions.Add(new DueSession(dueDay, session.Id));
    CivilServiceRulerDeathPersistence.TryEnqueue(session, dueDay);
}
```

- [ ] **Step 4: 实现 worker 侧 compare-and-set 与部分索引**

SQL 条件必须包含 `ID=@id AND KINGDOM_ID=@kingdom AND MODE='imperial_exam' AND STAGE='ranking' AND STATUS='ranking_pending' AND PLAYER_RANKING_PENDING=1`。增加：

```sql
CREATE INDEX IF NOT EXISTS idx_CivilServiceExamSession_player_ruler_death
ON CivilServiceExamSession (KINGDOM_ID, ID)
WHERE MODE='imperial_exam' AND STAGE='ranking'
  AND STATUS='ranking_pending' AND PLAYER_RANKING_PENDING=1
```

- [ ] **Step 5: 运行科举守卫、SQL 测试和全量规则测试**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File Tests/CivilServiceExamRuntimeSourceGuard.ps1`

Expected: `Civil-service ruler-death source guard passed.`

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release`

Expected: `Rule tests passed.`

- [ ] **Step 6: 提交**

```powershell
git add Code/core/court/CivilServiceRulerDeathPersistence.cs Code/core/court/CivilServiceExamService.cs Code/core/db/LineageArchiveIndexRules.cs Tests/AncientWarfare3.Rules.Tests/CivilServiceRulerDeathPersistenceSqlTests.cs.txt Tests/CivilServiceExamRuntimeSourceGuard.ps1 Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "perf: remove civil service sql from ruler death"
```

### Task 9: 补齐 world lifecycle、存档与多人边界

**Files:**
- Modify: `Code/core/performance/AWAuthorityCycleService.cs`
- Modify: `Code/core/asyncwork/AWAsyncWorldLifecycle.cs`
- Modify: `Code/patch/AW_ActorDeathPatch.cs`
- Modify: `Tests/KingDeathSuccessionPerformanceSourceGuard.ps1`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AW3MultiplayerSuccessionRulesTests.cs.txt`

- [ ] **Step 1: 写失败测试覆盖世界代数和 replica**

测试旧世界写入完成不能发布到新世界；replica 不建候选、不写争议、不写科举；clear-world 后 dirty queue、索引、死王上下文和 pending persistence 全为空。

- [ ] **Step 2: 将清理集中到现有生命周期**

```csharp
public static void Reset()
{
    CooperativeGate.Reset();
    NativeGate.Reset();
    _nativeCycleToken = 0L;
    SuccessionPreparationService.Reset();
    SuccessionRelationshipIndex.Reset();
    CivilServiceExamService.ClearRuntime();
    // retain existing reset calls unchanged
}
```

不得在 `MapBox.clearWorld` 新增另一个调度器 abort/drain 顺序；只复用 `AWAuthorityCycleService.Reset` 已有调用点。

- [ ] **Step 3: 保存边界仅 flush 已接纳历史写入**

复用 `HistoricalWriteService` 现有 paused save barrier。继位服务不直接调用 `SaveManager`、不自建线程、不等待数据库；保存后的 reload 通过 `RebuildRuntime` 恢复争议和科举索引。

- [ ] **Step 4: 运行多人、存档和调度守卫**

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release`

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File Tests/CultiwayPerfSchedulerNonRegressionSourceGuard.ps1`

Expected: 两者通过。

- [ ] **Step 5: 提交**

```powershell
git add Code/core/performance/AWAuthorityCycleService.cs Code/core/asyncwork/AWAsyncWorldLifecycle.cs Code/patch/AW_ActorDeathPatch.cs Tests/KingDeathSuccessionPerformanceSourceGuard.ps1 Tests/AncientWarfare3.Rules.Tests/AW3MultiplayerSuccessionRulesTests.cs.txt
git commit -m "fix: reset succession caches at world boundaries"
```

### Task 10: 静态对照 Cultiway perf 并进行 Native/Large 游戏内验收

**Files:**
- Modify: `docs/superpowers/specs/2026-08-09-king-death-succession-performance-design.md`
- Create: `docs/superpowers/reports/2026-08-09-king-death-succession-performance.md`

- [ ] **Step 1: 执行完整静态验证**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/KingDeathSuccessionPerformanceSourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/DeathSqlHotPathGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/MandateSuccessionRuntimeSourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/CivilServiceExamRuntimeSourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/CultiwayPerfSchedulerNonRegressionSourceGuard.ps1
git diff --check
```

Expected: 全部退出码 0，无 whitespace error。

- [ ] **Step 2: 对照 Cultiway perf 生命周期**

逐项记录 AW3 未改动的对应边界：

```text
Cultiway Source/Patch/PatchFramePriorityScheduler.cs
  MapBox.Update begin/end, save drain, clear abort, world creation reset
Cultiway Source/Core/Performance/CooperativeSimulationRunner.cs
  one active cycle, presentation finish, drain, abort, step context
Cultiway Source/Core/Performance/CooperativeBatchRunner.cs
  cursor ownership and abort
Cultiway Source/Core/Performance/CooperativeActorParallelJobRunner.cs
  worker/main-thread object boundary
Cultiway Source/Core/Performance/SimulationStepContext.cs
  elapsed/time-scale restoration
```

报告必须附六个受保护 AW3 文件的最终 SHA-256，且与计划顶部完全一致。

- [ ] **Step 3: Native 模式游戏内矩阵**

在同一大型存档依次测试：合法登记继承人、登记继承人提前死亡、旁支继承、军功/文官推戴、共和领袖死亡、继承争议、多城与单城、无合法继承人、继承人为禁卫军/将领/官员/外国归宗者、死王后立即保存读取。

记录：继位者 ID、继承模式、争议 ID、科举会话 ID、死王帧各诊断阶段、后续 120 帧最大 AW3 时间。

- [ ] **Step 4: Large 模式复跑相同矩阵**

使用同一确定性存档和相同操作顺序。Native/Large 的继位者、争议、科举、编年史、天命和多人权威结果必须一致；Large 模式不得出现额外 authority cycle、暂停、动画或保存边界回归。

- [ ] **Step 5: 性能验收**

```text
Actor.die 死王 AW3 目标 <= 2 ms，硬上限 5 ms
死亡帧 king_heir_prepare / king_civil_service / AW3 DB 阶段均 <= 5 ms
死亡后 120 帧内无 AW3 阶段 > 16.7 ms
Actor.die、KingdomBehCheckKing prefix、Kingdom.setKing hook 同步 SQLite = 0
有效缓存继位候选扫描 = 0，全世界 Actor/Kingdom 扫描 = 0
空 dirty queue 的继位 authority 工作近似 0 分配并立即返回
```

- [ ] **Step 6: 写报告并提交**

报告列出每个根因的修改前/修改后证据，不接受只写“体感改善”。

```powershell
git add docs/superpowers/specs/2026-08-09-king-death-succession-performance-design.md docs/superpowers/reports/2026-08-09-king-death-succession-performance.md
git commit -m "docs: verify king succession performance fix"
```

## Final Acceptance Gate

实施完成前必须同时满足：五个根因均有失败测试和通过证据；原版仍是唯一 `Kingdom.setKing` 继位安装路径；死亡帧没有同步 SQL 或全量扫描；六个 Cultiway perf 调度移植文件哈希未变化；Native/Large 行为一致；大型存档性能达到上述硬指标。

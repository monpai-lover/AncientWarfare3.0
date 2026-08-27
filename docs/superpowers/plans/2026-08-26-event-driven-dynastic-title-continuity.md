# Event-Driven Dynastic Title Continuity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the annual global title-holder traversal while preserving title inheritance and old-save continuity through targeted runtime events.

**Architecture:** Keep the existing coalesced `DirtyHolders` authority queue and synchronous death-succession transaction. Extend actor-load reconciliation so a loaded male child refreshes only its loaded hereditary parents, then remove the global holder loop from the kingdom annual callback.

**Tech Stack:** C# 11/.NET Framework 4.8 mod project, Harmony runtime hooks, PowerShell source guards, .NET 9 focused rules tests.

---

## File Map

- Modify `Tests/DynasticMaleLineContinuitySourceGuard.ps1`: isolate the relevant method bodies and assert the event-driven invariants.
- Modify `Code/core/lineage/DynasticMaleLineContinuityService.cs`: add parent refresh on actor load and remove the annual global traversal.

### Task 1: Replace Annual Global Refresh With Load Events

**Files:**
- Modify: `Tests/DynasticMaleLineContinuitySourceGuard.ps1`
- Modify: `Code/core/lineage/DynasticMaleLineContinuityService.cs`

- [ ] **Step 1: Add failing source guards for the desired behavior**

Add a method-body extractor after `Read-Source`:

```powershell
function Read-MethodBody([string]$path, [string]$start,
    [string]$next) {
    $source = Read-Source $path
    $startIndex = $source.IndexOf($start, [StringComparison]::Ordinal)
    if ($startIndex -lt 0) { throw "Missing method start: $start" }
    $nextIndex = $source.IndexOf($next, $startIndex,
        [StringComparison]::Ordinal)
    if ($nextIndex -lt 0) { throw "Missing next method: $next" }
    return $source.Substring($startIndex, $nextIndex - $startIndex)
}
```

After `$service` is assigned, add guards scoped to the two methods:

```powershell
$annual = Read-MethodBody $service `
    'public static void OnKingdomYear' `
    'public static void OnTitleProjectionChanged'
if ($annual.Contains('ActiveMaleTitleHolders')) {
    throw 'Annual kingdom work must not traverse the global title-holder index.'
}

$load = Read-MethodBody $service `
    'public static void OnActorLoaded' `
    'public static void ProcessAuthorityCycle'
if (-not $load.Contains('QueueParentHolder(pActor.data.parent_id_1)')) {
    throw 'Loading a male child must refresh its first loaded hereditary parent.'
}
if (-not $load.Contains('QueueParentHolder(pActor.data.parent_id_2)')) {
    throw 'Loading a male child must refresh its second loaded hereditary parent.'
}
```

- [ ] **Step 2: Run the guard and verify that it fails before implementation**

Run:

```powershell
pwsh -NoProfile -File Tests/DynasticMaleLineContinuitySourceGuard.ps1
```

Expected: failure stating that annual work still references `ActiveMaleTitleHolders` or that actor loading does not refresh a parent.

- [ ] **Step 3: Implement targeted load reconciliation and remove the annual loop**

Change `OnKingdomYear` so it retains only bounded kingdom-local roles:

```csharp
public static void OnKingdomYear(Kingdom pKingdom)
{
    if (pKingdom?.data == null || pKingdom.isRekt()) return;
    RequestContinuation(pKingdom.king);
    RequestContinuation(HeirService.GetHeir(pKingdom));
    try
    {
        foreach (FeudatorySnapshot snapshot in
                 FeudatoryService.GetByKingdom(pKingdom.id))
            RequestContinuation(ResolveActor(
                snapshot?.PrinceActorId ?? -1L));
    }
    catch { }
}
```

Change `OnActorLoaded` so either load order repairs the holder:

```csharp
public static void OnActorLoaded(Actor pActor)
{
    if (pActor?.data == null) return;
    if (IsHereditaryHolder(pActor) && SafeIsAlive(pActor))
    {
        ActiveMaleTitleHolders.Add(pActor.data.id);
        EnqueueHolder(pActor.data.id);
    }
    if (!pActor.isSexMale()) return;
    QueueParentHolder(pActor.data.parent_id_1);
    if (pActor.data.parent_id_2 != pActor.data.parent_id_1)
        QueueParentHolder(pActor.data.parent_id_2);
}
```

Do not alter `NobleRankService.OnActorDying`, the succession retry queue, `DirtyHolders`, `EnqueuedHolders`, or `MaxHolderRefreshesPerCycle`.

- [ ] **Step 4: Run focused guards and rules tests**

Run:

```powershell
pwsh -NoProfile -File Tests/DynasticMaleLineContinuitySourceGuard.ps1
dotnet run --project Tests/DynasticMaleLineContinuityRulesTests.csproj
```

Expected:

```text
Dynastic male-line continuity source guard passed.
Dynastic male-line continuity rule tests passed.
```

- [ ] **Step 5: Build the complete mod project**

Run:

```powershell
dotnet build AncientWarfare3.csproj -c Debug --no-restore
```

Expected: build succeeds with zero errors. Existing unrelated warnings, if any, must be reported rather than silently treated as this change.

- [ ] **Step 6: Review the exact diff and commit only this fix**

Run:

```powershell
git diff --check -- Code/core/lineage/DynasticMaleLineContinuityService.cs Tests/DynasticMaleLineContinuitySourceGuard.ps1
git diff -- Code/core/lineage/DynasticMaleLineContinuityService.cs Tests/DynasticMaleLineContinuitySourceGuard.ps1
git add -- Code/core/lineage/DynasticMaleLineContinuityService.cs Tests/DynasticMaleLineContinuitySourceGuard.ps1
git commit -m "perf: make title continuity event driven"
```

Expected: one commit containing only the service change and its source guard. Do not push or deploy unless separately requested.

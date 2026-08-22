# 战时优先调度 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在大步长模式中将现有 RTS 军事 P0 提前为每帧有预算上限的固定步长通道，并在战争积压时动态降低渲染目标，从而提高世界吞吐且不破坏移动、攻城、运输和返乡。

**Architecture:** 新增纯规则层负责动态 FPS 档位、固定步长欠账和公平轮转；新增运行时 `AWMilitaryFrontLaneScheduler` 复用现有 `AWCooperativeActorPostRunner` 的单 actor P0 执行链。`AWFramePriorityGovernor` 只读取有效动态目标，战略规划继续留在 `ArmyRtsSchedulingService.ProcessLogicalPass`，actor post 通过渲染帧令牌避免同帧重复推进。

**Tech Stack:** C# 11 / .NET Framework 4.8、Harmony、Unity/WorldBox API、net9.0 纯规则测试、PowerShell 源码守卫。

---

### Task 1: 动态战时 FPS 规则

**Files:**
- Create: `Code/core/performance/AWWartimeFrameBudgetRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/WartimeFrameBudgetRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write the failing tests**

测试覆盖：和平始终返回配置 FPS；战时积分低于 60% 保持配置值；连续 30 帧超过 60% 降到 40 FPS；连续 30 帧超过 90% 降到 35 FPS；恢复必须连续 90 帧低于对应退出阈值；配置目标低于档位时不得反向提高 FPS。

```csharp
var state = new AWWartimeFrameBudgetState();
for (int i = 0; i < 30; i++)
    state = AWWartimeFrameBudgetRules.Advance(
        state, true, 49d, 50d, 50f);
Equal(35f, state.EffectiveTargetFps,
    "sustained severe wartime backlog selects 35 FPS");
```

- [ ] **Step 2: Run the rules project and verify failure**

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

Expected: FAIL because `AWWartimeFrameBudgetRules` does not exist.

- [ ] **Step 3: Implement the pure state machine**

定义 `AWWartimeFrameBudgetTier.Configured/Moderate/Severe` 和不可变状态。进入阈值使用 `0.60/0.90`，退出阈值使用 `0.45/0.75`；进入稳定窗口 30 帧，恢复窗口 90 帧。有效目标分别为 `configured`、`min(configured, 40)`、`min(configured, 35)`。

- [ ] **Step 4: Run rules tests**

Expected: all rules tests pass.

- [ ] **Step 5: Commit**

```powershell
git add -- Code/core/performance/AWWartimeFrameBudgetRules.cs Tests/AncientWarfare3.Rules.Tests/WartimeFrameBudgetRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "perf: add wartime frame budget rules"
```

### Task 2: 军事帧前固定步长和公平轮转规则

**Files:**
- Create: `Code/core/performance/AWMilitaryFrontLaneRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/MilitaryFrontLaneRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write failing tests**

测试 `0.02s` 固定步长、最大欠账 `0.4s`、空队列清空欠账、游标从上次停止处继续、完整扫过快照才消费一个固定步长，以及预算不足时不会跳过尾部 actor。

```csharp
Equal(0.4d, AWMilitaryFrontLaneRules.AddDebt(
    0.39d, 0.02d, 20d), 0.0001d,
    "military debt is capped to prevent a catch-up spiral");
Equal(2, AWMilitaryFrontLaneRules.NormalizeCursor(5, 3),
    "cursor wraps fairly across the stable snapshot");
```

- [ ] **Step 2: Run and verify failure**

Expected: FAIL because the rule type does not exist.

- [ ] **Step 3: Implement rules**

提供 `FixedStepSeconds = 0.02d`、`MaximumDebtSeconds = 0.4d`、`AddDebt`、`HasStepDue`、`ConsumeCompletedSweep`、`NormalizeCursor` 和 `ResolveMaximumActors`。规则层不引用 Unity 或 WorldBox 类型。

- [ ] **Step 4: Run rules tests and commit**

```powershell
git add -- Code/core/performance/AWMilitaryFrontLaneRules.cs Tests/AncientWarfare3.Rules.Tests/MilitaryFrontLaneRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "perf: define military front lane cadence"
```

### Task 3: 将现有军事 P0 提取为共享执行入口

**Files:**
- Modify: `Code/core/performance/AWCooperativeActorPostRunner.cs`
- Modify: `Code/core/performance/ArmyMilitaryMovementPriorityIndex.cs`
- Create: `Tests/WartimeMilitaryFrontLaneSourceGuard.ps1`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] **Step 1: Write a failing source guard**

守卫要求：`ProcessMilitaryP0Actor` 为 `internal static`；帧前和 post 只能调用这一入口；索引使用渲染帧令牌而不是由 actor 周期清空；禁止新增第二份 `b2/b3/b4/b5/b6/u10` 调用链。

- [ ] **Step 2: Run the guard and verify failure**

Run: `powershell -ExecutionPolicy Bypass -File Tests/WartimeMilitaryFrontLaneSourceGuard.ps1`

Expected: FAIL because no front-lane shared entry exists.

- [ ] **Step 3: Make P0 execution reusable**

将 `ProcessMilitaryP0Actor(long,float)` 改为 `internal static`。它继续使用现有运输、自登陆、战斗、返乡和原版 AI 调用链，不改变调用顺序。

将 `ArmyMilitaryMovementPriorityIndex` 的 `ProcessedThisCycle` 改为 `Dictionary<long,int> ProcessedFrameByActor`，增加：

```csharp
internal static void BeginFrame(int frameId)
internal static bool WasProcessed(long actorId, int frameId)
internal static void MarkProcessed(long actorId, int frameId)
internal static int Count { get; }
```

保留无参数包装器读取当前帧，以兼容已有调用点。`AWCooperativeActorPostRunner.Start` 不再清除已由帧前通道写入的同帧令牌。

- [ ] **Step 4: Run source guard and existing RTS rules tests**

Expected: PASS, and existing RTS task ownership tests remain green.

- [ ] **Step 5: Commit**

```powershell
git add -- Code/core/performance/AWCooperativeActorPostRunner.cs Code/core/performance/ArmyMilitaryMovementPriorityIndex.cs Tests/WartimeMilitaryFrontLaneSourceGuard.ps1 Tests/SourceGuardTests.ps1
git commit -m "refactor: share military p0 execution"
```

### Task 4: 实现并接入帧前军事调度器

**Files:**
- Create: `Code/core/performance/AWMilitaryFrontLaneScheduler.cs`
- Modify: `Code/patch/AW_FramePrioritySchedulerPatch.cs`
- Modify: `Code/core/performance/AWCooperativeSimulationRunner.cs`
- Modify: `Code/ModClass.cs`
- Modify: `Tests/WartimeMilitaryFrontLaneSourceGuard.ps1`

- [ ] **Step 1: Extend the failing source guard**

要求 `MapBox.Update` 帧首完成读边界和延迟路径 flush 后调用 `AWMilitaryFrontLaneScheduler.ProcessFrame`；加载、暂停、多人副本、非大步长和无军事队列时不执行；reset/clearWorld 时清理欠账和游标。

- [ ] **Step 2: Implement scheduler state**

维护稳定 actor ID 快照、游标、固定步长欠账、当前 sweep 剩余数、累计处理数和最大等待帧。每帧：

```csharp
ArmyMilitaryMovementPriorityIndex.BeginFrame(Time.frameCount);
debt = AWMilitaryFrontLaneRules.AddDebt(
    debt, Time.unscaledDeltaTime, runner.RequestedSpeed);
```

只要有欠账且未超过 `2.5ms`，从游标继续调用共享 `ProcessMilitaryP0Actor(actorId, 0.02f)`。完整扫过快照后扣除一个步长并重新快照；预算耗尽则保留游标。

- [ ] **Step 3: Add runtime safety boundaries**

在 `EnsureActorReadBoundary` 和 `AWDeferredPathRequestBatch.FlushAtFrameStart` 之后运行，确保不会与后台表现提交并发，也不会抢先消费尚未正式提交的路径结果。异常只清理帧前调度状态并写 `LogError`，不能暂停整个游戏或释放正式 RTS controller。

- [ ] **Step 4: Run source guard and build**

Run: `powershell -ExecutionPolicy Bypass -File Tests/WartimeMilitaryFrontLaneSourceGuard.ps1`

Run: `dotnet build AncientWarfare3.csproj`

Expected: PASS and build succeeds.

- [ ] **Step 5: Commit**

```powershell
git add -- Code/core/performance/AWMilitaryFrontLaneScheduler.cs Code/patch/AW_FramePrioritySchedulerPatch.cs Code/core/performance/AWCooperativeSimulationRunner.cs Code/ModClass.cs Tests/WartimeMilitaryFrontLaneSourceGuard.ps1
git commit -m "perf: run military movement before actor simulation"
```

### Task 5: 接入动态 FPS 和诊断

**Files:**
- Modify: `Code/core/performance/AWFramePriorityGovernor.cs`
- Modify: `Code/core/performance/AWMilitaryFrontLaneScheduler.cs`
- Modify: `Code/core/performance/AWCooperativeSimulationRunner.cs`
- Modify: `Tests/WartimeMilitaryFrontLaneSourceGuard.ps1`

- [ ] **Step 1: Add failing guard assertions**

要求 governor 的 `CanRun`、剩余预算和 `RecalculateBudget` 使用同一个 `EffectiveTargetRenderFps`；禁止修改 `AWPerformanceSettings.TargetRenderFps`；诊断包含 pending、processed、耗时、最大延迟、固定步编号、动态 FPS 和原因。

- [ ] **Step 2: Implement effective target selection**

`AWFramePriorityGovernor.BeginFrame` 根据：大步长模式、帧前调度器存在有效军事工作、`AdmissionCredits / 50` 积压比例，推进 `AWWartimeFrameBudgetState`。所有截止时间计算改用只读 `EffectiveTargetRenderFps`。

- [ ] **Step 3: Extend diagnostics**

`GetDiagnostics()` 追加类似：

```text
military_front=42/180@2.31ms delay=1 step=312 dynamic_fps=35(severe_backlog)
```

没有战争时显示 configured 档位和零军事开销。

- [ ] **Step 4: Run rules, guard, build and commit**

```powershell
git add -- Code/core/performance/AWFramePriorityGovernor.cs Code/core/performance/AWMilitaryFrontLaneScheduler.cs Code/core/performance/AWCooperativeSimulationRunner.cs Tests/WartimeMilitaryFrontLaneSourceGuard.ps1
git commit -m "perf: adapt render budget to wartime backlog"
```

### Task 6: 全量验证和本地部署

**Files:**
- Verify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Verify: `Tests/SourceGuardTests.ps1`
- Verify: `AncientWarfare3.csproj`
- Deploy: `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`

- [ ] **Step 1: Run focused tests**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
powershell -ExecutionPolicy Bypass -File Tests/WartimeMilitaryFrontLaneSourceGuard.ps1
powershell -ExecutionPolicy Bypass -File Tests/ArmyRtsCombatRecoverySourceGuard.ps1
powershell -ExecutionPolicy Bypass -File Tests/ArmyRtsIndependentPathSourceGuard.ps1
```

- [ ] **Step 2: Run unified source guards and build**

```powershell
powershell -ExecutionPolicy Bypass -File Tests/SourceGuardTests.ps1
dotnet build AncientWarfare3.csproj
git diff --check
```

- [ ] **Step 3: Deploy source and DLL**

Run the repository deployment script used by the current master branch. Do not overwrite runtime config or saves.

- [ ] **Step 4: Inspect runtime log**

启动 WorldBox，加载约 4000 人口存档并在 20x 观察至少一次同岛战争和一次运输/返乡。日志必须显示动态 FPS 档位、军事帧前处理持续发生、无重复路径消费异常，并且和平后恢复配置 FPS。

- [ ] **Step 5: Final commit and push only requested files**

确认工作区中原有 `Code/core/lineage/SuccessionDisputeService.cs` 修改仍未被暂存。提交遗漏的测试或诊断文件并推送 master。

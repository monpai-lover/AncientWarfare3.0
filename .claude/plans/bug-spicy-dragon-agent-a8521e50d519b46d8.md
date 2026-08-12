# 泛化战斗脱离：野战交火时释放 RTS 控制

## 目标
大步长模式下军队"卡住不动"的根因是**战斗行为控制**（RTS 行军任务与原版战斗 AI 拉锯），而非调度脱离。
用户确认方向：**两军在任意位置交火即把 actor 交给原版战斗 AI，战斗打完再收回 RTS 控制**——把现有"仅攻城目标城内"的脱离逻辑泛化到野战。

## 已核实的关键事实（不改这些）
- 大步长下没有独立 native 时钟：`AW_FramePrioritySchedulerPatch` 接管 `MapBox.checkMainSimulationUpdate`（返回 false），
  `_world.armies.update()` 跑在协作循环里（`AWCooperativeSimulationRunner.cs:1273`）。切换 RTS 调度 owner（Native/Aw3）
  只是选同一循环里的哪个 stage 跑 RTS tick，**都被同一节流限制**，无法脱离大步长。因此不做调度脱离。
- 已存在两级脱离机制：
  - actor 级（通用）：`HasImmediateCombatPriority`（16 tile 内有敌）→ `ShouldReassertMissionTask` 返回 false
    → 不再重置行军任务（`ArmyRtsTaskOwnershipRules.cs:54`）。
  - army 级（窄）：`ReleaseToVanillaCombat`/`ReacquireFromVanillaCombat`，仅在"目标城内 + 城内有敌 + 目标未完成"触发
    （`ArmyRtsControllerService.cs:2943-2969`）。**野战两军相撞不触发**——这是卡住的直接原因。
- actor 级脱离不足以解决：单个交战单位虽不再被重置行军任务，但 army 的行军路线/编队仍在推进，
  队长继续行军、未交战成员继续跟随，把队伍拖离战场，且目标一死立刻重下行军任务 → 拉锯。

## 方案：army 级野战脱离（transient，不污染持久 phase）

### 1. 新增判定规则（纯函数，可单测）
文件：`Code/core/lineage/ArmyRtsTaskOwnershipRules.cs`（或新建 `ArmyRtsFieldCombatRules.cs`，倾向后者保持职责单一）
```csharp
public static class ArmyRtsFieldCombatRules
{
    // 交战单位占活跃战斗员比例达到此值即释放
    public const int EngageReleasePercent = 25;
    // 降到此值以下才收回，避免抖动（滞后）
    public const int DisengageResumePercent = 5;

    public static bool ShouldReleaseToFieldCombat(
        bool pAlreadyReleased, int pEngagedCombatants, int pLiveCombatants,
        bool pCaptainEngaged)
    {
        if (pLiveCombatants <= 0) return pCaptainEngaged;
        int pct = (int)((long)pEngagedCombatants * 100L / pLiveCombatants);
        return pAlreadyReleased
            ? pct > DisengageResumePercent || pCaptainEngaged
            : pct >= EngageReleasePercent || pCaptainEngaged;
    }
}
```
（滞后：已释放时用低阈值维持，未释放时用高阈值触发，防止边界反复切换。）

### 2. RuntimeState 增加 transient 字段
文件：`ArmyRtsControllerService.cs:523` `RuntimeState`
```csharp
internal bool FieldCombatReleased;
```
不持久化（RuntimeByArmy 内存态），存档不受影响。

### 3. 在 TryHandleWarCombatOwnership 之前插入野战脱离门
文件：`ArmyRtsControllerService.cs`，`ProcessOne`（约 2696 行 `if (commit && TryHandleWarCombatOwnership(...))` 之前）

逻辑顺序（关键：城内攻城 phase 逻辑优先，野战门只在 army 未被城内逻辑接管时生效）：
1. 先算 army 是否处于既有 VanillaCombat/Withdrawal/Replenishing 等 phase 或撤退——若既有 `TryHandleWarCombatOwnership`
   要接管，则**清 `FieldCombatReleased` 并交给它**（保持现状，不重复释放）。
2. 否则统计野战交火：遍历 units（复用 `HasImmediateCombatPriority(actor)` + `IsLiveCombatantActor`），
   得到 engagedCombatants / liveCombatants / captainEngaged，调用 `ShouldReleaseToFieldCombat`。
   - `AbstractDecisive` 抽象决战模式**不释放**（与既有 `ResolveCombatControl` 一致，抽象战斗不接管 actor）。
3. 需要释放且未释放：执行**不改持久 phase**的释放（新私有方法 `EnterFieldCombat`）：
   - `ArmyRouteProviderService.Cancel(id, TargetReplaced)`
   - `AWArmyMarchService.ClearArmy(id)`
   - `ArmyFormationService.RemoveArmy(id)`
   - `ResetStrategicMovementRuntime(runtime)`
   - `ReleaseArmyActors(pArmy)`（交给原版战斗 AI）
   - `runtime.FieldCombatReleased = true;` → `Controllers.Requeue(id); return;`
   （复用 `ReleaseToVanillaCombat` 的动作，但**不**调 `TrySetPhase(VanillaCombat)`，避免下一 tick 因 `insideTargetTerritory==false`
   被 `ResolveCombatControl` 立即 `ReacquireStrategicControl` 而回到拉锯。）
4. 已释放且战场已清（`ShouldReleaseToFieldCombat` 返回 false）：`ExitFieldCombat`：
   - `runtime.FieldCombatReleased = false;`
   - `runtime.JobCursor.Reopen();`（重开 job 分配，恢复 RTS 接管）
   - 不 return，落到正常 `TryHandleWarCombatOwnership` + 编队/路线流程，RTS 自然收回控制。

### 4. 已释放期间跳过重下命令
释放状态下 `ProcessOne` 提前 return（步骤 3 的 requeue），因此不会重下行军/编队任务；
下一 tick 重新评估战场，清了就收回。这正是"打完再被 RTS 控制"。

## 验证
- 构建（NML 源码编译）：`dotnet build` 对应工程 / 现有 build 脚本，需干净通过。
- Rules 测试：`ArmyRtsFieldCombatRules` 加单测（阈值、滞后、captainEngaged、liveCombatants=0 边界）。
  跑 `Tests/AncientWarfare3.Rules.Tests`。
- 对抗仿真：`Tests/ArmyRtsAdversarialSimulation` 若能挂钩野战场景则加一例（两军野外相撞 → 释放 → 清场 → 收回）。
- 注意：按记忆，master 上 .ps1 源码守卫多为既有 stale（37/40 在干净 HEAD 失败），用 stash 差分法判断我方改动是否引入**新**失败，真正门槛 = 构建 + Rules.Tests + 对抗仿真。
- 游戏内实测受阻：当前 NML 自更新循环导致游戏无法启动（先前已诊断，用户暂缓修）。代码层先做完，实测留待启动问题解决。

## 不做
- 不做"RTS 调度脱离到 native"（已验证无效且可能更糟）。
- 不改持久化存档结构。
- 不动既有城内攻城 `ReleaseToVanillaCombat` 路径（保持向后兼容，野战门与之互斥优先级：城内逻辑优先）。

## 风险
- 阈值 25%/5% 为初值，可能需按实测调；集中在 `ArmyRtsFieldCombatRules` 常量，易调。
- 遍历 units 统计交战比例的每 tick 成本：army 单位数通常可控；如需可加采样/缓存，但先直算保持简单。

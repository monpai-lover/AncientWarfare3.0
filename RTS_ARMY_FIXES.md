# RTS军队系统修复

## 修复的Bug

### 1. 国王不应该跟随军队移动

**根本原因：**
- 原始逻辑：`militaryRole = isCurrentCaptain || warrior && !civilAuthority || civilAuthority && hasArmyIndex`
- 第三个条件 `civilAuthority && hasArmyIndex` 导致国王（King）和城市领袖（City Leader）被RTS系统控制
- **错误概念**：国王不是军事单位，不应该跟随军队行军
- **正确设计**：只有**将领（Commander）和士兵（Warriors）**应该跟随RTS军队移动

**修复内容：**

1. `ArmyRtsRules.ShouldOwnMilitaryActor` (line 493-505)
   - 添加提前返回：如果是civil authority但不是captain，直接返回false
   - 简化 `militaryRole` 逻辑：`isCurrentCaptain || warrior`
   - **国王和城市领袖不再被RTS系统控制**（除非他们是军队captain，这种情况也会被特殊处理）
   
   ```csharp
   if (isCivilAuthority && !isCurrentCaptain)
       return false;
   bool militaryRole = isCurrentCaptain || currentProfessionIsWarrior;
   ```

### 2. 战备期间军队有任务但不移动，将领孤身前往战斗

**根本原因：**
- `ResolveMissionTargetStrength` 持续用城市容量更新 `targetStrength`
- `targetStrength = Math.Max(living, cityCapacity)` 导致目标始终 ≥ 当前兵力
- `NeedsReplenishment` 检查 `living * 100 < target * 80` 永远为true
- 所有 `ForceReady`/`RallyReady`/`forcePreDeparture` 都依赖 `departureStrengthReady`
- 军队永远无法离开Rally/Replenish状态，不发布行军路线

**修复内容：**

1. `ArmyRtsControllerService.ResolveMissionTargetStrength` (line 4115-4135)
   - 对于已有persisted `TargetStrength` 的任务，锁定该值：`resolved = Math.Max(living, persisted)`
   - 只在任务首次创建时（`persisted <= 0`）使用城市容量
   - 防止目标兵力在任务执行期间持续膨胀

## 测试验证

修复后应验证：

1. **国王不跟随军队测试：**
   - 创建一个军队，让国王加入（但不是captain）
   - 分配战争任务
   - 观察国王是否**不跟随**军队行军，保持在城市执行治理任务
   - 只有将领（commander）和士兵（warriors）应该跟随军队

2. **集结出发测试：**
   - 创建新战争，观察军队是否正常集结
   - 确认军队在达到合理兵力后能够出发（不需要达到城市满容量）
   - 观察将领是否等待部队集结而不是孤身前往

3. **将领和士兵移动测试：**
   - 确认军队captain（将领）正确跟随RTS路线
   - 确认所有warrior士兵正确跟随编队
   - 确认国王和城市领袖不受RTS控制，保持原有AI行为

## 相关文件

- `Code\core\lineage\ArmyRtsRules.cs` - 核心所有权和补给逻辑
- `Code\core\lineage\ArmyRtsControllerService.cs` - 控制器服务和任务分配
- `Code\core\lineage\AWArmyRoleRules.cs` - 军队角色规则和captain所有权

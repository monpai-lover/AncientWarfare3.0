# AW3.0 姓族 UI 设计（姓氏档案系统 阶段 5）

> 权威需求：`docs/AW3_Lineage_Archive_Task.md` 阶段 5。
> 后端总成（LineageService 权威入口 + 4 表 + 出生双 hook + 晋升/衰落/合流/同姓不婚 + 继承人/氏支分封/年号/头衔/积极建城）已完成，编译 0 错。本 spec 只覆盖 UI 与显示层，全部消费已有数据层，不改后端逻辑。

## 目标

把已建好的姓族后端数据可视化，使玩家能够：
1. 在 actor 信息窗看到自己的姓 / 氏 / 身份，并可点击下钻。
2. 从自定义「姓族」tab 自上而下浏览：姓 → 氏支列表 → 氏族大树 → 家族树。
3. 用**持久化家族树**替换原版"死后即消失"的家族树（仅限有谱系记录者）。
4. 在 kingdom 窗看到继承人 / 年号 / 头衔。

## 约束与既定事实

- 游戏 0.51.0+，NeoModLoader（NML）。net48，`dotnet build -t:Rebuild --no-incremental` 强制。
- 所有 patch / 夺舍类用 `AW_` 命名前缀。Postfix 优先；替换原版族谱用 Prefix return false 接管（少数完全接管场景）。
- 本地提交，**不推送**，直到用户进游戏验证（逐阶段验证规则）。
- 已确认可用 API（来自 AssetRipper 源码 + NML 源码 ModLoader-master + Cultiway/AW2 实例）：
  - NML：`AbstractListWindow<T,TItem>` / `AbstractWindow<T>` / `AbstractListWindowItem<TItem>`（命名空间 `NeoModLoader.api`）；`NeoModLoader.General.UI.Tab.TabManager.CreateTab` + `PowersTab.SetLayout/AddPowerButton/UpdateLayout`；`PowerButtonCreator.CreateSimpleButton`。
  - 原版：`StatsWindow.showStatRow(...)` 返回 `KeyValueField`，其 `on_click_value` 是可点击回调；`UiUnitAvatarElement.show(NanoObject)` 显示头像；`ScrollWindow.showWindow(string id)` 打开窗口；`SpriteTextureLoader.getSprite(path)` 取图标。
  - 原版家族树：入口 `UnitWindow._button_genealogy_tab`（本地化 key `genealogy`），点击经 `WindowMetaTab.doAction` 打开 window id `"family"`（`FamilyWindow : WindowMetaGeneric<Family, FamilyData>`，节点组件 `UnitGenealogyElement`）。原版画祖父母/父母/兄弟姐妹/子女四类，每节点头像+名+性别图标，**死人一律 `isRekt()` 隐藏，Family 全员死后被销毁** → 这是"死后即消失"的根因。原版无配偶/关系标签/年号/氏支信息。

## 整体架构

混合路线（用户拍板），全部新建于 `Code/ui/`（patch 放 `Code/patch/` 与现有 patch 同级）：

**A. 嵌入原版（Postfix patch，不另开窗）**

| Patch 类 | 目标 | 注入 |
|---|---|---|
| `AW_UnitWindowPatch` | `UnitWindow.showStatsRows` | 仅 Xia 有谱系者：身份行 + 姓行 + 氏行（可点击）+ 「家族树」按钮 |
| `AW_KingdomWindowPatch` | `KingdomWindow.showStatsRows` | 继承人行（头像+点击）+ 年号行 + 头衔行 |
| `AW_GenealogyReplacePatch` | 原版族谱 tab 激活点 | 有谱系贵族 → 拦截开我们的树；否则放行原版 |

**B. 新开窗口（NML 封装）**

| 窗口类 | 基类 | 内容 |
|---|---|---|
| `LineageOverviewWindow` | `AbstractListWindow<姓>` | 列所有姓 |
| `ShiBranchListWindow` | `AbstractListWindow<氏支>` | 列某姓下氏支 |
| `FamilyTreeWindow` | `AbstractWindow<树>` | 多叉树，**大树 / 家族树双模式**，懒加载折叠 |

**入口（自定义 tab，照搬 AW2 `Code/ui/TabManager.cs` 写法）**：`OnModLoad` 建「姓族」分栏 tab → 放「姓族列表」按钮 → `ScrollWindow.showWindow("aw_lineage_overview")`。

## 导航连线（两条入口）

```
[姓族 tab → 姓族列表按钮]
   → LineageOverviewWindow(列所有姓)
        ↓ 点姓
   → ShiBranchListWindow(该姓的氏支列表)
        ↓ 点氏支
   → FamilyTreeWindow【氏族大树·懒加载折叠】
        ↓ 点树中某 actor
   → FamilyTreeWindow【家族树·该 actor 居中】

[actor inspect 窗]
   ├ 点「姓」 → ShiBranchListWindow(该姓氏支列表)
   ├ 点「氏」 → FamilyTreeWindow【氏族大树·所属氏支】
   └ 点「家族树」按钮 → FamilyTreeWindow【家族树·本人居中】
                            ↓ 侧栏「回氏族大树」按钮
                         FamilyTreeWindow【氏族大树·所属氏支】

[原版族谱 tab 点击]
   → AW_GenealogyReplacePatch 判定
        有谱系贵族 → FamilyTreeWindow【家族树·本人居中】
        其他       → 放行原版 FamilyWindow
```

**两种树的关系**：
- **氏族大树** = 整个氏支的全量多叉树（始祖→末代全后代，含死人），鸟瞰视图，懒加载折叠。
- **家族树** = 以某 actor 为中心的局部树，聚焦视图。
- 二者互相切换：大树点人 → 进他的家族树；家族树侧栏按钮 → 回其氏族大树。
- 两者**共用同一节点组件 `FamilyTreeNode`**，只是初始根与展开策略不同。

## 信息窗显示规则（`AW_UnitWindowPatch`，仅 Xia 有谱系者）

合流状态读 `kingdom.data.aw_name_integrated`，身份读 actor `aw_lineage_status`：

| 状态 | 身份 | 显示行 | 可点击跳转 |
|---|---|---|---|
| 合流前 | 贵族 | 身份「贵族」+ 姓行 + 氏行 | 姓→`ShiBranchListWindow`；氏→`FamilyTreeWindow` 大树 |
| 合流前 | 平民谱系 | 身份「平民谱系」（不显姓氏） | —（仅「家族树」按钮可入档查询） |
| 合流前 | 奴隶谱系 | 身份「奴隶谱系」（不显姓氏） | —（同上） |
| 合流后 | 所有人 | 身份 + 氏行（姓隐藏） | 氏→`FamilyTreeWindow` 大树 |

外加：有谱系者一律有「家族树」按钮 → 本人局部家族树。无谱系单位整段不注入。

实现：`showStatRow` 取回 `KeyValueField`，对姓/氏行设 `kvf.on_click_value = () => ScrollWindow.showWindow(...)` 并先 `ScrollWindow.get(id)` 上设置目标上下文（见下"窗口上下文传递"）。

## Kingdom 窗嵌入（`AW_KingdomWindowPatch`，对齐 AW2）

Postfix `KingdomWindow.showStatsRows`，注入三行（逻辑后端已做完，UI 只读取显示）：

| 行 | 数据源 | 交互 |
|---|---|---|
| 继承人 | `HeirService.GetHeir(kingdom)` 小头像 `UiUnitAvatarElement.show` | 点击→`inspect_unit`；无继承人不显示 |
| 年号 | `YearNameService.GetYearName(kingdom)`（"鲁伯姬元年"） | 纯文本 |
| 头衔 | `KingdomTitleService.GetTitleString(kingdom)`（"伯国"） | 纯文本 |

## 替换原版家族树（`AW_GenealogyReplacePatch`）

条件替换，不一刀切：
- patch 族谱 tab 激活点（`WindowMetaTab.doAction`，判断该 tab 属于 `UnitWindow` 的 genealogy tab；或更稳妥 patch `UnitWindow` 打开 family 窗的路径）。
- 判定：`Config.selectedUnit` 是 Xia ∧ 有谱系记录（`aw_lineage_id >= 0`）：
  - 是 → `__result` 接管 / 拦截，`ScrollWindow.showWindow("aw_family_tree")` 开我们的家族树（含死人），`return false`。
  - 否 → `return true` 放行原版 `FamilyWindow`。

**我们的树 = 原版好东西 + 补齐**（家族树与氏族树共用节点）：

| 维度 | 原版 | 我们 |
|---|---|---|
| 父母/祖父母/子女/兄弟姐妹 | ✅ | ✅ 保留 |
| 死人 | ❌ 隐藏+销毁 | ✅ **灰调头像 + 生卒年**（核心） |
| 配偶连线 | ❌ | ✅ 补（若无现成配偶档案字段则留桩，配偶连线降级为可选） |
| 关系标签 | ❌ | ✅ 补「父/母/子/兄/配」中文标签 |
| 年号/时代 | ❌ | ✅ 补（接模块 D，标人物活跃时代） |
| 氏支分封标记 | ❌ | ✅ 补（接模块 B，标该节点分出新氏支） |
| 身份标记 | ❌ | ✅ 贵族/平民谱系/奴隶谱系图标 |
| 头像/性别图标 | ✅ | ✅ 保留 |
| 多代纵深 | 3 代 | 家族树局部 3 代为主；氏族大树懒加载下钻全谱 |
| 性能 | 对象池+初始 16 限 | 沿用对象池 + 懒加载折叠 |

## 性能：氏族大树懒加载折叠

- 瓶颈不在建树（SQLite 查 FamilyEdge 几百行很快），在**实例化几百个节点 GameObject**。
- 策略：默认只展开氏支始祖下 2-3 层，深层折叠；节点带展开/折叠按钮，点击才实例化下一层（懒加载）。任何时刻可见节点数十个，不卡。
- 复用对象池回收不可见节点。
- 范围锁定：一棵大树 = 一个氏支（始祖→末代），节点量几十到几百，此策略足够。

## 数据层补充（`LineageQuery`，UI 消费，不改后端逻辑）

现有总览/氏支/成员/家族树查询之上补 4 方法：
- `GetShiBranchFounder(long shiId)` → 氏支始祖 actor id（大树根）。
- `GetChildrenArchive(long actorId)` → 子女档案列表（懒加载下一层）。
- `GetParentsArchive(long actorId)` → 父母档案（家族树向上）。
- `GetSpouseArchive(long actorId)` → 配偶档案；FamilyEdge 未存配偶，需从 ActorArchive / actor.data lover 取，**若无现成字段则留桩返回空**，配偶连线降级为可选。

DTO 节点字段：id、姓名、性别、生卒年、is_alive、身份（lineage_status）、shi_id、（可选）分封年号。**死人从 ActorArchive 取，活人优先 `World.world.units.get(id)` 实时对象。**

## 窗口上下文传递

NML `AbstractListWindow` / `AbstractWindow` 是单例窗口，打开前需告诉它"展示哪个姓/氏支/actor"。约定：每个窗口暴露静态 `SetContext(...)`（如 `ShiBranchListWindow.SetContext(long lineageId)`、`FamilyTreeWindow.SetContext(long shiId, long centerActorId, Mode mode)`），调用方先 `SetContext` 再 `ScrollWindow.showWindow(id)`；窗口在 `OnNormalEnable` 读 context 刷新列表/树。

## 文件清单

```
Code/ui/
├─ AW_LineageTab.cs              自定义「姓族」tab + 「姓族列表」按钮（OnModLoad 调）
├─ windows/
│  ├─ LineageOverviewWindow.cs     AbstractListWindow<姓> 总览
│  ├─ ShiBranchListWindow.cs       AbstractListWindow<氏支> 某姓氏支列表
│  └─ FamilyTreeWindow.cs          AbstractWindow 多叉树（大树/家族树双模式+懒加载）
├─ items/
│  ├─ LineageListItem.cs           姓行（名+总/存活/贵族/氏支数）
│  ├─ ShiBranchListItem.cs         氏支行（名+总/存活/成立年/贵族数）
│  └─ FamilyTreeNode.cs            树节点 prefab（头像+名+性别+生卒+关系标签+身份/氏支图标，死人灰调）
Code/patch/
├─ AW_UnitWindowPatch.cs           信息窗注入姓/氏/身份/家族树按钮
├─ AW_KingdomWindowPatch.cs        kingdom 窗注入继承人/年号/头衔
└─ AW_GenealogyReplacePatch.cs     替换原版族谱 tab（有谱系贵族→我们的树）
```

`ModClass.OnModLoad` 末尾调 `AW_LineageTab.Init()`；patch 类靠 Harmony `[HarmonyPatch]` 自动挂载；`LineageQuery` 补 4 方法。

## 统计字段（对齐任务书）

- **姓窗口行**（`LineageListItem`）：姓名、总人数、存活人数、贵族数、氏支数。
- **氏支窗口行**（`ShiBranchListItem`）：氏支名、总人数、存活人数、成立年数、贵族数。
- 成员列表（氏支详情/家族树成员）：身份、性别、生卒年份、国家、城市、氏。
- "成立年数"= 从该姓/氏支 created_time 到当前世界年（`Date.getYearsSince`）。

## 实现顺序（每步可编译验证）

1. `LineageQuery` 补 4 方法 → 编译。
2. `AW_LineageTab` + `LineageOverviewWindow` + `ShiBranchListWindow`（两列表窗 + tab 入口）→ 编译，能开窗看到姓/氏支列表。
3. `FamilyTreeNode` + `FamilyTreeWindow`（先家族树模式，再氏族大树懒加载）→ 编译。
4. `AW_UnitWindowPatch` + `AW_GenealogyReplacePatch`（信息窗入口 + 替换原版族谱）→ 编译。
5. `AW_KingdomWindowPatch`（继承人/年号/头衔显示）→ 编译。
6. `dotnet build -t:Rebuild --no-incremental` 0 错 → 进游戏验证。

## 验收标准（任务书阶段 5）

1. 「姓族」tab 能打开姓族总览。
2. 点姓能看该姓所有氏支与成员，**含已死者**。
3. 点人物能打开家族树；点族谱 tab 的贵族打开的是我们的持久化树而非原版。
4. **关键**：杀人、存档、读档后，家族树/氏族大树**仍能显示死者亲缘**。
5. kingdom 窗显示继承人头像、年号、头衔。

## 风险与降级

- 原版族谱 tab 的 patch 点若 `WindowMetaTab.doAction` 难精确判定属于哪个 tab，降级为 patch `UnitWindow` 打开 family 窗的上层方法或 family 窗 `OnEnable`，按 `Config.selectedUnit` 判定接管。
- 配偶连线依赖配偶档案字段，无则留桩返回空、UI 不画配偶（不阻塞主流程）。
- 懒加载折叠若实现复杂，可先做"默认展开 N 层 + 折叠按钮"最小版，超深再说。
- NML 单例窗口上下文传递用静态 `SetContext` 约定，避免并发歧义（同一时刻只一个姓族窗活跃）。

## 待办（非本 spec 范围，记录在案）

- 谥号系统（死后按生平定，需家族/王朝历史记录系统）——年号中间字目前用王名首字占位，历史系统做好后替换。
- 头衔升级触发——后端默认 Baron，等政策/天命系统做好接 `PromoteTitle`。
- 积极建城倾向强度——`aw_eager_builder` flag 已打，进 UI 能观察后再调强度。

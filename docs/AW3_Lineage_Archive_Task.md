# AW3.0 姓族 / 氏支 / 家族树档案系统任务书

## 1. 总目标

为 Xia 种族实现一套独立于原版 Clan 的“姓族 + 氏支 + 人物档案 + 家族树”系统。系统要能保存活人和死人、保存亲子谱系、支持姓族查询、支持氏支查询、支持贵族身份衰落，并预留/接入 AW2 的“姓氏合流”国策逻辑。

## 2. 已锁定规则

- 姓族 是 AW3 自建血缘架构，不等同原版 Clan。
- 氏支 是 AW3 自建分支/封地架构，可参考原版 Clan、城市、国家等信息生成，但原版 Clan 不是权威数据源。
- **原版 Clan 也要夺舍（AW_Clan）做深度魔改**：原版 Clan 只有基础排序、没有真正的家族树、死人不保存。AW_Clan 要：① 绘制正确的家族树（基于 AW3 SQLite 的持久亲子边，而非原版乱序）；② 保存 Clan 的死人（接入 ActorArchive/FamilyEdge）。
  - 两条线分工：**自建姓族/氏支/SQLite** = 权威血缘数据源 + 跨存档持久化；**AW_Clan 夺舍** = 在原版氏族对象/UI 上接入我们正确的家族树与死人保存。二者不冲突，AW_Clan 的家族树读 AW3 的 FamilyEdge/ActorArchive。
- 合流前：
    - 贵族男子显示 氏 + 名。
    - 贵族女子显示 名 + 姓。
    - 平民、奴隶显示单名。
    - 同姓不婚生效。

- 合流后：
    - 该国家不显示 姓。
    - 所有人取消单名，统一显示 氏 + 名。
    - 已有氏的人沿用旧氏。
    - 完全没有旧氏的人从随机氏池获得氏。
    - 同姓不婚取消。

- 继承：
    - 父系继承姓族、氏支、谱系。
    - 父亲无谱系时，合流前子女不从母亲继承；若本人以后成为贵族，再新建谱系。

- 贵族身份：
    - King、City Leader、重要成名者获得贵族身份、姓、氏、guizu。
    - 贵族后代继承谱系。
    - 子代、孙代可继续保留贵族身份；曾孙代起若连续无人再成为贵族，则退回平民，移除 guizu。
    - 退回平民/奴隶后，SQL 仍保留其姓族、氏支、亲子关系，可查询。

- 家族树：
    - 不依赖原版 FamilyTree，因为 actor 销毁后原版关系会丢失。
    - AW3 用 SQLite 保存持久亲子边。
    - 第一版 UI 做“三层可展开”：父母 / 本人 / 子女，点击节点重新居中继续查。

- **窗口导航为三层（已锁定）**：姓 → 氏支 → 家族树/成员。
    - 姓只有几十个（先秦古姓），是“血缘大流”；很多人同姓不同氏。点一个姓不直接列个人，而是列该姓下的所有**氏支**。
    - 氏支 = 一个贵族始祖及其**父系后代树**（纯血缘、跨国家稳定）。氏名和 source_type（封地/继承/随机/合流/名人）作为来源属性记录，查询以血缘树为准、来源作展示。
    - 从某个氏支的**家族树**里查具体个人。
- **统计维度（姓窗口 / 氏支窗口都要，参考原版 ClanWindow）**：总人数、存活人数、成立年数（参考 `clan.getFoundedDate()` / `units.Count`）、贵族数（当前有 guizu 的人数）；姓窗口另显示该姓下**氏支数量**。

## 3. 核心数据模型

### Actor data 约定

继续兼容 AW2/现有命名字段：

- family_name：姓。
- clan_name：氏。注意这里只表示 AW3 的“氏”，不等同原版 Clan 对象。
- chinese_family_name：中文命名兼容字段。
- aw_given_name：单名，所有改全名操作都基于它。
- aw_lineage_id：姓族谱系 ID。
- aw_shi_id：氏支 ID。
- aw_noble_distance：距最近贵族祖先的父系代数；本人贵族为 0，子代 1，孙代 2，曾孙代 3。
- aw_name_integrated：该 actor 最近一次被合流规则处理过。
- aw_lineage_status：none / noble / common_lineage / slave_lineage。

### SQLite 表

- ActorArchive
    - 保存所有有谱系/有氏的 Xia；合流后所有 Xia 都会有氏，因此都进档案。
    - 字段：id, given_name, display_name, family_name, clan_name, lineage_id, shi_id, asset_id, sex, status, kingdom_id, kingdom_name, city_id, city_name, original_clan_id, parent_id_1, parent_id_2, generation, noble_distance, birth_time, death_time, is_alive, name_integrated, head, skin, skin_set。

- LineageGroup
    - 一个姓族/血缘谱系。
    - 字段：lineage_id, family_name, founder_actor_id, founder_name, created_time, origin_kingdom_id, origin_city_id, is_extinct。

- ShiBranch
    - 一个氏支。
    - 字段：shi_id, lineage_id, clan_name, founder_actor_id, source_type, origin_kingdom_id, origin_city_id, origin_original_clan_id, created_time, is_extinct。
    - source_type 可为 enfeoffed / inherited / random / integration / special_figure。

- FamilyEdge
    - 持久亲子边。
    - 字段：child_id, parent_id, parent_slot, child_lineage_id, created_time。

- KingdomLineageState
    - 保存国策合流状态。
    - 字段：kingdom_id, kingdom_name, name_integrated, integration_time。

## 4. 核心服务接口

新增 LineageService，作为唯一权威入口：

- OnActorBorn(Actor actor)：出生后写入单名、父系继承、亲子边、初始档案。
- OnActorPromoted(Actor actor, NobleTrigger trigger)：成为国王/城主/成名者时赋予或刷新贵族身份。
- EnsureLineageForNoble(Actor actor)：无谱系贵族创建姓族和氏支。
- RefreshNobleStatus(Actor actor)：按 aw_noble_distance 添加或移除 guizu。
- ApplyDisplayName(Actor actor)：按性别、身份、国策状态重写显示名。
- ApplyNameIntegration(Kingdom kingdom)：国策完成时扫该国所有 Xia，隐藏姓、补氏、统一氏名。
- CanFallInLoveByLineage(Actor a, Actor b)：合流前同姓不婚判断。
- ArchiveActor(Actor actor, ArchiveReason reason)：出生、晋升、死亡、存档前统一 upsert。
- GetSurnameOverview()：姓族总览数据。
- GetSurnameMembers(string familyName)：某姓所有成员，含已死者。
- GetShiBranches(string familyName)：某姓下所有氏支。
- GetFamilyTree(long centerActorId)：返回三层家族树节点。

## 5. 阶段任务

### 阶段 1：SQLite 基础设施与持久档案

- 从 AW2 迁移并适配 SQLite 基础设施。
- Actor.id 使用 long。
- DB 运行时路径建议为：<modFolder>/.runtime/aw3_lineage_archive.db。
- 存档路径建议为：<saveFolder>/aw3_lineage_archive.db。
- Patch：
    - SaveManager.saveWorldToDirectory(...)：保存时复制 DB 到存档目录。
    - SaveManager.loadWorld(...)：读档时恢复 DB。
    - MapBox.generateNewMap()：新世界清空运行时 DB。
    - Actor.die 使用 Prefix：死亡前归档完整数据。
- 阶段验收：
    - 建表成功。
    - 杀死有档案 Xia 后，DB 有记录。
    - 存档、退出、读档后死者仍在 DB。

### 阶段 2：谱系、氏支、命名与贵族身份

- 修改 XiaNaming：
    - 平民/奴隶默认只生成单名。
    - 单名写入 aw_given_name。
    - 不再在普通出生时无条件写 family_name/clan_name。
- 实现父系继承：
    - 子女查父亲 actor 或 SQL 档案。
    - 父亲有谱系则继承 lineage_id, shi_id, family_name, clan_name。
    - aw_noble_distance = father.aw_noble_distance + 1。
- 实现贵族晋升：
    - King、City Leader、重要成名者触发。
    - 无谱系则随机古姓，新建姓族。
    - 氏优先按封地/城市/国名生成，失败再随机氏。
    - 添加 guizu，距离重置为 0。
- 实现身份衰落：
    - aw_noble_distance >= 3 且本人不是当前贵族时，移除 guizu，状态改 common_lineage。
    - 奴隶状态优先显示为 slave_lineage，不显示全名。
- 实现命名格式：
    - 合流前贵族男：氏 + 名。
    - 合流前贵族女：名 + 姓。
    - 合流前平民/奴隶：名。
    - 合流后所有 Xia：氏 + 名。
- 阶段验收：
    - 普通 Xia 出生只有单名。
    - 贵族男/女格式正确。
    - 贵族后代继承谱系。
    - 曾孙代无新贵时退回平民但仍可在 DB 查到。

### 阶段 3：姓氏合流国策接入

- 先用 KingdomLineageState.name_integrated 和 kingdom.data 自定义字段保存状态。
- 等 AW3 国策系统迁移时，接入 AW2 的 name_integration 状态与执行动作。
- 合流执行：
    - 扫描该国所有 Xia。
    - 有旧氏则沿用。
    - 无旧氏则从随机氏池分配。
    - 写入档案。
    - 重命名为 氏 + 名。
    - UI 不再显示 姓。
    - 同姓不婚对该国取消。
- 阶段验收：
    - 国策前按贵族/平民规则显示。
    - 国策后该国所有 Xia 都有氏名。
    - 旧贵族、退回平民、奴隶沿用旧氏。
    - 无旧氏普通人获得随机氏。

### 阶段 4：同姓不婚

- Patch Actor.canFallInLoveWith 或等价恋爱判定。
- 仅在双方都是 Xia、双方都有 family_name、双方当前国家都未合流、姓相同的情况下返回 false。
- 合流后不因隐藏旧姓阻止婚姻。
- 阶段验收：
    - 合流前同姓不能恋爱/繁育。
    - 异姓可正常繁育。
    - 合流后同旧姓不再被隐藏规则阻止。

### 阶段 5：UI 与查询窗口

- Actor 信息窗口：
    - 有谱系者显示身份：贵族 / 平民谱系 / 奴隶谱系。
    - 合流前贵族显示 姓 和 氏。
    - 合流前平民谱系/奴隶谱系不显示姓氏，但可显示“可在家族树查询”的入口。
    - 合流后只显示 氏。
    - 姓、氏、家族树入口均可点击。
- 总 tab：
    - 新增 姓族检视 按钮。
- 姓族总览窗口：
    - 每个姓一个按钮。
    - 显示该姓总人数、存活人数、贵族人数、氏支数。
- 姓族详情窗口：
    - 成员列表：活人 + 死人，标身份、性别、生卒、国家、城市、氏。
    - 氏支列表：该姓下所有氏支。
    - 家族树入口：点人物打开三层树。
- 家族树窗口：
    - 中心人物。
    - 上层父母。
    - 下层子女。
    - 点击任一节点重新居中。
    - 死者使用 SQL 档案渲染，活人优先用当前 actor。
- 阶段验收：
    - tab 能打开姓族总览。
    - 点姓能看到该姓所有成员，含已死者。
    - 点人物能打开家族树。
    - 杀人、存档、读档后，家族树仍能显示死者亲缘。

## 6. 编译与验证要求

每阶段完成后必须运行：

    & "$env:ProgramFiles\dotnet\dotnet.exe" build "F:\WorldBox New Mod\AncientWarfare3.0\AncientWarfare3.csproj" -c Debug -t:Rebuild --no-incremental

验收标准：
- 不允许只依赖增量编译。
- 每阶段由用户进游戏验证。
- 阶段通过后再提交并推送 GitHub。
- 阶段 1、3、5 必须重点验证“杀人 -> 存档 -> 读档 -> 死者和家族树仍可查”。

## 7. 风险与待核实点

- Actor.die 必须用 Prefix，否则死亡过程中 city/kingdom/clan/family 可能已被清理。
- 出生补丁要确认最稳入口，优先找能拿到父母 ID 后、命名前或命名后的 Hook。
- 原版 UI 的头像组件能否直接用 SQL 档案重建死者头像，需要实现时验证；不行则第一版用文字列表替代头像。
- 合流国策在 AW3 当前还未完整迁移，阶段 3 先做服务和状态字段，后续国策系统接入时只调用 ApplyNameIntegration。
- clan_name 字段名字容易和原版 Clan 混淆，代码注释必须明确：它表示 AW3 的“氏”，不是原版 Clan ID。

### Clan 夺舍方式（已核实结论）

经核实新版 `SystemManager.getNextObject()=new TObject()`（protected 非 virtual），**无法夺舍出 AW_Clan 子类实例**（新建 clan 恒为原版 Clan）。对照 AW2：AW2 旧版 `newObject(string)` 是 virtual 可 override 返回 AW_City（AW_CitiesManager.cs:40），新版没了这条路。`loadObject(TData)` 虽 public virtual（读档可夺舍），但新建走不通，子类方案不成立。

**为什么不能子类夺舍（双版本 DLL 实证）**：
- 旧版（2024 DLL，ilspycmd 反编译）：`public virtual TObject newObject(string pSpecialID=null){ new TObject(); }` —— public virtual，AW2 靠 override 它产出 AW_City。
- 新版（2025 源码）：`protected TObject newObject()` + `protected TObject getNextObject(){ new TObject(); }` —— protected 去 virtual，`new TObject()` 的 TObject 编译时固定，子类无法重写新建类型。只剩 `loadObject` 是 public virtual（只拦读档）。
- ∴ 游戏引擎主动收紧封装（为对象池复用），子类夺舍只能拦读档、拦不了运行新建，会原版/子类混杂崩溃。NML 也不提供对象夺舍。

**∴ 用 Harmony 通用夺舍工具（移植 AW2 [MethodReplace]，AW_ 命名）**：
- 移植 AW2 的 `MethodReplaceAttribute` + `HarmonyTools.ReplaceMethods()`（`Code/attributes/` + `Code/utils/`）到 AW3.0。机制：用 **Transpiler 丢弃目标方法原 IL，生成“调用你的替换方法 + return”**。
- 关键：它走 Harmony Patch，**别人对同一方法的 Prefix/Postfix 仍正常跑**（Transpiler 只替换方法体那一层，不破坏 patch 链）—— 满足“函数内部换成调用自己、别人 patch 还能用”的要求。
- 适配新版：去掉 `Main.LogInfo/LogWarning` 依赖（换 `ModClass.LogInfo/LogWarning`）；OnModLoad 时调 `ReplaceMethods()` 扫描注册。Transpiler 互相冲突是边缘情况（极少 mod 对同方法挂 Transpiler），暂不特殊处理。
- Clan 魔改用它 + Postfix 组合：夺舍 `ClanManager.newClan`(public)/`ClanWindow` 方法注入家族树绘制+死人显示；数据源 = AW3 自建 LineageGroup/ShiBranch/FamilyEdge（SQLite 持久化），原版 Clan 仅作 UI 载体。
- 姓氏/氏支也可存 `clan.data`/`actor.data` 的 CustomDataContainer（随存档序列化）作运行时缓存。

### 出生 / 晋升 / 新世界 钩子（已核实）
- 出生：`Actor.newCreature()`（Actor.cs:978, internal，新生儿初始化）；命名 `generateName(MetaType,long,ActorSex)`（2356）。最稳入口实现时再定（任务书风险点）。
- 晋升：`City.setLeader(Actor,bool)`（City.cs:2420, public）、`Kingdom.setKing(Actor,bool)`（Kingdom.cs:575, public），Postfix。
- 新世界清库：`MapBox.generateNewMap()`（MapBox.cs:992, public）。
- 存档持久化：Postfix `SaveManager.saveWorldToDirectory(pFolder)`（SaveManager.cs:85）；Postfix `loadWorld(pPath)`（506）。

## 8. 默认假设

- 古姓从词库随机，不强制夏王族全为姒姓。
- 氏生成合流前封地/城市优先；合流后无旧氏者随机氏池。
- 三代无贵的精确定义：贵族本人 0，子代 1，孙代 2，曾孙代 3；距离达到 3 且本人未重新成为贵族时退回平民。
- 父亲缺失或父亲无谱系时，合流前不从母亲继承姓氏。
- 跨种族仍按原版 isSameSpecies 隔离；本系统只处理 Xia。

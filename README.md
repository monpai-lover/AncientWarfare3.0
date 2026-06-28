# AW3.0 功能蓝图（基于 AncientWarfare2.0 全功能梳理）

> 本文档把旧项目 **AncientWarfare2.0（AW2）** 的全部功能逐一梳理，作为新模组 **AW3.0** 在新版 WorldBox 上**重新开发/重构**的蓝本。
> AW3.0 不再移植旧代码，而是在新版游戏架构上重写。分批做，先做最简单的 **race（种族）→ trait（特质）→ item（物品）**。
>
> 旧项目位置：`F:\WorldBox New Mod\AncientWarfare2.0-main`
> 新版游戏源码参考：`F:\WorldBox New Mod\AssetRipper_export_20260628_163320\ExportedProject\Assets\Scripts\Assembly-CSharp\`
> 类型迁移映射见记忆 `aw2-type-migration-map.md`。

---

## 0. 总体设计与主题

AW2 是一个以**中国上古夏朝**为主题、深度改造 WorldBox 政治/军事系统的大型 mod。核心玩法支柱：

| 支柱 | 一句话 |
|---|---|
| **夏朝文明 (Xia)** | 新增种族 + 单位 + 建筑 + 命名 + 神力，自成体系 |
| **天命系统 (MoH)** | 全世界同时只有一个"天命王朝"，天命值动态升降，崩溃则天下大乱、群雄逐鹿 |
| **政策/阶级系统** | 王国按状态机从奴隶制→封建→帝制演进，状态决定城市 AI 行为 |
| **附庸/宗主 (Vassal)** | 王国可成为他国附庸，颜色/外交/战争联动，含独立战/吞并战 |
| **奴隶制** | 捕奴→繁殖→奴隶军，独立的奴隶经济与军事层 |
| **城市税收与科技** | 非首都向首都缴税，城市研究科技树解锁政策 |
| **历史记录** | 全程 SQLite 入库，提供编年史/王国史窗口 |
| **中文命名** | 集成"一米中文名"mod，姓/氏/名/国号/年号/城名全套中文生成 |

**架构模式**：AW2 用"夺舍 + 扩展数据"——把原版 Manager 替换为 AW_ 版本（继承+override+MethodReplace），扩展字段放 `addition_data`（BaseSystemData 子类）。
**⚠️ AW3.0 重构注意**：新版 Manager 创建机制 `getNextObject()=new TObject()` 非 virtual，夺舍只能走 `loadObject`（virtual）路径；id 体系从 string 改为 **long**；`UnitGroup→Army`、`MapText→NameplateText`、`MapIconLibrary→QuantumSpriteLibrary` 等大量类改名。建议 AW3.0 尽量**少夺舍、多用游戏官方扩展点**（PlotAsset 委托、Asset 配置、Harmony patch），降低对原版内部结构的耦合。

---

## 1. 内容库（AW3.0 优先重做：race → trait → item）

### 1.1 种族 RacesLibrary —— 夏朝 Xia

**新增种族 `Xia`**（clone from `human`），关键属性：

| 属性 | 值 |
|---|---|
| civ_base_army_mod | 0.5（军事起步弱）|
| civ_base_zone_range | 15 |
| civ_baseCities | 1 |
| hateRaces | ["orc"]（天敌兽人）|
| production | bread, pie, tea |
| 偏好武器 | sword×10, ge×10, bow×5 |
| 偏好属性 | diplomacy/warfare/stewardship/intelligence 各×1 |
| 偏好食物 | bread/fish/tea 各×1 |
| path_icon | "ui/Icons/iconXias" |
| main_texture_path | "races/Xia/" |
| nomad_kingdom_id | "nomads_Xia" |
| banner_id | "Xia" |
| 皮肤 | 男/女平民各10、战士10 |
| 氏族资源 | 背景17种、图标22种 |

附带操作：克隆命名生成器 `Xia_name`（from human_name）、`cloneBuildingKeys(human→Xia)`；四个原版种族的 `name_template_kingdom` 改为 `Xia_kingdom`（中文名启用时各种族用专属模板）。
关联 patch：`ActorAnimationLoader.loadAnimationBoat`（Xia 船动画路径替换）、`BannerLoaderClans.create`（氏族旗帜框）。

### 1.2 特质 TraitLibrary（7 特质 + 2 特质组）

**特质组**：`aw2`（蓝 #3BAFFF）、`aw_social_identity`（橙 #FF9300，组内互斥）。

| 特质 id | 组 | 关键效果 | special_effect |
|---|---|---|---|
| `figure` | aw2 | mod_health+15, stewardship+10 | — |
| `天命` | aw2 | stewardship+150, dipl+15, war+14, int+14 | `Actionlib.checkP`（天命状态检定）|
| `first` | aw2 | dipl+15, war+14, int+14 | `tianmingP`（自动篡位，驱动天命继承）|
| `formerking` | aw2 | — | `Actionlib.former` |
| `禁卫军` | aw2 | scale+0.03, health+2, damage+25, speed+15, knockback_reduction+100 | — |
| `rebel` | aw2 | sameTraitMod=20, health+2, dipl+35, stew+35, war+4 | `Actionlib.rebelkingdom`（义军抱团）|
| `zhuhou`（诸侯）| social_identity | mod_health+5, stewardship+5 | — |
| `guizu`（贵族）| social_identity | fertility+35 | — |
| `slave`（奴隶）| social_identity | birth=0, inherit=100（世袭）, interval=3 | 周期性强制职业=Slave |

逻辑要点：`first.tianmingP` —— >17岁且有国/城的非国王，没诸侯特质则尝试 `historical_character_usurpation` 篡位；全种族只留1个 first；篡位有 10 年冷却（`LastUsurpationYears`）。

### 1.3 物品 ItemLibrary（3 物品）

| id | 类型 | 关键属性 |
|---|---|---|
| `ji`（戟）| Weapon (clone sword) | damage10, atk_speed-1, crit3%, knockback_red0.1, val800, mat[bronze,copper], 命中施加 `qing` 状态(0.5s) |
| `ge`（戈）| Weapon | 同戟但 crit6%（翻倍）|
| `binfa`（兵法）| Legendary Amulet | warfare+20, crit+3, val2000, mat[bronze] |

另：四个原版种族各批量加 ji/ge/binfa 偏好（×7）；`gold` 资源上限提到 50000（Main.cs 里另设为 99999999，以代码为准）。
`qingAttack` 回调 → 施加 `qing` 状态，对应 XiaTower 的 `FireArrow` 投射物贴图复用。

### 1.4 其他内容库（后续批次）

- **ProfessionLibrary**：`slave`（奴隶，can_capture）、`heir`（继承人）。枚举 `AWUnitProfession{Null,Baby,Unit,King,Leader,Warrior,Heir,Slave}`。
- **ActorAssetLibrary**：`unit_Xia`（成年，11头型，地图色#33724D，max_age90,max_children6）、`baby_Xia`（成长为 unit_Xia）、色系 `xia_default`（#FFC984→#543E2C 黄棕肤）。
- **BuildingLibrary / BuildingAssetLibrary**：全套 `xxx_Xia`（血量×3、免火/酸/龙卷风、不随王国染色）+ 升级链(house0-5/hall0-2/windmill0-1)；**XiaTower**（火箭塔，1000血，6连发 FireArrow，priority114514，不可燃，建边境）。
- **GodPowerLibrary**：`spawn_xia`（召唤夏人）、`vassal`/`vassal_remove`（设/解附庸）、`vassal_zones`（附庸地图层）。
- **StatusEffectLibrary**：`tianming0`（天命兴盛:armor+10,damage+60,免击退,5s）、`tianmingm1`（天命衰退）、`qing`（清扫特效0.5s）。
- **ProjectileLibrary**：`FireArrow`（火箭,抛物线,speed15,不改地形）。
- **WarTypeLibrary**：`tianming`/`tianmingrebel`/`reclaim`/`vassal_war`/`independence_war`（各自 alliance_join 不同）。
- **LoyaltyLibrary**：`tianming`忠诚（天命兴衰影响城市忠诚）、`tianminggo`（天命国-200外交)、`vassels`（附庸+500~1000)、`vasselsrebel`（附庸超宗主-200)。
- **KingdomAssetLibrary**：`Xia`/`nomads_Xia` 王国（tag civ+Xia，友好human/Xia/neutral/good，敌对bandits）；human/nomads_human 加 Xia 友好。
- **UnitGroupTypeLibrary**：`convention`(10)/`guards`(5)/`slaves`(20)。

---

## 2. 核心玩法系统

### 2.1 天命 MoH（utils/MoH/MoHTools.cs + AW_KingdomManager.MoH.cs）

- 全世界唯一"天命王国"，国王有 `first` 特质，称"朝"。天命值 `MOH_Value` 范围 [-30, 100]，初始30。
- **每年增减**：无战+1/有敌-1/未称霸-2/希望纪元+2/绝望灰烬混沌纪元-20/国王有first+5/国王≤24岁-1/国王智力≤5 -1/王室人口≤2 -1。
- 降到下限 → `MoHKingdomBoom()`：天命国所有非首都城市触发 rebellion Plot，分裂成叛乱政权，清天命。
- **天命战争**：`whisper_of_war` 打天命国 → 转 `tianming` 战争类型；双方首都归一方则该方胜、设新天命、转交邻接城市。
- **起义军 Rebel**：天命崩溃后产生 Rebel 国，Rebel 内战占领速度×8，快速决出新霸主。
- **称帝**：无天命时，控制原天命城市≥65% 且最强者称帝建新朝，国王得 first，改年号。
- SQLite `MOH` 表记录天命起止。

### 2.2 政策/阶级系统（core/kingdom_policies/）

- 王国持多种 `PolicyStateType` 状态（social_level/army_main_soldiers/city_organization/name_organization/enfeoffment_type），存 `current_states` 字典。
- **状态机**（AW_Kingdom.UpdateForPolicy 每帧）：政策完成→从队列取下一个或用 `policy_finder` 找→Planning→InProgress→Completed→切状态。状态切换时所有城市 `clearJob` 重选任务。
- **政策链**：default →(start_slaves)→ slaveowner →(start_halfaristocrat)→ halfaristocrat →(base_enfeoffment)→ enfeoffment_base →(favor_order/continuous_enfeoffment[需科技])→ 推恩令/无限分封。支线：control_slaves/slaves_army/name_integration(姓氏合流)/change_capital(迁都)/title_upgrade(升爵)。
- **爵位** Baron→Marquis→Duke→King→Emperor，影响 getMaxCities(+0/+2/+4/+8/+16) 和显示（帝+天命="朝"，帝+former="残部"，Rebel="义军"）。
- 政策状态决定城市 `city_task_list`（城市 AI 能做的任务）和 `calc_kingdom_strength`（国力算法）。

### 2.3 附庸/宗主（AW_Kingdom.Vassal.cs）

- `SetVassal(lord)`：存原色→设 suzerain_id→入 lord.vassals→清 Rebel→记 VASSAL 表→变宗主色→结束双方战争→随宗主入盟。
- `RemoveSuzerain()`：恢复原色。`GetRootSuzerain()` 链式查根（带循环保护）。
- **战争联动**（AW_WarManager.newWar）：宗主参战附庸跟随，附庸参战宗主+兄弟附庸跟随（攻防四种情况）。
- 附庸地图模式：同根宗主城市同色渲染（独立线程）。
- 相关 Plot：vassal_war（臣服战）/Independence_War（独立战，附庸对宗主好感<950）/absorb_vassal（吞并附庸，关系>10年+军力够）/active_vassal（弱国主动求附庸）。

### 2.4 城市税收与科技（AW_City.cs / .Tech.cs）

- **税收**（updateAge）：gold_in_tax=人口/2，减军队/建筑/无房支出=gold_change。非首都按忠诚度向首都缴 gold_change/2（忠诚<-100不缴，>100全额，中间按比例）。
- **奴隶口粮**：城市有奴隶则取总食物10%作口粮 `food_count_for_slaves_this_year`。
- **科技**：`AW_CityTechAsset`（前置/分支/cost/rank/职业要求/research_action）。市民工作时 `PushResearchThrough` 推进研究，完成入 `own_tech`、解锁分支。农业(husbandry/irrigate)、工业(pottery/mining/...iron)、政策科技(解锁推恩令等)。

### 2.5 战争/联盟/军事组

- **AW_War**：记录开战城市快照、攻防首都；天命战争结算 ResolveTianmingWar。AW_WarTypeAsset 加 checkvictory/togglecapture。
- **AW_Alliance**：王国入盟时附庸随入。
- **AW_UnitGroup**（→新版 Army）：城市可有多类型军团（convention/guards/slaves），各有 max_count、领袖查找委托、创建回调。SetCity 迁移整组，城市 army 指向最大组。

### 2.6 事件/历史 SQLite（core/events/ + table_items/）

- EventsManager 单例管 SQLite（临时 .tmp.db），反射扫 `[TableDef]` 自动建表。CityPopRecordManager 单独库按城市建表记人口。
- 表：Kingdom/City/War/Alliance/Actor/KingRule/MOH/VASSAL/USURPATION/INTEGRATION/CAPITALCHANGE/KingdomChangeName/KingdomChangeYear/CityChangeName/KingdomWar/CityPopComposition。
- 触发点：新建/消亡对象、即位/卸任、改元、天命得失、篡位、兼并、迁都、附庸建/解/吸收。

---

## 3. AI 行为树（ai/）

**Actor 行为**：FindSlaveToCatchAround（扫描周围8格血<50%无Clan的敌方平民）→ CatchTargetAsSlave（俘虏:加slave特质+跨城转移+设奴隶职业）→ SubmitSlaves（回城交付）；FindTileKing/RawGoToTileTarget（禁卫军跟国王）。
**City 行为**：BehProduceNobles（贵族在原Clan内繁衍,速率×4）、BehProduceSlaves（奴隶+贫民繁殖,生的还是奴隶）、BehCheckSlaveJobs（派奴隶活+维持捕手编制≤3）、BehCheckSlaveArmy（组奴隶军团）、BehCheckGuard（贵族组禁卫军,换青铜戟甲）、BehCheckRetirement（老兵≥70%寿命退役加veteran）。
**Kingdom 行为**（追加到 do_checks）：CheckHeir（维护继承人）、CheckNewCapital（迁都）、CheckPromotion（每50年升爵）。
**Job/Task**：slave_catcher/king_guard/slave_warrior 公民职业；`CityJobLibrary.CheckAndGetCityJob(policyState)` 按政策状态动态拼装城市任务链（**政策驱动 AI 的核心**）。

---

## 4. Harmony 补丁（patch/）

| Patch | 目标 | 类型 | 作用 |
|---|---|---|---|
| ActorPatch | ActorBase.nextJobActor | MethodReplace | 重写职业→任务映射（支持mod职业）|
| ActorPatch | ActorBase.taskSwitchedAction | Postfix | 工作时按智力推进科研 |
| CitiesManagerPatch | CitiesManager.buildNewCity | Postfix | 新城入库 |
| ClanManagerPatch | ClanManager.checkActionKing | Postfix | 触发收复/附庸/独立/吞并 Plot |
| ClanManagerPatch | ClanManager.tryPlotWar | MethodReplace | AI优先收复失地 |
| ClanManagerPatch | ClanManager.tryPlot{Join,Dissolve,New}Alliance | Prefix | 仅宗主可外交结盟 |
| DiplomacyManagerPatch | DiplomacyManager.findSupremeKingdom | Transpiler | 称霸计算含附庸军力 |
| KingdomHeirPatch | KingdomBehCheckKing.findKing | Prefix | 有继承人则直接即位 |
| KingdomHeirPatch | MapIconLibrary.drawLeaders | Postfix | 小地图画太子标记 |
| KingdomManagerPatch | KingdomManager.makeNewCivKingdom | Postfix | 新王国入库 |
| MapIconPatch | MapIconLibrary.drawArmies | MethodReplace | 画多类型军团图标 |
| MapIconPatch | MapIconManager.updateScaleEffect / drawCursorZones | Transpiler | 附庸地图模式高亮 |
| PathFinderPatch | PathfinderTools.tryToGetSimplePath / AStarFinder.FindPath | MethodReplace | 修浅水卡顿+道路加速(cost0.01) |
| PlotPatch | Plot.isSameType | Postfix | 同plot_type判为同类 |
| WolrdLogPatch | WorldLogMessageExtensions.getFormatedText | Postfix | mod事件日志富文本(天命/篡位/附庸战等14种)|
| SlavesPatch | CityBehProduceUnit.findPossibleParents | MethodReplace | 排除奴隶参与自由人生育 |
| SlavesPatch | Actor.consumeCityFoodItem | Transpiler | 奴隶吃配给粮，不足无心情加成 |
| MoHCorePatch | MapText.showTextKingdom | Postfix | 天命国城市名牌特殊图标 |
| NamePatch(中文名) | Clan.createClan/addUnit/getMaxMembers, WindowCreatureInfo.OnEnable | Postfix/Prefix | 姓氏命名+贵族特质+Clan上限20+信息窗显示姓氏 |

**自定义机制 `[MethodReplace]`**（attributes/ + utils/HarmonyTools.cs）：用 Transpiler 整体替换目标方法体（丢弃原IL，跳转到替换方法）。OnLoad 时全程序集扫描注册。支持静态/实例方法（参数位移兼容 this）。**⚠️ AW3.0 慎用——nameof 只校验名不校验签名，新版改签名会运行时静默失效。**

---

## 5. UI（ui/）

- **TabManager**：AW2 标签页（spawn_xia召唤/vassal设解附庸/天命面板/王国历史/附庸列表/附庸地图层/历史名人开关）。
- **KingdomWindow 扩展**（KingdomWindowAdditionComponent，Postfix 注入）：国王头像+年号+当前社会形态+执行中国策+太子头像+宗主旗帜+历史/政策树按钮。
- **KingdomMoHWindow**：天命面板（国王/年号/天命描述/社会状态/国策队列/可选国策，消耗10天命入队）。
- **KingdomHistoryWindow**：编年史（左历任君主列表，右5标签，Review已实现:即位/改元/天命/篡位/兼并/迁都/附庸全时间线）。
- **KingdomPolicyGraphWindow / CityTechGraphWindow**：政策树/科技树 DAG 图（拓扑排序布局）。
- **Prefab**：PolicyButton/StateButton/Tooltip/KingRuleHistoryItem/SimpleText/CityTechButton。

---

## 6. 命名系统（中文名集成，#if 一米_中文名）

- **Xia 生成器**（name_generators/Xia/）：Xia_name（姓+名/千字文，代码注册）、Xia_kingdom（中文国名前缀）、Xia_city（真实城名 或 上字+下字）、Xia_clan（家乡+姓氏+家/氏/族）、Xia_culture（发源地+文化）。
- **词库**（name_generators/lib/）：中文名字(1133)、中文国名前缀(~200)、城名上(59)/下(129)、国号前/后(各18)、姓(~40 先秦八姓)、氏(~400 百家姓)。
- **姓 vs 氏**：姓=上古血统姓(姬姒嬴)，氏=后天族名(赵钱司马,=clan_name)。
- **SpecialFigure 历史名人**（钩 ActorManager.spawnPopPoint）：姬发80%(姓姬国周)、嬴政/刘邦/曹丕/司马炎各0.5%；注入姓氏+1500血+figure+first特质，全局各一次。另钩 drawKings 画名人小地图图标。

---

## 7. 启动流程（Main.cs）

- **OnLoad**：Configure→初始化常量类→BaseInstPredictor.init→HarmonyTools.ReplaceMethods（扫 MethodReplace）。
- **Awake**：本地化→中文名生成器+词库→实例化所有内容库(Task/Job/Trait/Race/Item/Building...)→各 AW_Manager.init→注册资产库→政策post_init→TabManager/WindowManager/MapMode init。
- **Start**：禁用原版 inspect_unit/village/kingdom 窗口→加载Xia旗帜→patchHarmony（注册全部Harmony patch）。
- **Update**：检测新种族补皮肤；EventsManager 懒加载。
- **Reload**：重载本地化。

---

## 8. AW3.0 重构建议（分批路线）

按用户指定"先简单后复杂"，建议批次：

1. **批A — race 夏朝种族**：在新版重建 Xia 种族 + unit_Xia/baby_Xia + xia_default 色系 + 王国 Xia/nomads_Xia。用新版 `Subspecies`/`ActorAsset`/`raceLibrary`(查新名) API。【最先做】
2. **批B — trait 特质**：2 特质组 + 9 特质。新版 Trait API 基本兼容，注意 special_effect 委托签名。
3. **批C — item 物品**：ji/ge/binfa + 武器偏好 + qing 状态联动。
4. **批D — 建筑/单位/神力/投射物**：XiaTower、建筑升级链、spawn_xia、FireArrow。
5. **批E — 命名系统**：Xia 生成器 + 词库 + 中文名集成 + SpecialFigure（依赖中文名 mod 1.5.0）。
6. **批F — 天命系统**：MoH 全套（值/战争/起义/称帝）——核心但复杂。
7. **批G — 政策/阶级**：状态机 + 政策链 + 爵位 + 城市 AI 政策驱动。
8. **批H — 附庸/宗主**：SetVassal/战争联动/地图模式/独立吞并战。
9. **批I — 奴隶制 + 税收科技**：捕奴 AI + 奴隶军 + 缴税 + 科技树。
10. **批J — UI + 历史记录**：各窗口 + SQLite 编年史。

**每批原则**：优先用新版官方扩展点（PlotAsset 委托、Asset 配置、标准 Harmony patch），少做深度夺舍；id 用 long；每批 `dotnet build -t:Rebuild --no-incremental` 验证 0 错误，再由用户跑游戏验证运行。

---

*本蓝图由通读 AW2 全 194 文件 / 16561 行生成。细节数值以旧代码为准，可随时回查 `F:\WorldBox New Mod\AncientWarfare2.0-main\Code`。*

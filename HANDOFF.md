# AncientWarfare3.0(AW3.0)交接档案

> WorldBox mod。游戏 v0.51.2,NeoModLoader(NML),C# .NET Framework 4.8(net48)。主题:中国夏朝。
> 本档案为完整接手说明:架构、各系统、关键坑、文件清单、待测试项。最后更新随本会话批次。

---

## 0. 环境 / 构建(必读)

- **开发目录**:`F:\WorldBox New Mod\AncientWarfare3.0`(只在 F 盘开发,**不碰 D 盘部署目录、不做 D 盘 dll 验证、不管两盘同步**)。
- **构建**:`& "C:\Program Files\dotnet\dotnet.exe" build`(dotnet 不在 PATH,用全路径)。**F 盘 build 0 警 0 错即合格**。
- **反编译查 API 真相**(只读引用,允许):
  - dll:`F:\WorldBox New Mod\AssemblyPublicizer\publicized_assemblies\Assembly-CSharp_publicized.dll`
  - 工具:`%USERPROFILE%\.dotnet\tools\ilspycmd.exe`,**必须 `export DOTNET_ROLL_FORWARD=Major`**(它目标 net8,本机 net9)。
  - 用法:`ilspycmd -t <TypeName> <dll>`。
- **真相优先级**:NML 现场 dll > F 盘 publicized dll > AssetRipper 源码 > ilspycmd(混淆不可信)。
- 运行时日志:游戏 Player.log。排查 mod 没加载先 grep `Failed to compile mod` / `error CS`(=现场编译失败,非运行崩)。

---

## 1. 总体架构

AW3.0 = 在**新版 WorldBox 上重构的新模组**(不从 AW2 移植,但参考 AW2 设计)。核心系统:

1. **夏朝种族(Xia)**:`content/Xia*.cs`。clone `$civ_advanced_unit$` 注册,逐帧 png 贴图(`actors/species/civs/Xia/`),专属建筑/船/物品/特质/王国/年号。
2. **姓族谱系系统**(核心):姓(family)/氏(clan)/氏支(shi branch)/贵族衰落/称王分封。`core/lineage/` + `core/db/`。SQLite 随存档持久化。
3. **继承人系统**:`HeirService` + `AW_HeirPatch`。kingdom.data 存 heir_id,Harmony 接管继位。unit_heir 皮肤 + minimap 图标。
4. **历史人物降临**:`content/figures/`。姬发/嬴政/刘邦/曹丕/司马炎,严格顺序 + 持久化。
5. **编年史 / 历史系统**:`HistoryWriter`/`HistoryQuery`/`ChronicleEvents` + 三事件表 + 王国档案表。人物传记/国家历史/城市易主 + 万国史(全王国列表)+ 朝代分段。
6. **UI**:NML `AbstractWindow`/`AbstractListWindow` + 自定义 tab(`AW_LineageTab`,带神力按钮)。

---

## 2. SQLite 持久化机制(关键基础设施)

- `[TableDef("Name")]` 标在 `AbstractTableItem<T>` 子类 → `LineageArchiveManager` 反射**自动建表 + 自动迁移加列**(EnsureLoadedSchema/AddMissingColumns)。**加字段直接加 public 字段即可,自动迁移**。
- 列名 = 字段名**大写**(无 `[TableItemDef(Name)]`)。`[TableItemDef(pIsPrimary:true)]` 标主键;`[TableItemDef(pDefaultValue:"-1")]` 设默认。
- 写:`db.Insert(table, ColumnVal.Create("COL", val))` / `db.UpdateValue(table, constraints, cols)` / `db.CheckKeyExist(...)`。
- 读:`item.ReadFromReader(reader)`(反射回填,新字段自动读)。复杂 SELECT 用原生 `new SQLiteCommand(db)`。
- `OperatingDB` = `SQLiteConnection`(`LineageArchiveManager.Instance.OperatingDB`)。
- **持久化随存档**:`AW_SavePatch` 钩 saveWorldToDirectory(存)/ loadWorld(读)/ generateNewMap(新世界)。读档后跑 `XiaSubspeciesRepair.EnsureWorldTraits` + `FigureStateStore.Load` + `KingdomArchiveWriter.BackfillAll`。

### 表清单(`core/db/`)
| 表 | 内容 |
|---|---|
| ActorArchiveTableItem | 人物档案(姓氏/谱系/亲子/贵族距离/phenotype/founded_branch_shi/head/skin) |
| LineageGroupTableItem | 姓族(family 级) |
| ShiBranchTableItem | 氏支(含 origin_kingdom_id,称王分封判定用) |
| FamilyEdgeTableItem | 亲子边(死后家族树仍可绘) |
| KingdomLineageStateTableItem | 王国姓氏合流状态 |
| FigureStateTableItem | 历史人物生成状态(严格顺序 + 互斥) |
| PersonBiographyTableItem | 人物传记事件 |
| KingdomHistoryTableItem | 国家历史事件(found/rule_change/destroyed) |
| CityHistoryTableItem | 城市易主事件 |
| **KingdomArchiveTableItem** | **王国名册(全王国含亡国,旗帜重建快照)** ← 本批新增 |

---

## 3. 各系统要点

### 3.1 姓族谱系(`core/lineage/LineageService.cs` 为中枢)
- 出生:`OnActorBornWithParents`(applyParentsMeta 钩)→ `InheritFromParents`(双系继承:优先有谱系父系,退母系)→ `RecordFamilyEdges` → `ApplyDisplayName` → `ArchiveActor`。
- 命名 `ApplyDisplayName`:合流前 贵族男=氏+名 / 贵族女=名+姓 / 平民奴隶=单名;合流后统一氏+名。**写回真名 `setName`**。
- 晋升 `OnActorPromoted`(成王/城主/名人):距离归零、加 guizu 特质、status=noble。
- 贵族衰落:`NOBLE_DISTANCE >= 3`(NOBLE_DECAY_DISTANCE)→ 退平民,移 guizu。
- **称王分封 `OnKingFoundBranch`**:称王且**新王国 ≠ 其氏支 origin_kingdom_id**(=建新国/夺别国)→ 脱离原氏支+原版 clan,开新氏支(KING_FOUNDED),改氏名(同步 ApplyDisplayName),距离归零,标记 FOUNDED_BRANCH_SHI_ID。本国内继位不触发。由 `AW_HeirPatch.SetKing_Postfix` 调用。

### 3.2 继承人(`HeirService` + `AW_HeirPatch`)
- `GetHeir`:现任合格(活/非王/成年/非疯)则**保持稳定**,失格才重选(修过"已存在还被重选"bug)。
- 选择:优先国王成年子女(男性优先),退 royal_clan。
- `IS_HEIR` 标记(actor.data,bool):RefreshHeir/GetHeir/ClearHeir 维护(旧清新设)。供 unit_heir 皮肤(`XiaTexturePatch`)+ minimap 图标(`AW_HeirMinimapPatch`)。
- 继位接管:`AW_HeirPatch.GetKingFromRoyalClan_Prefix`(有继承人 return false 接管)。

### 3.3 历史人物(`content/figures/`)
- 5 人严格顺序(姬发→嬴政→刘邦→曹丕→司马炎),`FigureStateStore` 持久化(spawned/dead/actor_id),存活互斥。
- 姬发 chance 0.8 + RequiresIntegration=false;后 4 人 0.005 + 需姓氏合流(合流系统未做→暂卡)。
- 钩点:`AW_FigurePatch` 钩 `newCreature`(神力/城市补人)+ `applyParentsMeta`(繁殖)→ `TrySpawnOn`。
- 成王套国名(周/秦/汉/魏/晋),minimap_figure 图标,成王/城主后图标不画。

### 3.4 编年史 / 历史(本批重点扩展)
- 写:`ChronicleEvents`(判断要不要记 + 记什么)→ `HistoryWriter`(拼"通用年+年号"前缀快照 + 自增 event_id + 落库)。
- 钩点:`AW_ChroniclePatch`(建国 newCivKingdom / 亡国 KingdomManager.removeObject / 城市易主 City.setKingdom)+ `AW_FigurePatch.SetKing_Postfix`(换君 OnKingChanged)。
- 读:`HistoryQuery`(ReadPerson/Kingdom/City + **GetAllKingdoms** + **GetKingdomReigns** 朝代分段)。
- **王国档案**:`KingdomArchiveWriter`(Upsert 建国/换君,MarkDestroyed 亡国,BackfillAll 读档补全)。`KingdomFlagBuilder` 从存档值重建旗帜(背景+图标+配色),**不引用活 Kingdom**,亡国安全。
- **朝代分段**:`GetKingdomReigns` 从 KingdomHistory 的 rule_change 事件切朝代;有王段=年号(纪年快照)+王名+起止年;无王段=时间区间"13-18年"。

---

## 4. UI 体系

- 自定义 tab `AW_LineageTab`(id "AW3Lineage",图标 iconXias):
  - 「姓族总览」按钮 → `LineageOverviewWindow`
  - **「万国史」按钮 → `KingdomRosterWindow`**(本批新增,全王国列表)
  - 「生成 Xia」神力 + 「历史人物开关」toggle
- 窗口(`AbstractWindow`/`AbstractListWindow`,id 集中在 `AW_LineageWindowIds`,标题在 `Locales/others.csv` 的 `{id} Title`):
  - `LineageOverviewWindow`(姓族总览)→ `ShiBranchListWindow`(氏支列表)→ `FamilyTreeWindow`(家族树/氏族大树双模式)
  - `HistoryListWindow`(编年史,人物/城市平铺,**国家史朝代分段折叠**)
  - `KingdomRosterWindow`(万国史,旗帜+国名国家色+亡国标记,点进国家史)
- 右 tab 入口按钮(`Find("Tabs Right")`,兜底):
  - unit 窗:家族树按钮 + 人物传记按钮(`AW_UnitTabPatch`)
  - kingdom 窗:本国历史按钮(`AW_KingdomTabPatch`)
  - city 窗:城市历史按钮(`AW_CityTabPatch`)
- 家族树节点 `FamilyTreeNodeView`:活人用 UiUnitAvatarElement;**死者脱离该控件,用独立 Image 自合成静态 Xia sprite + 存档 phenotype**(见坑 5.4)。氏族大树懒加载+自动折叠(折叠不查 SQL)。

---

## 5. 关键坑(踩过的,务必牢记)

1. **Harmony 继承方法陷阱**:`[HarmonyPatch(typeof(X), "method")]` 的 typeof **必须是方法实际声明/override 的类**。钩继承自基类但 X 未 override 的方法 → GetMethod 返 null → "Patching exception in method null" → 整个 PatchAll 失败 → **全 mod 被禁用**。例:KingdomWindow 没 override startShowingWindow(在 WindowMetaGeneric),要钩 showTopPartInformation;tryToShowActor 在 StatsWindow 声明,typeof 必须 StatsWindow。

2. **mod 晚于 post_init**:mod 在 OnModLoad clone 资产,晚于游戏 post_init。运行时字段(atlas_asset/texture_asset/check_flip)为 null 需手动重建。建筑要 `atlas_asset = dynamic_sprites_library.get(atlas_id)`;船要 `has_sprite_renderer=false`。**绝不在 init 预加载 getSpriteList**(毒化 SpriteTextureLoader 空缓存)。

3. **birth_rate 整数坑**:`birth_rate` 是整数额外子女数,`(int)stats["birth_rate"]` 取整,小数 0.35 丢成 0。种族 genome 必须显式设整数 birth_rate。且 clone `$civ_advanced_unit$` 缺人类繁殖 subspecies trait(reproduction_strategy_viviparity + reproduction_sexual),没有就**不能繁殖**(人口只靠 AutoCivilization.spawnUnits 补,不计 births)。

4. **死者画像"僵尸"双根因**:① 复用 `UiUnitAvatarElement` 但死者 `getActor()==null`,该控件是 IRefreshElement 被框架刷新 + tooltip 走 _actor → 画像消失/出错;② phenotype_index=0 对 Xia 上成僵尸绿。**修复=死者完全脱离该控件**(`_avatar.enabled=false`),用独立 Image 自合成:`getContainerForUI → walking.frames[0] → getColoredSprite`(用存档 phenotype)。判活必须 `!isRekt() && isAlive()`(否则走 loader.showDied() 显示 _died_sprite 墓碑)。

5. **actor.data int/long 取值坑**:`set(key, int)` 必须 `get<int>` 读;用 `get<long>` 类型失配**静默返默认值**。NOBLE_DISTANCE 是 int(`dist+1`)→ 必须 get<int>(曾致活人 tooltip 距贵族代数永不显示)。SHI_ID/LINEAGE_ID/FOUNDED_BRANCH_SHI_ID 是 long。

6. **随机播种坑**:UnityEngine.Random 被世界生成 MapBox 固定播种,每新档同序列致氏名恒"慕容"。mod 随机一律用**私有 System.Random**。

7. **NML 窗口动画闸门**:NML 自定义窗首次打不开(create(true) 残留 hide-tween 填 _animations_list,被 isAnimationActive 闸门跳过)。用 `AW_LineageWindowIds.SafeShow`(内含 finishAnimations)。

8. **locale CSV 逗号坑**:`Locales/*.csv` 某数据行半角逗号数 > 列头列数(4)就抛异常**丢弃整个文件**。中文字段内逗号必须全角。校验:`awk -F',' 'NR>1 && NF!=4'`。

9. **toggle 按钮本地化键**:`CreateToggleButton` 的 tooltip 描述键 = `GodPower.getDescriptionID` = 下划线 `{name}_description`,非空格。

10. **武器在手 pivot**:mod 散 PNG 的 pivot 被 NML 硬编码(0.5,0.5)致武器上浮。贴图同目录放 sprites.json 设 PivotX/Y,PixelsPerUnit=1。

---

## 6. 本会话批次完成内容

### 批次一(综合修复)
- 贵族女性姓名顺序(AW_BabyNamePatch,性别定后重拼)
- 平民文本"平族"→"平民"
- 继承人稳定性(不乱换)
- 死人画像重建(脱离 UiUnitAvatarElement + 存档 phenotype)
- 小树上溯查父母(父母画在本人上方 + 占位节点不断链)
- 距贵族N代 tooltip 活人也显示(int/long 修复)
- 继承人 UI 位置+文字标签 / unit_heir 皮肤 / minimap_heir 图标
- 氏族大树懒加载+自动折叠(折叠不查 SQL,全死/无 king-leader-heir 自动折叠)
- 称王分封原树标注"建支:X氏"+点击跳新支
- 平民不进氏族大树(只家庭树上溯可见)
- 编年史后端三表 + HistoryQuery + HistoryListWindow + 三右tab入口

### 批次二(本批:历史功能 + figure 修复)
- **历史人物自然产生修复**:放宽 baby 路径 `isAlive()` guard(改 `!isRekt()`,因 applyParentsMeta 时 baby 可能瞬时未 alive);ReconcileAliveState 用 isRekt() 而非 !isAlive()(防新生 figure 被误判死)。**保留 `[FigureDiag]` 诊断日志**(`DiagnoseSpawn=true`)。
- **KingdomArchive 表 + Writer + 旗帜重建**:全王国(含亡国)名册;`KingdomFlagBuilder` 存档值重建旗帜规避空引;ChronicleEvents 接入(建国/换君 Upsert,亡国 MarkDestroyed);读档 BackfillAll。
- **查询层**:GetAllKingdoms / GetKingdomReigns(朝代分段:rule_change 切朝代,有王=年号+纪年,无王=时间区间)。
- **UI**:KingdomRosterWindow(万国史,旗帜+国名国家色+亡国标记)+ KingdomRosterListItem;HistoryListWindow 改朝代分段折叠(HistoryRow 统一行,段头可展开收缩);万国史按钮放 AW_LineageTab(神力 tab);三 tab 图标换 AW2 风格(iconDocument/iconKingdomList/iconVillages)。

---

## 7. 待测试 / 已知风险(进游戏验证)

1. **历史人物自然产生**:城市自然繁殖一会,看 figure 是否降临 + Player.log 的 `[FigureDiag]` 行(若仍不出,log 会显示卡在哪个 guard,据此微调;诊断未关,定位后把 `HistoricalFigureService.DiagnoseSpawn=false`)。**根因未 100% 确证**——所有钩路径理论已覆盖,本批按最高置信(baby isAlive guard)修复 + 留诊断。
2. **万国史**:AW_LineageTab(神力 tab)有「万国史」按钮 → 列出所有国(含亡国);亡国旗帜正常重建不崩、国名用国家色、亡国标[已亡]。
3. **朝代分段**:点国进去,有王段显「年号纪年·王名·起止年」、无王段显「无王时期·时间区间」,可点段头展开/收缩。
4. **kingdom/city 窗 "Tabs Right" 存在性**:运行时核实(不存在则按钮不显示,不崩)。
5. **老存档**:KingdomArchive 靠 BackfillAll 补全现有活国;**亡国若在本更新前就亡了,无档案行**(无法回溯,可接受)。
6. **朝代分段依赖 rule_change 事件**:若某国从未记过换君(如建国即玩家删),只有 found 段。

---

## 8. 文件清单(`Code/` 共 89 个 .cs)

- `content/`:Xia 种族全家桶 + figures(历史人物)
- `core/db/`:10 张表 + LineageArchiveManager + SQLiteHelper(在 utils)
- `core/lineage/`:LineageService(中枢)/Query/Writer/Reader/DTO/Keys/IdAllocator/NamePool + Heir/YearName/KingdomTitle/History/ChronicleEvents/KingdomArchiveWriter/KingdomFlagBuilder
- `patch/`:24 个 Harmony patch(出生/死亡/晋升/继承人/编年史/figure/存档/UI tab/防护)
- `ui/`:AW_LineageTab + WindowIds + RawTooltip + windows/(6 窗)+ items/(8 项)
- `Locales/others.csv`:所有本地化键

## 9. 内存索引(`C:\Users\24908\.claude\projects\F--Codex-AncientWarfare2-0-main\memory\MEMORY.md`)
关键坑都已存为 memory 文件(aw3-* 前缀),接手前通读 MEMORY.md 索引。

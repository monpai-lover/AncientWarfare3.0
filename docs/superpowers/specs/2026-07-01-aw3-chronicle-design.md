# AW3.0 编年史系统(人物传记 / 国家历史 / 城市易主)— 设计

> 日期:2026-07-01
> 范围:**本期只做记录后端 + 随存档持久化 + 最少事件接入**;不做查看窗口(查看按钮以后放各窗口右侧)。

## Context(为什么做)
游戏里发生的事(谁出生死亡、谁建国换君亡国、城市易主)目前不留痕。要建三类**编年史**:
1. **人物传记** —— 仅"入谱贵族家系"(IsXia 且有 lineage_id)的人有,记其生平。
2. **国家历史** —— 记王国大事。
3. **城市历史** —— 记城市易主(换所属王国)。

统一以"年份 + 内容"逐条记载,年份格式 = **通用年 + 国家年号纪年在前,内容在后**;无年号时只显通用年。复用现有 SQLite 档案库(随存档持久化)、年号系统(YearNameService)、Date 基建。

## 已核实可复用的基建
- **SQLite 反射建表**:加一个 `[TableDef]` 的 `AbstractTableItem<T>` 子类即随存档自动建表/迁移/持久化(同 ActorArchive/FamilyEdge;`LineageArchiveManager` 反射扫描)。读档迁移走 `EnsureLoadedSchema`,新增列自动 ALTER。
- **年号**:`YearNameService.GetYearName(Kingdom)` → "鲁伯姬元年"/""(无年号空)。
- **通用年**:`Date.getYear(double worldTime)` / `Date.getCurrentYear()`;`World.world.getCurWorldTime()` 取当前世界时间。
- **现有钩子**:`Actor.die`(AW_ActorDeathPatch)、`Kingdom.setKing`(AW_FigurePatch)、`LineageService.OnActorBornWithParents`/ApplyFigure(出生/历史人物)。
- **新增钩点**(已确认存在):`Kingdom.newCivKingdom(Actor)`(Kingdom.cs:443,建国)、`KingdomManager.removeObject(Kingdom)`(:43,亡国)、`City.setKingdom(Kingdom, bool pFromLoad)`(City.cs:501,易主)。

## 数据模型:三张独立表(用户定)

三张表**共享核心列**,各自一个关联列。均 `[TableDef]` 随存档持久化。

共享列:
- `event_id`(`long`,主键)— 由 `HistoryIdAllocator` 自增分配(单调,跨表唯一或各表独立均可;采用各表独立自增器,初值取表内 MAX+1)。
- `world_time`(`double`)— 排序键 + 通用年来源(`Date.getYear(world_time)`)。
- `year_prefix`(`string`)— **写入当时拼好的快照文本**,如 `"16年 周武王元年"` 或无年号 `"16年"`。**快照**避免日后改年号导致旧事件年份显示错乱。
- `subject_name`(`string`)— 事件发生时主体名快照(人名/国名),日后改名旧事件仍显当时名。
- `content`(`string`)— 事件内容文本(中文,逗号用全角避免 locale 无关但保持一致)。
- `event_type`(`string`)— 子类型枚举字符串(birth/death/become_king/found/rename/rule_change/destroyed/city_transfer),便于过滤/将来配图标。

各表特有关联列:
- **PersonBiographyTableItem** `[TableDef("PersonBiography")]`:`actor_id`(`long`,关联 ActorArchive.id)。
- **KingdomHistoryTableItem** `[TableDef("KingdomHistory")]`:`kingdom_id`(`long`)。
- **CityHistoryTableItem** `[TableDef("CityHistory")]`:`city_id`(`long`)。

> 注:三表字段几乎相同但**有意不抽象成一张通用表**(用户选三独立表)。共享列的拼装逻辑由 `HistoryWriter` 统一,避免代码重复。

## 组件

### 1. 三个 TableItem 类(`Code/core/db/`)
PersonBiographyTableItem / KingdomHistoryTableItem / CityHistoryTableItem,字段如上。`[TableItemDef(pIsPrimary:true)] long event_id`。

### 2. `HistoryWriter`(`Code/core/lineage/HistoryWriter.cs`)— 统一写事件
公开方法:
- `RecordPerson(long actorId, Kingdom contextKingdom, string subjectName, string eventType, string content)`
- `RecordKingdom(Kingdom kingdom, string eventType, string content)`
- `RecordCity(City city, Kingdom contextKingdom, string eventType, string content)`

内部统一:
- `double t = World.world.getCurWorldTime();`
- `year_prefix = BuildYearPrefix(t, contextKingdom)`:= `Date.getYear(t) + "年"` + (年号非空 ? `" " + yearName` : "")。
- 分配 `event_id`,INSERT 到对应表(走 `LineageArchiveManager.Instance.OperatingDB` + SQLiteHelper.Insert;DB null 则静默跳过+LogWarning,不崩)。

`BuildYearPrefix(double t, Kingdom k)`:年号取 `k != null ? YearNameService.GetYearName(k) : ""`。

### 3. 事件接入(patch / 现有 service 加行)

| 事件 | 接入点 | 主体/年号来源 | 防重复 |
|---|---|---|---|
| 人物·出生 | `LineageService.OnActorBornWithParents` 末尾 + 历史人物 `ApplyFigure` | actor 的 kingdom | 一次性,天然不重 |
| 人物·死亡 | `AW_ActorDeathPatch`(Actor.die)归档处 | actor 的 kingdom | 一次性 |
| 人物·成为国王 | `Kingdom.setKing` Postfix(AW_FigurePatch 已钩) | 该 kingdom | 新王==旧王跳过 |
| 国家·建国 | `Kingdom.newCivKingdom` Postfix(**新**) | 该 kingdom | 一次性 |
| 国家·换君 | `Kingdom.setKing` Postfix(同人物成王,一钩记两表) | 该 kingdom | 同上 |
| 国家·亡国 | `KingdomManager.removeObject` Postfix(**新**) | 该 kingdom | 一次性 |
| 城市·易主 | `City.setKingdom(Kingdom,bool)` Postfix(**新**) | 城市新所属国 | 见下 |

**人物事件范围**:写入前判 `LineageService.IsXia(actor) && actor.data.get(LINEAGE_ID)>=0`(仅入谱贵族家系;无谱系/非 Xia 不记)。

**防重复规则**:
- `City.setKingdom`:`pFromLoad==true`(读档回填)**不记**;新国 == 旧国**不记**(无变化);旧国为 null(初次归属)按"建城/初属"记或跳过——本期**跳过初次归属**,只记真正"易主"(旧国非空且 != 新国)。
- `Kingdom.setKing`:新王 == 当前 king **不记**。
- 国家·改名事件**本期不做**(用户定:setName 钩点杂、易误触,先只保留建国/换君/亡国)。

### 4. 注册与持久化
- 三 TableItem 类带 `[TableDef]` → `LineageArchiveManager` 反射自动建表,无需手写注册。
- 读档/新世界后无需特殊加载(写事件是 append-only,不需内存缓存;查询留待将来做查看 UI)。

## 不在本期范围
- **查看窗口**(人物传记窗/国史窗/城史窗)——以后做,按钮统一放各窗口**右侧**(unit 窗 Tabs Right / kingdom 窗 / city 窗)。
- 国家**改名**事件。
- 事件**编辑/删除**、富文本、图标。

## 验证
1. F 盘 `dotnet build`(DOTNET_ROLL_FORWARD=Major)0 错 0 警。
2. 进游戏 + 看 DB / log:
   - 入谱贵族出生/死亡 → PersonBiography 有行,`year_prefix` = "N年 X元年" 或 "N年"。
   - 某人建国/成王 → PersonBiography(成王)+ KingdomHistory(建国/换君)各有行。
   - 城市被攻陷换国 → CityHistory 有"易主"行,读档不产生假易主、同国不重复。
   - 亡国 → KingdomHistory 有亡国行。
   - 改年号后新事件 prefix 用新年号,旧事件 prefix 仍是当时快照(不被改写)。
3. 用临时 log 打印每条写入的 `(表, year_prefix, content)` 便于肉眼核对(验证后可降级/移除)。

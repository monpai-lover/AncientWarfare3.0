# AW3.0 编年史系统(人物传记 / 国家历史 / 城市易主)实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给 AW3.0 加三类随存档持久化的编年史记录(入谱贵族人物传记 / 国家历史 / 城市易主),每条事件带「通用年 + 国家年号」前缀,本期只做记录后端不做查看窗口。

**Architecture:** 三张独立 SQLite `[TableDef]` 表(LineageArchiveManager 反射自动建表/持久化)+ 一个 `HistoryWriter` 统一拼年份前缀并写库 + 在现有/新增 Harmony 钩点处调 HistoryWriter 记事件。

**Tech Stack:** C# .NET Framework 4.8、HarmonyLib、NeoModLoader、System.Data.SQLite;复用现有 `AbstractTableItem<T>`/`LineageArchiveManager`/`YearNameService`/`Date`。

## Global Constraints

- **net48,禁用 file-scoped namespace / record / 顶级 using**:一律传统 `namespace X { }`(见记忆 aw3-ui-api)。
- **无单元测试框架**:验证 = `dotnet build` 0 警 0 错 + 进游戏看 DB/log。每个 Task 的"测试"步骤是 build + 必要时加临时 LogInfo 肉眼核对。
- **build 命令**:`$env:DOTNET_ROLL_FORWARD="Major"; & "C:\Program Files\dotnet\dotnet.exe" build`(在 `F:\WorldBox New Mod\AncientWarfare3.0`)。**只管 F 盘**(记忆 aw3-only-f-drive)。
- **SQLite 列名规则**:`AbstractTableItem` 字段无 `[TableItemDef(Name)]` 时列名 = **字段名大写**。写入用 `db.Insert(Table, ColumnVal.Create("COL_UPPER", val), ...)`,Table = `XxxTableItem.GetTableName()`。
- **DB 不可用守卫**:`LineageArchiveManager.Instance.OperatingDB` 可能为 null → 写入前判 null,null 则 `ModClass.LogWarning(...)` 并 return,**不崩**。
- **中文内容标点**:事件 content 里若有标点用全角(与 locale csv 一致习惯,避免混淆)。
- **不提交 git**:除非用户明确要求(本项目惯例:build 验证即可,提交由用户决定)。

## 文件结构

- 新建 `Code/core/db/PersonBiographyTableItem.cs` — 人物传记表定义。
- 新建 `Code/core/db/KingdomHistoryTableItem.cs` — 国家历史表定义。
- 新建 `Code/core/db/CityHistoryTableItem.cs` — 城市历史表定义。
- 新建 `Code/core/lineage/HistoryWriter.cs` — 统一写事件(拼年份前缀 + 自增 event_id + Insert)。
- 新建 `Code/patch/AW_ChroniclePatch.cs` — 新增钩点(建国 newCivKingdom、亡国 removeObject、城市易主 City.setKingdom)。
- 修改 `Code/core/lineage/LineageService.cs` — OnActorBornWithParents 末尾记出生。
- 修改 `Code/patch/AW_ActorDeathPatch.cs` — 死亡处记死亡。
- 修改 `Code/patch/AW_FigurePatch.cs` — setKing Postfix 记「成王」(人物)+「换君」(国家)。
- 修改 `Code/content/figures/HistoricalFigureService.cs` — ApplyFigure 记历史人物出生。

---

### Task 1: 三张表定义

**Files:**
- Create: `Code/core/db/PersonBiographyTableItem.cs`
- Create: `Code/core/db/KingdomHistoryTableItem.cs`
- Create: `Code/core/db/CityHistoryTableItem.cs`

**Interfaces:**
- Produces:
  - `PersonBiographyTableItem`(`[TableDef("PersonBiography")]`,主键 `long event_id`,关联 `long actor_id`)
  - `KingdomHistoryTableItem`(`[TableDef("KingdomHistory")]`,主键 `long event_id`,关联 `long kingdom_id`)
  - `CityHistoryTableItem`(`[TableDef("CityHistory")]`,主键 `long event_id`,关联 `long city_id`)
  - 三者均含共享列字段:`long event_id` / `double world_time` / `string year_prefix` / `string subject_name` / `string content` / `string event_type`
  - `GetTableName()`(继承自 AbstractTableItem)返回 "PersonBiography"/"KingdomHistory"/"CityHistory"

- [ ] **Step 1: 写 PersonBiographyTableItem.cs**

```csharp
using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    /// <summary>
    ///     人物传记事件表。仅"入谱贵族家系"(IsXia 且 lineage_id>=0)的人有事件。
    ///     一条 = 一次生平事件(出生/死亡/成为国王)。随存档持久化([TableDef] 反射自动建表)。
    /// </summary>
    [TableDef("PersonBiography")]
    public class PersonBiographyTableItem : AbstractTableItem<PersonBiographyTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long event_id;

        public long   actor_id = -1;     // 传记主人(关联 ActorArchive.id)
        public double world_time;        // 排序 + 通用年来源
        public string year_prefix;       // 写入当时拼好的快照,如 "16年 周武王元年" / "16年"
        public string subject_name;      // 事件发生时人名快照
        public string content;           // 事件内容
        public string event_type;        // birth / death / become_king
    }
}
```

- [ ] **Step 2: 写 KingdomHistoryTableItem.cs**

```csharp
using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    /// <summary>
    ///     国家历史事件表(建国/换君/亡国)。随存档持久化([TableDef] 反射自动建表)。
    /// </summary>
    [TableDef("KingdomHistory")]
    public class KingdomHistoryTableItem : AbstractTableItem<KingdomHistoryTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long event_id;

        public long   kingdom_id = -1;   // 关联 Kingdom.id
        public double world_time;
        public string year_prefix;
        public string subject_name;      // 事件发生时国名快照
        public string content;
        public string event_type;        // found / rule_change / destroyed
    }
}
```

- [ ] **Step 3: 写 CityHistoryTableItem.cs**

```csharp
using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    /// <summary>
    ///     城市历史事件表(易主:换所属王国)。随存档持久化([TableDef] 反射自动建表)。
    /// </summary>
    [TableDef("CityHistory")]
    public class CityHistoryTableItem : AbstractTableItem<CityHistoryTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long event_id;

        public long   city_id = -1;      // 关联 City.id
        public double world_time;
        public string year_prefix;
        public string subject_name;      // 事件发生时城名快照
        public string content;
        public string event_type;        // city_transfer
    }
}
```

- [ ] **Step 4: build 验证表能反射建立**

Run: `$env:DOTNET_ROLL_FORWARD="Major"; & "C:\Program Files\dotnet\dotnet.exe" build`(在 `F:\WorldBox New Mod\AncientWarfare3.0`)
Expected: `0 个警告 0 个错误`。
(三类带 `[TableDef]` 的 `AbstractTableItem<T>` 子类会被 LineageArchiveManager 反射自动建表,无需手写注册。build 通过即结构正确。)

---

### Task 2: HistoryWriter 统一写事件

**Files:**
- Create: `Code/core/lineage/HistoryWriter.cs`

**Interfaces:**
- Consumes:
  - Task 1 的三个 TableItem 的 `GetTableName()`。
  - `LineageArchiveManager.Instance.OperatingDB`(SQLiteHelper,有 `.Insert(table, params ColumnVal[])`)。
  - `ColumnVal.Create(string col, object val)`。
  - `World.world.getCurWorldTime()`(double)、`Date.getYear(double)`(int)、`YearNameService.GetYearName(Kingdom)`(string,无年号返 "")。
- Produces:
  - `HistoryWriter.RecordPerson(long pActorId, Kingdom pContextKingdom, string pSubjectName, string pEventType, string pContent)`
  - `HistoryWriter.RecordKingdom(Kingdom pKingdom, string pEventType, string pContent)`
  - `HistoryWriter.RecordCity(City pCity, Kingdom pContextKingdom, string pEventType, string pContent)`
  - `HistoryWriter.BuildYearPrefix(double pTime, Kingdom pKingdom)`(string,internal,供测试/复用)

- [ ] **Step 1: 写 HistoryWriter.cs(年份前缀 + 自增 event_id + 三个 Record 方法)**

```csharp
using System;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     编年史统一写入:拼「通用年 + 国家年号」前缀快照,分配自增 event_id,写对应表。
    ///     年份前缀格式:"{Date.getYear}年" + (年号非空 ? " {年号}" : "")。无年号只显通用年。
    ///     前缀写入当时快照 → 日后改年号不影响旧事件显示。
    ///     DB 不可用(OperatingDB==null)则静默跳过 + LogWarning,不崩。
    /// </summary>
    public static class HistoryWriter
    {
        public static void RecordPerson(long pActorId, Kingdom pContextKingdom,
            string pSubjectName, string pEventType, string pContent)
        {
            Insert(PersonBiographyTableItem.GetTableName(), pContextKingdom, pEventType, pContent, pSubjectName,
                ColumnVal.Create("ACTOR_ID", pActorId));
        }

        public static void RecordKingdom(Kingdom pKingdom, string pEventType, string pContent)
        {
            if (pKingdom == null) return;
            Insert(KingdomHistoryTableItem.GetTableName(), pKingdom, pEventType, pContent, pKingdom.name,
                ColumnVal.Create("KINGDOM_ID", pKingdom.id));
        }

        public static void RecordCity(City pCity, Kingdom pContextKingdom, string pEventType, string pContent)
        {
            if (pCity == null) return;
            Insert(CityHistoryTableItem.GetTableName(), pContextKingdom, pEventType, pContent, pCity.data.name,
                ColumnVal.Create("CITY_ID", pCity.id));
        }

        internal static string BuildYearPrefix(double pTime, Kingdom pKingdom)
        {
            string year = Date.getYear(pTime) + "年";
            string era = pKingdom != null ? YearNameService.GetYearName(pKingdom) : "";
            return string.IsNullOrEmpty(era) ? year : year + " " + era;
        }

        // 公共写入:取时间、拼前缀、分配 event_id、Insert(关联列由调用方传 pKeyCol)。
        private static void Insert(string pTable, Kingdom pContextKingdom,
            string pEventType, string pContent, string pSubjectName, ColumnVal pKeyCol)
        {
            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null)
            {
                ModClass.LogWarning("HistoryWriter: DB 不可用,事件未记录(" + pTable + "/" + pEventType + ")");
                return;
            }

            double t = World.world.getCurWorldTime();
            string prefix = BuildYearPrefix(t, pContextKingdom);
            long eventId = NextEventId(db, pTable);

            try
            {
                db.Insert(pTable,
                    ColumnVal.Create("EVENT_ID", eventId),
                    pKeyCol,
                    ColumnVal.Create("WORLD_TIME", t),
                    ColumnVal.Create("YEAR_PREFIX", prefix ?? ""),
                    ColumnVal.Create("SUBJECT_NAME", pSubjectName ?? ""),
                    ColumnVal.Create("CONTENT", pContent ?? ""),
                    ColumnVal.Create("EVENT_TYPE", pEventType ?? ""));
            }
            catch (Exception e)
            {
                ModClass.LogWarning("HistoryWriter.Insert 失败(" + pTable + "):" + e.Message);
            }
        }

        // 自增 event_id:取表内 MAX(EVENT_ID)+1。空表返回 1。原生 SQLiteCommand(同 FigureStateStore.Load 模式)。
        private static long NextEventId(SQLiteConnection pDb, string pTable)
        {
            try
            {
                using var cmd = new SQLiteCommand(pDb);
                cmd.CommandText = "SELECT IFNULL(MAX(EVENT_ID), 0) FROM " + pTable;
                object result = cmd.ExecuteScalar();
                long max = (result == null || result == DBNull.Value) ? 0L : Convert.ToInt64(result);
                return max + 1;
            }
            catch
            {
                return 1; // 表尚未建立/异常 → 从 1 起(极早期不会走到写入)
            }
        }
    }
}
```

- [ ] **Step 2: 确认 OperatingDB 类型 = SQLiteConnection**

读 `Code/core/db/LineageArchiveManager.cs`,搜 `OperatingDB` 属性声明,确认其类型是 `SQLiteConnection`(NextEventId 的参数类型要匹配)。
- 若类型是 `SQLiteHelper` 包装而非裸 `SQLiteConnection`:把 `NextEventId(SQLiteConnection, ...)` 改为接受该包装类型,并用包装暴露的连接(如 `db.connection` 或 `db.GetConnection()`)构造 `SQLiteCommand`。在 FigureStateStore.Load(`Code/core/db/FigureStateTableItem.cs:56-63`)里它怎么拿 connection 构造 `new SQLiteCommand(db)` 就照抄那种用法(那里 `db` 直接传给了 `new SQLiteCommand(db)`,说明 OperatingDB 可直接当 connection 用 → 维持现写法)。

- [ ] **Step 3: build 验证**

Run: build 命令(同 Task 1 Step 4)
Expected: `0 个警告 0 个错误`。若 `new SQLiteCommand(db)` 类型不匹配报错,按 Step 2 调整。

---

### Task 3: 人物·出生 + 历史人物出生事件

**Files:**
- Modify: `Code/core/lineage/LineageService.cs`(OnActorBornWithParents,约 :48-57)
- Modify: `Code/content/figures/HistoricalFigureService.cs`(ApplyFigure,约 :192 后)

**Interfaces:**
- Consumes: `HistoryWriter.RecordPerson(actorId, kingdom, subjectName, "birth", content)`;`LineageService.IsXia(Actor)`;`LineageKeys.LINEAGE_ID`。
- Produces: 入谱贵族出生时 PersonBiography 增一条 birth 事件。

- [ ] **Step 1: 在 OnActorBornWithParents 末尾记出生(仅入谱贵族)**

打开 `Code/core/lineage/LineageService.cs`,找到 `OnActorBornWithParents` 方法末尾的 `ArchiveActor(pBaby, pAlive: true);`(约 :56),在其**后面**加:

```csharp
            ArchiveActor(pBaby, pAlive: true);

            // 编年史:仅入谱贵族(有 lineage_id)记出生事件。
            RecordBirthEvent(pBaby);
        }
```

并在 LineageService 类内(ArchiveActor 附近)新增私有方法:

```csharp
        /// <summary>入谱贵族出生 → PersonBiography 记一条 birth 事件(无谱系者不记)。</summary>
        private static void RecordBirthEvent(Actor pActor)
        {
            if (pActor?.data == null || !IsXia(pActor)) return;
            pActor.data.get(LineageKeys.LINEAGE_ID, out long lid, -1L);
            if (lid < 0) return; // 仅入谱贵族家系

            string name = pActor.getName();
            HistoryWriter.RecordPerson(pActor.data.id, pActor.kingdom, name, "birth", name + " 出生");
        }
```

- [ ] **Step 2: 历史人物降临也记出生(ApplyFigure)**

打开 `Code/content/figures/HistoricalFigureService.cs`,在 `ApplyFigure` 方法里 `AnnounceFigure(pActor);`(约 :192)**之后**加:

```csharp
            AnnounceFigure(pActor);

            // 编年史:历史人物降临 = 一次"出生"事件(预设姓名已就绪)。
            core.lineage.HistoryWriter.RecordPerson(
                pActor.data.id, pActor.kingdom, pActor.getName(), "birth",
                pActor.getName() + " 降临世间");
```

- [ ] **Step 3: build 验证**

Run: build 命令
Expected: `0 个警告 0 个错误`。

- [ ] **Step 4: 临时 log 核对(可选)**

在 `HistoryWriter.Insert` 的 try 块成功后临时加 `ModClass.LogInfo("[编年史] " + pTable + " | " + prefix + " | " + pContent);`,进游戏生成夏人贵族,看 Player.log 是否出现 `[编年史] PersonBiography | N年 ... | XX 出生`。核对后删除该临时 log。

---

### Task 4: 人物·死亡事件

**Files:**
- Modify: `Code/patch/AW_ActorDeathPatch.cs`(Die_Prefix 末尾)

**Interfaces:**
- Consumes: `HistoryWriter.RecordPerson(...)`;`LineageService.IsXia`;`LineageKeys.LINEAGE_ID`。
- Produces: 入谱贵族死亡时 PersonBiography 增一条 death 事件。

- [ ] **Step 1: 在 Die_Prefix 的归档后记死亡**

打开 `Code/patch/AW_ActorDeathPatch.cs`,把末尾的:

```csharp
            if (!LineageService.IsXia(__instance)) return;

            LineageService.ArchiveActor(__instance, pAlive: false);
        }
```

改成:

```csharp
            if (!LineageService.IsXia(__instance)) return;

            LineageService.ArchiveActor(__instance, pAlive: false);

            // 编年史:仅入谱贵族记死亡事件(死亡前 kingdom/data 仍完整)。
            __instance.data.get(LineageKeys.LINEAGE_ID, out long lid, -1L);
            if (lid >= 0)
            {
                string name = __instance.getName();
                core.lineage.HistoryWriter.RecordPerson(
                    __instance.data.id, __instance.kingdom, name, "death", name + " 逝世");
            }
        }
```

(确保文件顶部 `using AncientWarfare3.core.lineage;` 已存在——现有文件已 import LineageService 所在命名空间,`LineageKeys` 同命名空间;若 `LineageKeys` 未在 using 范围,用全名 `core.lineage.LineageKeys.LINEAGE_ID`。)

- [ ] **Step 2: build 验证**

Run: build 命令
Expected: `0 个警告 0 个错误`。

---

### Task 5: 人物·成为国王 + 国家·换君事件

**Files:**
- Modify: `Code/patch/AW_FigurePatch.cs`(SetKing_Postfix)

**Interfaces:**
- Consumes: `HistoryWriter.RecordPerson(...)` / `HistoryWriter.RecordKingdom(...)`;`Kingdom.king`、`Kingdom.name`;`LineageService.IsXia`/`LineageKeys.LINEAGE_ID`。
- Produces: 新王就位时 PersonBiography(become_king,若入谱贵族)+ KingdomHistory(rule_change)各一条;同王重复不记。

- [ ] **Step 1: 在 SetKing_Postfix 里记成王/换君**

打开 `Code/patch/AW_FigurePatch.cs`,找到 `SetKing_Postfix`(约 :30):

```csharp
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.setKing))]
        public static void SetKing_Postfix(Kingdom __instance, Actor pActor)
        {
            if (__instance == null || pActor == null) return;
            HistoricalFigureService.OnFigureKingBecame(__instance, pActor);
        }
```

在 `HistoricalFigureService.OnFigureKingBecame(...)` **之后**加事件记录:

```csharp
            HistoricalFigureService.OnFigureKingBecame(__instance, pActor);

            // 编年史:换君(国家)+ 成王(人物,若入谱贵族)。
            core.lineage.ChronicleEvents.OnKingChanged(__instance, pActor);
```

- [ ] **Step 2: 把换君逻辑放进 ChronicleEvents(避免 patch 文件臃肿 + 防重复)**

新建 `Code/core/lineage/ChronicleEvents.cs`(本 Task 内创建),集中"需要判重/取数据"的事件转换逻辑:

```csharp
namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     把游戏钩子里的原始信号转成编年史事件(含防重复 / 仅入谱贵族 判断),
    ///     避免各 patch 文件塞业务逻辑。HistoryWriter 负责落库,本类负责"要不要记 + 记什么"。
    /// </summary>
    public static class ChronicleEvents
    {
        // setKing:新王就位 → 国家换君 + 人物成王。新王==旧记录则跳过(用 data 上的标记防同王重复)。
        public static void OnKingChanged(Kingdom pKingdom, Actor pNewKing)
        {
            if (pKingdom?.data == null || pNewKing?.data == null) return;

            // 防重复:记录上次为该国登记的王 id,相同则跳过。
            pKingdom.data.get(LineageKeys.CHRONICLE_LAST_KING_ID, out long lastKingId, -1L);
            if (lastKingId == pNewKing.data.id) return;
            pKingdom.data.set(LineageKeys.CHRONICLE_LAST_KING_ID, pNewKing.data.id);

            string kingName = pNewKing.getName();

            // 国家·换君
            HistoryWriter.RecordKingdom(pKingdom, "rule_change", kingName + " 即位为君");

            // 人物·成王(仅入谱贵族)
            if (LineageService.IsXia(pNewKing))
            {
                pNewKing.data.get(LineageKeys.LINEAGE_ID, out long lid, -1L);
                if (lid >= 0)
                    HistoryWriter.RecordPerson(pNewKing.data.id, pKingdom, kingName, "become_king",
                        kingName + " 即位为 " + pKingdom.name + " 之君");
            }
        }
    }
}
```

- [ ] **Step 3: 加防重复用的 LineageKeys 常量**

打开 `Code/core/lineage/LineageKeys.cs`,在 LineageKeys 类里加:

```csharp
        public const string CHRONICLE_LAST_KING_ID = "aw_chronicle_last_king"; // 编年史:该国上次登记的王 id(防同王重复记换君)
```

- [ ] **Step 4: build 验证**

Run: build 命令
Expected: `0 个警告 0 个错误`。

---

### Task 6: 国家·建国 + 亡国 + 城市·易主事件

**Files:**
- Create: `Code/patch/AW_ChroniclePatch.cs`
- Modify: `Code/core/lineage/ChronicleEvents.cs`(加 OnKingdomFounded / OnKingdomDestroyed / OnCityTransferred)

**Interfaces:**
- Consumes: `HistoryWriter.RecordKingdom/RecordCity`;新增钩点 `Kingdom.newCivKingdom(Actor)`、`KingdomManager.removeObject(Kingdom)`、`City.setKingdom(Kingdom, bool)`。
- Produces: 建国 → KingdomHistory(found);亡国 → KingdomHistory(destroyed);城市易主 → CityHistory(city_transfer)。读档回填/同国/初次归属不记。

- [ ] **Step 1: ChronicleEvents 加三个转换方法**

在 `Code/core/lineage/ChronicleEvents.cs` 的 ChronicleEvents 类里追加:

```csharp
        // 建国
        public static void OnKingdomFounded(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            HistoryWriter.RecordKingdom(pKingdom, "found", pKingdom.name + " 建立");
        }

        // 亡国
        public static void OnKingdomDestroyed(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            HistoryWriter.RecordKingdom(pKingdom, "destroyed", pKingdom.name + " 灭亡");
        }

        // 城市易主:仅当"旧国非空 且 旧国 != 新国"(真易主),且非读档回填。
        public static void OnCityTransferred(City pCity, Kingdom pOldKingdom, Kingdom pNewKingdom, bool pFromLoad)
        {
            if (pFromLoad) return;                                  // 读档回填不记
            if (pCity?.data == null) return;
            if (pOldKingdom == null) return;                        // 初次归属不记
            if (pNewKingdom == null) return;
            if (pOldKingdom == pNewKingdom) return;                 // 无变化不记

            string oldName = pOldKingdom.name;
            string newName = pNewKingdom.name;
            HistoryWriter.RecordCity(pCity, pNewKingdom, "city_transfer",
                pCity.data.name + " 由 " + oldName + " 易主至 " + newName);
        }
```

- [ ] **Step 2: 写 AW_ChroniclePatch.cs(三个新钩点)**

City.setKingdom 的旧国必须在原方法执行**前**取(Postfix 时 `city.kingdom` 已是新国)→ 用 Prefix 取旧国传给 Postfix,或在 Prefix 里直接比较。这里用 **Prefix 读旧国 + 立即判断记录**(原方法未执行,city.kingdom 仍是旧国;新国 = 参数 pKingdom)。

```csharp
using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     编年史新增钩点:
    ///     - Postfix Kingdom.newCivKingdom —— 建国。
    ///     - Postfix KingdomManager.removeObject —— 亡国。
    ///     - Prefix  City.setKingdom —— 城市易主(Prefix 时 city.kingdom 仍是旧国,参数为新国;
    ///       pFromLoad 读档回填跳过)。
    ///     成王/换君事件在 AW_FigurePatch.SetKing_Postfix 里走 ChronicleEvents.OnKingChanged,不在此重复。
    /// </summary>
    [HarmonyPatch]
    public static class AW_ChroniclePatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.newCivKingdom))]
        public static void NewCivKingdom_Postfix(Kingdom __instance)
        {
            ChronicleEvents.OnKingdomFounded(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(KingdomManager), nameof(KingdomManager.removeObject))]
        public static void RemoveKingdom_Postfix(Kingdom pKingdom)
        {
            ChronicleEvents.OnKingdomDestroyed(pKingdom);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), nameof(City.setKingdom))]
        public static void CitySetKingdom_Prefix(City __instance, Kingdom pKingdom, bool pFromLoad)
        {
            // Prefix:原方法尚未执行,__instance.kingdom 仍是旧国;pKingdom 是即将设的新国。
            Kingdom oldKingdom = __instance != null ? __instance.kingdom : null;
            ChronicleEvents.OnCityTransferred(__instance, oldKingdom, pKingdom, pFromLoad);
        }
    }
}
```

- [ ] **Step 3: 核实三个钩点签名 + 可见性**

逐个核实(反编译源 `F:/WorldBox New Mod/AssetRipper_export_20260628_163320/.../Assembly-CSharp/`):
- `Kingdom.newCivKingdom(Actor)`:Kingdom.cs:443 是 `internal void`。Harmony patch internal 方法用 `nameof` 可能因可见性取不到 → 若 `nameof(Kingdom.newCivKingdom)` 编译报错,改用字符串 `"newCivKingdom"`。__instance 即建好的 Kingdom。
- `KingdomManager.removeObject(Kingdom)`:KingdomManager.cs:43 `public override void`。参数名核对(可能是 `pObject` 而非 `pKingdom`)——**打开反编译确认参数名**,Harmony Postfix 按参数名注入,patch 方法形参名必须与原版一致(若是 `pObject` 就改成 `Kingdom pObject`)。
- `City.setKingdom(Kingdom pKingdom, bool pFromLoad)`:City.cs:501 `internal void setKingdom(Kingdom pKingdom, bool pFromLoad = false)`。参数名核对 `pKingdom`/`pFromLoad`(以反编译为准);internal → 若 `nameof(City.setKingdom)` 报错改字符串 `"setKingdom"`。
- **若是泛型基类继承的方法(如 removeObject 来自 AssetLibrary/Manager 基类),patch typeof 必须用方法实际声明类**(记忆 aw3-harmony-inherited-method-pitfall:钩继承未 override 的方法解析为 null 致整个 PatchAll 失败、全 mod 被禁用)。removeObject 若是 `KingdomManager` 直接 override(KingdomManager.cs:43 有 override 关键字)则 typeof(KingdomManager) 正确;若只在基类则改 typeof(基类)。

- [ ] **Step 4: build 验证 + 反射可见性修正**

Run: build 命令
Expected: `0 个警告 0 个错误`。若因 internal/nameof 报错,按 Step 3 改字符串方法名 + 修正参数名。

- [ ] **Step 5: 进游戏冒烟验证(关键)**

进游戏(需重进):
- 看 Player.log **没有** `Patching exception in method null` / `has been disabled`(确认三个新钩点都成功 patch,没触发"继承方法 null"坑致全 mod 禁用)。
- 夏人建国 → KingdomHistory found 行;某城被攻陷换国 → CityHistory city_transfer 行,且读档时**不产生**假易主;王国灭亡 → destroyed 行。
- 用 Task 3 Step 4 的临时 log 肉眼核对。

---

## Self-Review

**1. Spec coverage(逐条对设计文档):**
- 三独立表 → Task 1 ✓
- 共享列(event_id/world_time/year_prefix快照/subject_name/content/event_type)→ Task 1 字段 ✓
- HistoryWriter 拼前缀(通用年+年号,无年号只通用年)→ Task 2 BuildYearPrefix ✓
- year_prefix 快照(防改年号错乱)→ Task 2 写入时拼好存库 ✓
- 人物出生(入谱贵族)+ 历史人物出生 → Task 3 ✓
- 人物死亡 → Task 4 ✓
- 人物成王 + 国家换君 → Task 5 ✓
- 国家建国/亡国 + 城市易主 → Task 6 ✓
- 防重复(同值不记/读档不记/城市只记真易主)→ Task 5(同王跳过)+ Task 6(pFromLoad/旧国空/同国跳过)✓
- 仅入谱贵族(IsXia+lineage_id>=0)→ Task 3/4/5 都判 ✓
- 不做查看窗口 / 不做国家改名 → 计划无此 Task ✓
- 随存档持久化 → [TableDef] 反射自动建表,Task 1 ✓
- 国家改名本期不做 → 未列 ✓(符合设计)

**2. Placeholder scan:** 无 TBD/TODO/"类似 TaskN"/空实现;每个代码步给了完整代码。Step 3(Task6)的"核实签名"不是占位,是必要的反编译核对动作并给了明确的失败应对(改字符串/改参数名/改 typeof)。

**3. Type consistency:**
- `HistoryWriter.RecordPerson/RecordKingdom/RecordCity` 签名在 Task 2 定义,Task 3/4/5/6 调用一致 ✓
- `ChronicleEvents.OnKingChanged/OnKingdomFounded/OnKingdomDestroyed/OnCityTransferred` Task 5/6 定义并调用一致 ✓
- 列名大写(EVENT_ID/ACTOR_ID/...)与字段名(event_id/actor_id)大写映射规则一致 ✓
- `LineageKeys.CHRONICLE_LAST_KING_ID` Task 5 Step3 定义,Step2 使用 ✓
- event_type 字符串(birth/death/become_king/found/rule_change/destroyed/city_transfer)与设计文档枚举一致 ✓

## 验证(整体)
1. 每个 Task build 0 警 0 错。
2. 全部完成后进游戏(重进):
   - Player.log 无 Patching exception / mod 未被禁用。
   - PersonBiography:贵族出生/死亡/成王有行,year_prefix = "N年 X元年" 或 "N年"。
   - KingdomHistory:建国/换君/亡国有行。
   - CityHistory:真易主有行,读档不假记、同国不重复。
   - 改年号后新事件用新年号,旧事件 prefix 仍是当时快照。

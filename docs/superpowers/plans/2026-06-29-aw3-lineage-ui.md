# AW3.0 姓族 UI（阶段5）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把已建好的姓族后端数据可视化——自定义「姓族」tab、姓→氏支列表→氏族大树→家族树四级导航、信息窗注入姓/氏/身份、kingdom 窗注入继承人/年号/头衔、用持久化家族树替换原版"死后即消失"的家族树。

**Architecture:** 混合路线。新开窗口用 NML `AbstractListWindow<T,TItem>`/`AbstractWindow<T>`（自带滚动+对象池）；嵌入原版的显示用 Harmony Postfix patch 原版 `UnitWindow.showStatsRows`/`KingdomWindow.showStatsRows`；替换原版族谱用 patch 接管。所有 UI 只消费已有的 `LineageQuery`（只读 SQLite），不改后端逻辑。

**Tech Stack:** C# .NET Framework 4.8（net48）、NeoModLoader（NML）、HarmonyLib、UnityEngine.UI、System.Data.SQLite。游戏 WorldBox 0.51.0+。

## Global Constraints

- 构建命令固定 `dotnet build -t:Rebuild --no-incremental`（增量缓存会说谎，必须全量）。每个任务末尾用它验证 0 错 0 警。
- 本项目目标框架 net48 / C# 默认版本：**禁止** file-scoped namespace（`namespace X;`）、`record`、`init`、顶级 `using` 简写。一律用传统 `namespace X { }` 块。（NML dll 内部用了新语法但那是已编译的，我方源码不能用。）
- 所有 patch / 夺舍 / 自定义类用 `AW_` 命名前缀。
- Postfix 优先；仅"替换原版族谱"用 Prefix `return false` 接管。
- `BaseSystemData` 自定义字段 get/set **只有 float 无 double**：`data.get/set(string, int/long/float/string/bool)`。存 world_time 须 `(float)` 转换。（本计划 UI 不写 data，只读，但读 kingdom 年号 start 时注意是 float。）
- patch 类靠 `ModClass.OnModLoad` 里已有的 `new Harmony(GUID).PatchAll()` 自动挂载（扫描本程序集所有 `[HarmonyPatch]`），无需手动注册；非 patch 的 UI 初始化（tab）需在 `OnModLoad` 末尾显式调 `AW_LineageTab.Init()`。
- 本地提交，**不推送**，直到用户进游戏验证（逐阶段验证规则）。
- `LineageQuery` / `LineageDTO` / `LineageArchiveReader` / `ActorArchiveTableItem` 已存在且字段齐全，复用不重写。`StatsWindow.showStatRow`/`KeyValueField`/`UiUnitAvatarElement` 等原版 API 通过 publicized dll 可访问（含 internal 成员）。

---

## 现有代码事实（已核实，实现时据此）

- `LineageQuery`（`Code/core/lineage/LineageQuery.cs`，`internal static`）已有：
  - `List<SurnameOverview> GetSurnameOverview()`
  - `List<ShiBranchInfo> GetShiBranches(string pFamilyName)`
  - `List<MemberInfo> GetSurnameMembers(string pFamilyName)` / `List<MemberInfo> GetShiMembers(long pShiId)`
  - `FamilyTreeNode GetFamilyTree(long pCenterActorId)`（含 parents/children 各一层）
  - `List<long> GetParentIds(long pChildId)` / `List<long> GetChildIds(long pParentId)`（public）
  - `private static FamilyTreeNode BuildNode(long pId)`（活人优先 `World.world.units.get(id)`，死人 `LineageArchiveReader.ReadRow(id)`）
- `LineageDTO`（`Code/core/lineage/LineageDTO.cs`，全 `internal class`）已有：`SurnameOverview`{family_name,total,alive,noble,shi_count,earliest_time}、`ShiBranchInfo`{shi_id,lineage_id,clan_name,source_type,total,alive,noble,created_time,founder_actor_id}、`MemberInfo`{id,display_name,family_name,clan_name,status,sex,is_alive,birth_time,death_time,kingdom_name,city_name,shi_id}、`FamilyTreeNode`{id,display_name,sex,is_alive,status,parents,children}。
- `ActorArchiveTableItem` 字段含：id,given_name,display_name,family_name,clan_name,lineage_id,shi_id,asset_id,sex,status,kingdom_id,kingdom_name,city_id,city_name,parent_id_1/2,generation,noble_distance,birth_time,death_time,is_alive,name_integrated,head,skin,skin_set。
- `LineageArchiveReader.ReadRow(long)` 返回完整 `ActorArchiveTableItem` 或 null。
- `LineageService`（`Code/core/lineage/LineageService.cs`，`internal static`）已有 `bool IsXia(Actor)`。`LineageKeys` 含 `LINEAGE_ID`/`SHI_ID`/`LINEAGE_STATUS`/`FAMILY_NAME`/`CLAN_NAME`/`KINGDOM_INTEGRATED`("aw_name_integrated")。`LineageStatus`{NONE,NOBLE,COMMON,SLAVE}。
- 后端服务：`HeirService.GetHeir(Kingdom)`→Actor（可能 null）、`YearNameService.GetYearName(Kingdom)`→string、`KingdomTitleService.GetTitleString(Kingdom)`→string、`KingdomTitleService.GetTitle(Kingdom)`→enum、`KingdomTitleService.GetTitleChar(...)`→string。
- 原版 API（publicized）：
  - `internal KeyValueField StatsWindow.showStatRow(string pId, object pValue, MetaType pMetaType=MetaType.None, long pMetaId=-1, string pIconPath=null, string pTooltipId=null, TooltipDataGetter pTooltipData=null)`
  - `KeyValueField` 字段：`Text name_text`、`Text value`、`Image icon`、`UnityAction on_click_value`、`UnityAction on_hover_value`。
  - `void UiUnitAvatarElement.show(Actor pActor)`。
  - `internal override void UnitWindow.showStatsRows()` / `internal override void KingdomWindow.showStatsRows()`（无参，可 Postfix）。
  - `ScrollWindow.showWindow(string pWindowID)`；`SpriteTextureLoader.getSprite(string path)`。
  - NML：`NeoModLoader.General.UI.Tab.TabManager.CreateTab(string name, string titleKey, string descKey, Sprite icon)`→PowersTab；`PowersTab.SetLayout(List<string>)`/`AddPowerButton(string groupId, PowerButton)`/`UpdateLayout()`；`PowerButtonCreator.CreateSimpleButton(string id, UnityAction action, Sprite icon)`→PowerButton。
  - NML：`AbstractWindow<T>` override `Init()`(abstract)/`OnNormalEnable()`；`protected Transform ContentTransform`；`static T Instance`；`static T CreateAndInit(string id)`。`AbstractListWindow<T,TItem>` override `CreateItemPrefab()`(abstract,返回 `AbstractListWindowItem<TItem>`)；`protected void AddItemToList(TItem)`/`ClearList()`。`AbstractListWindowItem<TItem>` override `Setup(TItem)`(abstract)。
- `ModClass.OnModLoad`（`Code/ModClass.cs:15`）已有 `PatchAll()` + `XiaContent.Init()`，在末尾追加 UI tab 初始化。

## File Structure

```
Code/core/lineage/
  LineageDTO.cs        修改:FamilyTreeNode 补字段(birth/death/clan_name/kingdom/city/head/skin/skin_set/shi_id/founder标记)
  LineageQuery.cs      修改:BuildNode 填新字段;新增 GetShiBranchFounderId/GetShiBranchInfo
Code/ui/
  AW_LineageTab.cs                 自定义「姓族」tab + 「姓族列表」按钮入口(OnModLoad 调 Init)
  AW_LineageWindowIds.cs           窗口 id 常量集中处
  windows/
    LineageOverviewWindow.cs       AbstractListWindow<姓> 总览(列所有姓)
    ShiBranchListWindow.cs         AbstractListWindow<氏支> 某姓的氏支列表(+SetContext)
    FamilyTreeWindow.cs            AbstractWindow 多叉树(大树/家族树双模式+懒加载折叠+SetContext)
  items/
    LineageListItem.cs             姓行 prefab(名+总/存活/贵族/氏支数,可点击→氏支列表)
    ShiBranchListItem.cs           氏支行 prefab(名+总/存活/成立年/贵族数,可点击→大树)
    FamilyTreeNodeView.cs          树节点 prefab(头像占位+名+性别+生卒+关系标签+死人灰调+展开按钮)
Code/patch/
  AW_UnitWindowPatch.cs            Postfix UnitWindow.showStatsRows:注入姓/氏/身份/家族树按钮
  AW_KingdomWindowPatch.cs         Postfix KingdomWindow.showStatsRows:注入继承人/年号/头衔
  AW_GenealogyReplacePatch.cs      接管原版族谱:有谱系贵族→我们的家族树
```

---

## Task 1: 扩充 FamilyTreeNode DTO + BuildNode 填充

把家族树节点 DTO 补齐 UI 需要的字段（生卒年、氏、国家/城市、头像数据），并让 `BuildNode` 填这些字段。这是后续树 UI 的数据基础。

**Files:**
- Modify: `Code/core/lineage/LineageDTO.cs`（`FamilyTreeNode` 类）
- Modify: `Code/core/lineage/LineageQuery.cs`（`BuildNode` 方法 + 新增两个公开方法）

**Interfaces:**
- Consumes: `LineageArchiveReader.ReadRow(long)`→`ActorArchiveTableItem`；`World.world.units.get(long)`→Actor；`LineageService.IsXia(Actor)`。
- Produces:
  - `FamilyTreeNode` 新增字段：`string clan_name`、`double birth_time`、`double death_time`、`string kingdom_name`、`string city_name`、`long shi_id`、`int head`、`int skin`、`int skin_set`、`int noble_distance`。
  - `long LineageQuery.GetShiBranchFounderId(long pShiId)`（取氏支始祖 actor id，无则 -1）。
  - `ShiBranchInfo LineageQuery.GetShiBranchInfo(long pShiId)`（取单个氏支信息+统计，无则 null）。

- [ ] **Step 1: 给 FamilyTreeNode 补字段**

修改 `Code/core/lineage/LineageDTO.cs` 中 `FamilyTreeNode` 类，在 `public string status;` 之后、`parents` 之前插入：

```csharp
        public string clan_name;     // 氏(显示用)
        public double birth_time;
        public double death_time;    // -1 表示在世/未记录
        public string kingdom_name;
        public string city_name;
        public long   shi_id = -1;
        public int    noble_distance = 99;
        public int    head;          // 头像数据(可选,用于自绘头像)
        public int    skin;
        public int    skin_set;
```

- [ ] **Step 2: 让 BuildNode 填充新字段**

在 `Code/core/lineage/LineageQuery.cs` 中，替换整个 `BuildNode` 方法为：

```csharp
        /// <summary>构造单个节点。活人优先用 actor 当前态,否则查档案。两路都填齐 UI 字段。</summary>
        private static FamilyTreeNode BuildNode(long pId)
        {
            var live = World.world?.units?.get(pId);
            if (live != null && LineageService.IsXia(live))
            {
                live.data.get("display_name", out string disp, "");
                live.data.get(LineageKeys.LINEAGE_STATUS, out string st, LineageStatus.NONE);
                live.data.get(LineageKeys.CLAN_NAME, out string clan, "");
                live.data.get(LineageKeys.SHI_ID, out long shi, -1L);
                live.data.get(LineageKeys.NOBLE_DISTANCE, out long nd, 99L);
                return new FamilyTreeNode
                {
                    id = pId,
                    display_name = string.IsNullOrEmpty(disp) ? live.getName() : disp,
                    sex = live.isSexMale() ? 0 : 1,
                    is_alive = true,
                    status = st,
                    clan_name = clan,
                    shi_id = shi,
                    noble_distance = (int)nd,
                    birth_time = live.data.created_time,
                    death_time = -1,
                    kingdom_name = live.kingdom?.name ?? "",
                    city_name = live.city?.name ?? ""
                };
            }

            var row = LineageArchiveReader.ReadRow(pId);
            if (row == null) return null;
            return new FamilyTreeNode
            {
                id = pId,
                display_name = string.IsNullOrEmpty(row.display_name) ? row.given_name : row.display_name,
                sex = row.sex,
                is_alive = row.is_alive != 0,
                status = row.status,
                clan_name = row.clan_name ?? "",
                shi_id = row.shi_id,
                noble_distance = row.noble_distance,
                birth_time = row.birth_time,
                death_time = row.death_time,
                kingdom_name = row.kingdom_name ?? "",
                city_name = row.city_name ?? "",
                head = row.head,
                skin = row.skin,
                skin_set = row.skin_set
            };
        }
```

（注：`Actor.city`/`Actor.kingdom` 字段在新版存在；若 `Actor.city` 不可用，编译会报错，改用 `live.city` → `live.getCity()`，按编译结果定。先按 `live.city`。`LineageKeys.NOBLE_DISTANCE` 存的是 int，但 `data.get` 只有 long 重载，故用 `out long nd` 再 `(int)nd`。）

- [ ] **Step 3: 新增 GetShiBranchFounderId 和 GetShiBranchInfo**

在 `Code/core/lineage/LineageQuery.cs` 的 `GetShiBranches` 方法之后插入：

```csharp
        /// <summary>取某氏支的始祖 actor id(ShiBranch.FOUNDER_ACTOR_ID)。无则 -1。</summary>
        public static long GetShiBranchFounderId(long pShiId)
        {
            var db = DB;
            if (db == null) return -1;
            using var cmd = new SQLiteCommand(db);
            cmd.CommandText =
                $"SELECT IFNULL(FOUNDER_ACTOR_ID, -1) FROM {ShiBranchTableItem.GetTableName()} WHERE SHI_ID=@s LIMIT 1";
            cmd.Parameters.AddWithValue("@s", pShiId);
            var o = cmd.ExecuteScalar();
            return o == null ? -1 : (long)o;
        }

        /// <summary>取单个氏支信息(含统计)。无则 null。</summary>
        public static ShiBranchInfo GetShiBranchInfo(long pShiId)
        {
            var db = DB;
            if (db == null) return null;
            using var cmd = new SQLiteCommand(db);
            cmd.CommandText =
                $"SELECT SHI_ID, LINEAGE_ID, CLAN_NAME, SOURCE_TYPE, CREATED_TIME, FOUNDER_ACTOR_ID " +
                $"FROM {ShiBranchTableItem.GetTableName()} WHERE SHI_ID=@s LIMIT 1";
            cmd.Parameters.AddWithValue("@s", pShiId);
            ShiBranchInfo info = null;
            using (var reader = (SQLiteDataReader)cmd.ExecuteReader())
            {
                if (reader.Read())
                {
                    info = new ShiBranchInfo
                    {
                        shi_id = reader.GetInt64(0),
                        lineage_id = reader.GetInt64(1),
                        clan_name = SafeStr(reader, 2),
                        source_type = SafeStr(reader, 3),
                        created_time = reader.GetDouble(4),
                        founder_actor_id = reader.GetInt64(5)
                    };
                }
            }
            if (info != null) FillShiCounts(info);
            return info;
        }
```

- [ ] **Step 4: 编译验证**

Run: `dotnet build -t:Rebuild --no-incremental`（在 `F:\WorldBox New Mod\AncientWarfare3.0`）
Expected: 0 错 0 警。若 `live.city` 报错，改成 `live.city` 的正确取法（`live.getCity()`），重编。

- [ ] **Step 5: Commit**

```bash
git add Code/core/lineage/LineageDTO.cs Code/core/lineage/LineageQuery.cs
git commit -m "姓族UI数据层:FamilyTreeNode补生卒/氏/国城/头像字段+BuildNode填充+GetShiBranchFounderId/GetShiBranchInfo"
```

---

## Task 2: 窗口 id 常量 + 自定义「姓族」tab 入口

集中窗口 id 常量，建一个自定义 tab 放「姓族列表」按钮，点击打开总览窗。这是整套 UI 的入口骨架（窗口本身在 Task 3 建，此处先打通 tab→showWindow 调用链，用占位 LogInfo 验证按钮可点）。

**Files:**
- Create: `Code/ui/AW_LineageWindowIds.cs`
- Create: `Code/ui/AW_LineageTab.cs`
- Modify: `Code/ModClass.cs`（OnModLoad 末尾加 `AW_LineageTab.Init()`）

**Interfaces:**
- Consumes: NML `TabManager.CreateTab`/`PowersTab.SetLayout/AddPowerButton/UpdateLayout`/`PowerButtonCreator.CreateSimpleButton`；`SpriteTextureLoader.getSprite`；`ScrollWindow.showWindow`。
- Produces:
  - `AncientWarfare3.ui.AW_LineageWindowIds`（static class）：`const string OVERVIEW="aw_lineage_overview"`、`SHI_LIST="aw_shi_list"`、`FAMILY_TREE="aw_family_tree"`。
  - `AncientWarfare3.ui.AW_LineageTab.Init()`（static void，OnModLoad 调用）。

- [ ] **Step 1: 写窗口 id 常量类**

Create `Code/ui/AW_LineageWindowIds.cs`：

```csharp
namespace AncientWarfare3.ui
{
    /// <summary>姓族系统所有自定义窗口 id 常量,集中一处避免硬编码散落。</summary>
    internal static class AW_LineageWindowIds
    {
        public const string OVERVIEW = "aw_lineage_overview";   // 姓族总览(列所有姓)
        public const string SHI_LIST = "aw_shi_list";           // 某姓的氏支列表
        public const string FAMILY_TREE = "aw_family_tree";     // 家族树/氏族大树(双模式)
    }
}
```

- [ ] **Step 2: 写 tab 入口类（按钮先打 LogInfo 占位）**

Create `Code/ui/AW_LineageTab.cs`：

```csharp
using System.Collections.Generic;
using NeoModLoader.General;
using NeoModLoader.General.UI.Tab;

namespace AncientWarfare3.ui
{
    /// <summary>
    ///     自定义「姓族」分栏 tab + 「姓族列表」按钮(照搬 AW2 TabManager 写法,NML 新版 API)。
    ///     由 ModClass.OnModLoad 末尾调用 Init()。
    /// </summary>
    internal static class AW_LineageTab
    {
        private const string TAB_ID = "AW3Lineage";
        private const string GROUP = "lineage";
        private static bool _inited;

        public static void Init()
        {
            if (_inited) return;
            _inited = true;

            PowersTab tab = TabManager.CreateTab(
                TAB_ID,
                "AW3 Lineage",
                "Ancient Warfare 3 lineage / surname archive",
                SpriteTextureLoader.getSprite("ui/icons/iconClan"));

            tab.SetLayout(new List<string> { GROUP });

            PowerButton overviewButton = PowerButtonCreator.CreateSimpleButton(
                "aw_lineage_overview_btn",
                () => OpenOverview(),
                SpriteTextureLoader.getSprite("ui/icons/iconClan"));

            tab.AddPowerButton(GROUP, overviewButton);
            tab.UpdateLayout();
        }

        private static void OpenOverview()
        {
            // Task 3 接好窗口后改为 ScrollWindow.showWindow(AW_LineageWindowIds.OVERVIEW)
            ModClass.LogInfo("[AW3] 姓族列表按钮点击(总览窗待 Task 3 接入)");
        }
    }
}
```

（注：`ModClass.LogInfo` 是 NML BasicMod 提供的静态日志方法；若不可用改 `NeoModLoader.api.LogService.logInfo(...)`，按编译定。`SpriteTextureLoader.getSprite("ui/icons/iconClan")` 用原版氏族图标，保证存在。）

- [ ] **Step 3: ModClass 接入**

修改 `Code/ModClass.cs`，在 `LogInfo("Ancient Warfare 3.0 loaded — batch A (Xia race).");` 之前插入：

```csharp
            // 阶段5:姓族 UI —— 自定义 tab + 入口按钮(窗口靠 Harmony patch + showWindow 打开)
            ui.AW_LineageTab.Init();
```

- [ ] **Step 4: 编译验证**

Run: `dotnet build -t:Rebuild --no-incremental`
Expected: 0 错 0 警。

- [ ] **Step 5: Commit**

```bash
git add Code/ui/AW_LineageWindowIds.cs Code/ui/AW_LineageTab.cs Code/ModClass.cs
git commit -m "姓族UI入口:窗口id常量+自定义「姓族」tab+「姓族列表」按钮(NML TabManager),ModClass接入"
```

---

## Task 3: 姓族总览窗口（LineageOverviewWindow + LineageListItem）

NML 列表窗，列出所有姓，每行显示姓名+总/存活/贵族/氏支数，点击行进入该姓的氏支列表（Task 4）。本任务建窗口 + 行 prefab，并把 Task 2 的按钮接到真窗口。

**Files:**
- Create: `Code/ui/items/LineageListItem.cs`
- Create: `Code/ui/windows/LineageOverviewWindow.cs`
- Modify: `Code/ui/AW_LineageTab.cs`（`OpenOverview` 改为真打开）

**Interfaces:**
- Consumes: `LineageQuery.GetSurnameOverview()`→`List<SurnameOverview>`；NML `AbstractListWindow<T,TItem>`/`AbstractListWindowItem<TItem>`；`ScrollWindow.showWindow`。
- Produces:
  - `LineageOverviewWindow.Open()`（static，创建+显示）。
  - `LineageOverviewWindow` 是 `AbstractListWindow<LineageOverviewWindow, SurnameOverview>`。
  - `LineageListItem : AbstractListWindowItem<SurnameOverview>`，点击行调 `ShiBranchListWindow.OpenFor(string familyName)`（Task 4 提供，本任务先留 `ScrollWindow.showWindow(AW_LineageWindowIds.SHI_LIST)` + 静态上下文占位）。

- [ ] **Step 1: 写行 prefab（LineageListItem）**

Create `Code/ui/items/LineageListItem.cs`：

```csharp
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.windows;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    /// <summary>姓族总览的一行:姓名 + 总/存活/贵族/氏支数。点击进入该姓氏支列表。</summary>
    internal class LineageListItem : AbstractListWindowItem<SurnameOverview>
    {
        private Text _label;
        private Button _button;
        private string _familyName;

        public override void Setup(SurnameOverview pObject)
        {
            EnsureUi();
            _familyName = pObject.family_name;
            _label.text =
                $"{pObject.family_name}   总{pObject.total} 活{pObject.alive} 贵{pObject.noble} 氏{pObject.shi_count}";
        }

        private void EnsureUi()
        {
            if (_label != null) return;

            var rect = gameObject.GetComponent<RectTransform>();
            if (rect == null) rect = gameObject.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(220, 24);

            var le = gameObject.GetComponent<LayoutElement>();
            if (le == null) le = gameObject.AddComponent<LayoutElement>();
            le.minHeight = 24;
            le.preferredHeight = 24;

            _button = gameObject.GetComponent<Button>();
            if (_button == null) _button = gameObject.AddComponent<Button>();
            _button.onClick.AddListener(OnClick);

            var textObj = new GameObject("Label", typeof(RectTransform), typeof(Text));
            textObj.transform.SetParent(transform, false);
            var trect = textObj.GetComponent<RectTransform>();
            trect.anchorMin = Vector2.zero;
            trect.anchorMax = Vector2.one;
            trect.sizeDelta = Vector2.zero;
            _label = textObj.GetComponent<Text>();
            _label.font = LocalizedTextManager.current_font;
            _label.fontSize = 11;
            _label.color = Color.white;
            _label.alignment = TextAnchor.MiddleLeft;
        }

        private void OnClick()
        {
            if (string.IsNullOrEmpty(_familyName)) return;
            ShiBranchListWindow.OpenFor(_familyName);
        }
    }
}
```

（注：`LocalizedTextManager.current_font` 是原版当前本地化字体；若为 null，Unity 用默认字体，仍可显示。`AbstractListWindow` 的对象池会复用此 GameObject 并重复调 `Setup`，故 `EnsureUi` 用幂等判断只建一次 UI，`Setup` 每次只刷文本+上下文。）

- [ ] **Step 2: 写总览窗（LineageOverviewWindow）**

Create `Code/ui/windows/LineageOverviewWindow.cs`：

```csharp
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;

namespace AncientWarfare3.ui.windows
{
    /// <summary>姓族总览:列出所有姓(每行可点进氏支列表)。NML 列表窗,自带滚动+对象池。</summary>
    internal class LineageOverviewWindow : AbstractListWindow<LineageOverviewWindow, SurnameOverview>
    {
        public static void Open()
        {
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.OVERVIEW);
            ScrollWindow.showWindow(AW_LineageWindowIds.OVERVIEW);
        }

        protected override void Init()
        {
            // 无额外初始化;列表内容在 OnNormalEnable 刷新。
        }

        public override void OnNormalEnable()
        {
            Refresh();
        }

        private void Refresh()
        {
            ClearList();
            var list = LineageQuery.GetSurnameOverview();
            foreach (var s in list)
            {
                AddItemToList(s);
            }
        }

        protected override AbstractListWindowItem<SurnameOverview> CreateItemPrefab()
        {
            var obj = new GameObject("LineageListItem");
            obj.transform.SetParent(ContentTransform, false);
            var item = obj.AddComponent<LineageListItem>();
            obj.SetActive(false);
            return item;
        }
    }
}
```

- [ ] **Step 3: 把 tab 按钮接到真窗口**

修改 `Code/ui/AW_LineageTab.cs` 的 `OpenOverview`：

```csharp
        private static void OpenOverview()
        {
            windows.LineageOverviewWindow.Open();
        }
```

并在文件顶部 using 区不需额外引用（用全限定 `windows.`）。

- [ ] **Step 4: 编译验证**

Run: `dotnet build -t:Rebuild --no-incremental`
Expected: 0 错 0 警。`ShiBranchListWindow.OpenFor` 在 Task 4 才有 —— 故本步骤会因 `LineageListItem` 引用未定义类型而**编译失败**。为保持 TDD 单任务可编译，本任务先把 `LineageListItem.OnClick` 内改为占位：

```csharp
        private void OnClick()
        {
            if (string.IsNullOrEmpty(_familyName)) return;
            ModClass.LogInfo("[AW3] 点击姓:" + _familyName + "(氏支列表待 Task 4)");
        }
```

Task 4 完成后再替换回 `ShiBranchListWindow.OpenFor(_familyName)`。重编 Expected: 0 错 0 警。

- [ ] **Step 5: Commit**

```bash
git add Code/ui/items/LineageListItem.cs Code/ui/windows/LineageOverviewWindow.cs Code/ui/AW_LineageTab.cs
git commit -m "姓族总览窗:LineageOverviewWindow(NML列表窗)+LineageListItem行(姓名+总/活/贵/氏支数),tab按钮接入"
```

---

## Task 4: 氏支列表窗口（ShiBranchListWindow + ShiBranchListItem）

某姓下的氏支列表，每行氏支名+总/存活/成立年/贵族数，点击进入该氏支的氏族大树（Task 6）。带 `OpenFor(familyName)` 上下文入口。

**Files:**
- Create: `Code/ui/items/ShiBranchListItem.cs`
- Create: `Code/ui/windows/ShiBranchListWindow.cs`
- Modify: `Code/ui/items/LineageListItem.cs`（OnClick 改回真跳转）

**Interfaces:**
- Consumes: `LineageQuery.GetShiBranches(string)`→`List<ShiBranchInfo>`；`Date.getYearsSince(double)`→int（成立年数）。
- Produces:
  - `ShiBranchListWindow.OpenFor(string pFamilyName)`（static，设上下文+显示）。
  - `ShiBranchListWindow` 是 `AbstractListWindow<ShiBranchListWindow, ShiBranchInfo>`。
  - `ShiBranchListItem : AbstractListWindowItem<ShiBranchInfo>`，点击调 `FamilyTreeWindow.OpenBigTree(long shiId)`（Task 6 提供，本任务先占位）。

- [ ] **Step 1: 写氏支行 prefab**

Create `Code/ui/items/ShiBranchListItem.cs`：

```csharp
using AncientWarfare3.core.lineage;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    /// <summary>氏支列表的一行:氏支名 + 总/存活/成立年/贵族数。点击进入该氏支大树。</summary>
    internal class ShiBranchListItem : AbstractListWindowItem<ShiBranchInfo>
    {
        private Text _label;
        private Button _button;
        private long _shiId = -1;

        public override void Setup(ShiBranchInfo pObject)
        {
            EnsureUi();
            _shiId = pObject.shi_id;
            int years = Date.getYearsSince(pObject.created_time);
            _label.text =
                $"{pObject.clan_name}   总{pObject.total} 活{pObject.alive} 立{years}年 贵{pObject.noble}";
        }

        private void EnsureUi()
        {
            if (_label != null) return;

            var rect = gameObject.GetComponent<RectTransform>();
            if (rect == null) rect = gameObject.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(220, 24);

            var le = gameObject.GetComponent<LayoutElement>();
            if (le == null) le = gameObject.AddComponent<LayoutElement>();
            le.minHeight = 24;
            le.preferredHeight = 24;

            _button = gameObject.GetComponent<Button>();
            if (_button == null) _button = gameObject.AddComponent<Button>();
            _button.onClick.AddListener(OnClick);

            var textObj = new GameObject("Label", typeof(RectTransform), typeof(Text));
            textObj.transform.SetParent(transform, false);
            var trect = textObj.GetComponent<RectTransform>();
            trect.anchorMin = Vector2.zero;
            trect.anchorMax = Vector2.one;
            trect.sizeDelta = Vector2.zero;
            _label = textObj.GetComponent<Text>();
            _label.font = LocalizedTextManager.current_font;
            _label.fontSize = 11;
            _label.color = Color.white;
            _label.alignment = TextAnchor.MiddleLeft;
        }

        private void OnClick()
        {
            if (_shiId < 0) return;
            windows.FamilyTreeWindow.OpenBigTree(_shiId);
        }
    }
}
```

- [ ] **Step 2: 写氏支列表窗（带上下文）**

Create `Code/ui/windows/ShiBranchListWindow.cs`：

```csharp
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;

namespace AncientWarfare3.ui.windows
{
    /// <summary>某姓下的氏支列表(每行可点进该氏支大树)。上下文=当前姓名,OpenFor 设置。</summary>
    internal class ShiBranchListWindow : AbstractListWindow<ShiBranchListWindow, ShiBranchInfo>
    {
        private static string _contextFamilyName = "";

        public static void OpenFor(string pFamilyName)
        {
            _contextFamilyName = pFamilyName ?? "";
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.SHI_LIST);
            ScrollWindow.showWindow(AW_LineageWindowIds.SHI_LIST);
            if (Instance != null && Instance.gameObject.activeInHierarchy) Instance.Refresh();
        }

        protected override void Init() { }

        public override void OnNormalEnable()
        {
            Refresh();
        }

        public void Refresh()
        {
            ClearList();
            if (string.IsNullOrEmpty(_contextFamilyName)) return;
            var list = LineageQuery.GetShiBranches(_contextFamilyName);
            foreach (var s in list) AddItemToList(s);
        }

        protected override AbstractListWindowItem<ShiBranchInfo> CreateItemPrefab()
        {
            var obj = new GameObject("ShiBranchListItem");
            obj.transform.SetParent(ContentTransform, false);
            var item = obj.AddComponent<ShiBranchListItem>();
            obj.SetActive(false);
            return item;
        }
    }
}
```

（注：`OpenFor` 里 `OnNormalEnable` 会在 `showWindow` 触发 OnEnable 时自动刷新；额外的显式 `Refresh()` 处理"窗口已开着、仅换上下文"的情况。两次刷新幂等无害。）

- [ ] **Step 3: 把 LineageListItem 点击接回真跳转**

修改 `Code/ui/items/LineageListItem.cs` 的 `OnClick` 为：

```csharp
        private void OnClick()
        {
            if (string.IsNullOrEmpty(_familyName)) return;
            windows.ShiBranchListWindow.OpenFor(_familyName);
        }
```

确保文件顶部已 `using AncientWarfare3.ui.windows;`（若用全限定 `windows.` 则不需要；保持与 Step 写法一致，用全限定）。

- [ ] **Step 4: 编译验证**

Run: `dotnet build -t:Rebuild --no-incremental`
Expected: `FamilyTreeWindow.OpenBigTree` 在 Task 6 才有 → `ShiBranchListItem.OnClick` 引用未定义。本任务先把该行改占位：

```csharp
        private void OnClick()
        {
            if (_shiId < 0) return;
            ModClass.LogInfo("[AW3] 点击氏支 shi_id=" + _shiId + "(大树待 Task 6)");
        }
```

重编 Expected: 0 错 0 警。Task 6 后替换回 `windows.FamilyTreeWindow.OpenBigTree(_shiId)`。

- [ ] **Step 5: Commit**

```bash
git add Code/ui/items/ShiBranchListItem.cs Code/ui/windows/ShiBranchListWindow.cs Code/ui/items/LineageListItem.cs
git commit -m "氏支列表窗:ShiBranchListWindow(OpenFor上下文)+ShiBranchListItem行(氏支名+总/活/立年/贵),姓→氏支跳转接通"
```

---

## Task 5: 家族树节点 prefab（FamilyTreeNodeView）

树的单个节点视觉组件：名字+性别+生卒年+关系标签+身份/氏支标记，死人灰调，可选展开/折叠按钮。家族树窗与氏族大树共用此组件。本任务只做节点 prefab 本身（独立可测：给一个 FamilyTreeNode DTO，能渲染出文本/颜色）。

**Files:**
- Create: `Code/ui/items/FamilyTreeNodeView.cs`

**Interfaces:**
- Consumes: `FamilyTreeNode`（Task 1 扩充后的 DTO）；`Date.getYear(double)`→int。
- Produces:
  - `FamilyTreeNodeView`（MonoBehaviour）。方法：
    - `void Bind(FamilyTreeNode pNode, string pRelationLabel, System.Action<long> pOnClick, System.Action pOnToggle, bool pHasChildren, bool pExpanded)`。
    - 静态工厂 `static FamilyTreeNodeView Create(Transform pParent)`。

- [ ] **Step 1: 写节点视图组件**

Create `Code/ui/items/FamilyTreeNodeView.cs`：

```csharp
using System;
using AncientWarfare3.core.lineage;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    /// <summary>
    ///     家族树/氏族大树共用的节点视图。显示:[关系] 名 性别 (生-卒) 身份。
    ///     死者整体灰调。有子节点时显示展开/折叠按钮(懒加载)。
    /// </summary>
    internal class FamilyTreeNodeView : MonoBehaviour
    {
        private Text _label;
        private Button _nodeButton;
        private Button _toggleButton;
        private Text _toggleText;

        private static readonly Color AliveColor = Color.white;
        private static readonly Color DeadColor = new Color(0.55f, 0.55f, 0.55f, 1f);

        public static FamilyTreeNodeView Create(Transform pParent)
        {
            var obj = new GameObject("FamilyTreeNodeView", typeof(RectTransform));
            obj.transform.SetParent(pParent, false);
            var rect = obj.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(240, 22);
            var le = obj.AddComponent<LayoutElement>();
            le.minHeight = 22;
            le.preferredHeight = 22;
            var hl = obj.AddComponent<HorizontalLayoutGroup>();
            hl.childControlWidth = false;
            hl.childControlHeight = true;
            hl.childForceExpandWidth = false;
            hl.spacing = 4;

            var view = obj.AddComponent<FamilyTreeNodeView>();
            view.BuildUi();
            return view;
        }

        private void BuildUi()
        {
            // 展开/折叠按钮
            var toggleObj = new GameObject("Toggle", typeof(RectTransform), typeof(Text), typeof(Button));
            toggleObj.transform.SetParent(transform, false);
            toggleObj.GetComponent<RectTransform>().sizeDelta = new Vector2(16, 22);
            _toggleText = toggleObj.GetComponent<Text>();
            _toggleText.font = LocalizedTextManager.current_font;
            _toggleText.fontSize = 12;
            _toggleText.alignment = TextAnchor.MiddleCenter;
            _toggleText.color = Color.white;
            _toggleButton = toggleObj.GetComponent<Button>();

            // 主标签(点击=以此人重新居中/查家族树)
            var labelObj = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(Button));
            labelObj.transform.SetParent(transform, false);
            labelObj.GetComponent<RectTransform>().sizeDelta = new Vector2(220, 22);
            _label = labelObj.GetComponent<Text>();
            _label.font = LocalizedTextManager.current_font;
            _label.fontSize = 11;
            _label.alignment = TextAnchor.MiddleLeft;
            _nodeButton = labelObj.GetComponent<Button>();
        }

        public void Bind(FamilyTreeNode pNode, string pRelationLabel, Action<long> pOnClick,
            Action pOnToggle, bool pHasChildren, bool pExpanded)
        {
            string birth = pNode.birth_time > 0 ? Date.getYear(pNode.birth_time).ToString() : "?";
            string death = pNode.is_alive ? "" : (pNode.death_time > 0 ? Date.getYear(pNode.death_time).ToString() : "?");
            string life = pNode.is_alive ? "(" + birth + "- )" : "(" + birth + "-" + death + ")";
            string sex = pNode.sex == 0 ? "♂" : "♀";
            string identity = IdentityLabel(pNode.status);
            string prefix = string.IsNullOrEmpty(pRelationLabel) ? "" : "[" + pRelationLabel + "] ";

            _label.text = prefix + pNode.display_name + " " + sex + " " + life + " " + identity;
            _label.color = pNode.is_alive ? AliveColor : DeadColor;

            _nodeButton.onClick.RemoveAllListeners();
            long id = pNode.id;
            _nodeButton.onClick.AddListener(() => pOnClick?.Invoke(id));

            _toggleButton.onClick.RemoveAllListeners();
            if (pHasChildren && pOnToggle != null)
            {
                _toggleText.text = pExpanded ? "−" : "+";
                _toggleButton.gameObject.SetActive(true);
                _toggleButton.onClick.AddListener(() => pOnToggle.Invoke());
            }
            else
            {
                _toggleText.text = "";
                _toggleButton.gameObject.SetActive(false);
            }
        }

        private static string IdentityLabel(string pStatus)
        {
            if (pStatus == LineageStatus.NOBLE) return "贵";
            if (pStatus == LineageStatus.COMMON) return "平";
            if (pStatus == LineageStatus.SLAVE) return "奴";
            return "";
        }
    }
}
```

（注：性别符号 ♂♂♀、身份用单字"贵/平/奴"。生卒年用 `Date.getYear(double)`。死者整行变灰是核心需求。配偶连线/年号/氏支分封标记按 spec 属"可选补齐",此处先做关系标签+身份+生卒+灰调,后续可在 `Bind` 扩展。）

- [ ] **Step 2: 编译验证**

Run: `dotnet build -t:Rebuild --no-incremental`
Expected: 0 错 0 警。

- [ ] **Step 3: Commit**

```bash
git add Code/ui/items/FamilyTreeNodeView.cs
git commit -m "家族树节点视图FamilyTreeNodeView:名+性别+生卒+关系标签+身份单字,死者灰调,展开/折叠按钮(家族树与大树共用)"
```

---

## Task 6: 家族树/氏族大树窗口（FamilyTreeWindow，双模式+懒加载）

统一的树窗口。两种入口模式：家族树（以某 actor 居中，父母/本人/子女三层）、氏族大树（以氏支始祖为根，懒加载折叠下钻）。侧栏「回氏族大树」按钮在家族树模式可切到大树。接通 Task 3/4 的占位跳转。

**Files:**
- Create: `Code/ui/windows/FamilyTreeWindow.cs`
- Modify: `Code/ui/items/ShiBranchListItem.cs`（OnClick 改回真跳转）

**Interfaces:**
- Consumes: `LineageQuery.GetFamilyTree(long)`→`FamilyTreeNode`；`LineageQuery.GetChildIds(long)`/`GetParentIds(long)`；`LineageQuery.GetShiBranchFounderId(long)`；`FamilyTreeNodeView.Create`/`Bind`。
- Produces:
  - `FamilyTreeWindow.OpenBigTree(long pShiId)`（static，氏族大树模式）。
  - `FamilyTreeWindow.OpenFamilyTree(long pCenterActorId, long pShiIdForBackButton)`（static，家族树模式；pShiIdForBackButton 供"回大树"按钮，可 -1）。
  - `FamilyTreeWindow` 是 `AbstractWindow<FamilyTreeWindow>`。

- [ ] **Step 1: 写树窗口（懒加载折叠）**

Create `Code/ui/windows/FamilyTreeWindow.cs`：

```csharp
using System.Collections.Generic;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    /// <summary>
    ///     家族树 / 氏族大树(双模式)。
    ///     - 家族树:以某 actor 居中,显示父母+本人+子女三层。点节点→以该节点重新居中。
    ///     - 氏族大树:以氏支始祖为根,懒加载折叠下钻(默认展开 DEFAULT_DEPTH 层)。点节点→进该节点家族树。
    /// </summary>
    internal class FamilyTreeWindow : AbstractWindow<FamilyTreeWindow>
    {
        private const int DEFAULT_DEPTH = 2;   // 大树默认展开层数

        private enum Mode { Family, BigTree }
        private static Mode _mode;
        private static long _centerActorId = -1;
        private static long _rootActorId = -1;      // 大树根(始祖)
        private static long _backShiId = -1;        // 家族树模式下"回大树"用的氏支

        private readonly HashSet<long> _expanded = new HashSet<long>();
        private readonly List<FamilyTreeNodeView> _spawned = new List<FamilyTreeNodeView>();
        private Transform _treeRoot;
        private Button _backButton;
        private Text _backText;

        public static void OpenBigTree(long pShiId)
        {
            long founder = LineageQuery.GetShiBranchFounderId(pShiId);
            if (founder < 0) return;
            _mode = Mode.BigTree;
            _rootActorId = founder;
            _backShiId = pShiId;
            EnsureCreated();
            ScrollWindow.showWindow(AW_LineageWindowIds.FAMILY_TREE);
            if (Instance != null) Instance.Rebuild();
        }

        public static void OpenFamilyTree(long pCenterActorId, long pShiIdForBackButton)
        {
            _mode = Mode.Family;
            _centerActorId = pCenterActorId;
            _backShiId = pShiIdForBackButton;
            EnsureCreated();
            ScrollWindow.showWindow(AW_LineageWindowIds.FAMILY_TREE);
            if (Instance != null) Instance.Rebuild();
        }

        private static void EnsureCreated()
        {
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.FAMILY_TREE);
        }

        protected override void Init()
        {
            // "回氏族大树"按钮(家族树模式可见)
            var btnObj = new GameObject("BackToBigTree", typeof(RectTransform), typeof(Image), typeof(Button));
            btnObj.transform.SetParent(BackgroundTransform, false);
            var brect = btnObj.GetComponent<RectTransform>();
            brect.anchorMin = new Vector2(0, 1);
            brect.anchorMax = new Vector2(0, 1);
            brect.sizeDelta = new Vector2(120, 18);
            brect.anchoredPosition = new Vector2(70, -14);
            _backButton = btnObj.GetComponent<Button>();
            _backButton.onClick.AddListener(OnBack);
            var txtObj = new GameObject("Text", typeof(RectTransform), typeof(Text));
            txtObj.transform.SetParent(btnObj.transform, false);
            var trect = txtObj.GetComponent<RectTransform>();
            trect.anchorMin = Vector2.zero; trect.anchorMax = Vector2.one; trect.sizeDelta = Vector2.zero;
            _backText = txtObj.GetComponent<Text>();
            _backText.font = LocalizedTextManager.current_font;
            _backText.fontSize = 10;
            _backText.alignment = TextAnchor.MiddleCenter;
            _backText.color = Color.white;
            _backText.text = "← 回氏族大树";

            _treeRoot = ContentTransform;
        }

        public override void OnNormalEnable()
        {
            Rebuild();
        }

        private void OnBack()
        {
            if (_backShiId >= 0) OpenBigTree(_backShiId);
        }

        private void Rebuild()
        {
            ClearSpawned();
            _backButton.gameObject.SetActive(_mode == Mode.Family && _backShiId >= 0);

            if (_mode == Mode.Family)
                BuildFamilyView();
            else
                BuildBigTreeView();
        }

        // ── 家族树:父母 / 本人 / 子女 三层 ──
        private void BuildFamilyView()
        {
            var center = LineageQuery.GetFamilyTree(_centerActorId);
            if (center == null) return;

            foreach (var p in center.parents)
                Spawn(p, "父母", 1, false);

            Spawn(center, "本人", 0, false);

            foreach (var c in center.children)
                Spawn(c, "子女", 2, false);
        }

        // ── 氏族大树:始祖为根,懒加载折叠 ──
        private void BuildBigTreeView()
        {
            var root = BuildTreeNode(_rootActorId);
            if (root == null) return;
            // 默认展开 DEFAULT_DEPTH 层
            PreExpand(_rootActorId, 0);
            RenderSubtree(root, 0, "始祖");
        }

        private void PreExpand(long pId, int pDepth)
        {
            if (pDepth >= DEFAULT_DEPTH) return;
            _expanded.Add(pId);
            foreach (var cid in LineageQuery.GetChildIds(pId))
                PreExpand(cid, pDepth + 1);
        }

        private void RenderSubtree(FamilyTreeNode pNode, int pIndent, string pRelation)
        {
            var children = LineageQuery.GetChildIds(pNode.id);
            bool hasChildren = children.Count > 0;
            bool expanded = _expanded.Contains(pNode.id);

            var view = FamilyTreeNodeView.Create(_treeRoot);
            view.Bind(pNode, pRelation, OnNodeClick,
                hasChildren ? (System.Action)(() => ToggleExpand(pNode.id)) : null,
                hasChildren, expanded);
            ApplyIndent(view, pIndent);
            _spawned.Add(view);

            if (!expanded) return;
            foreach (var cid in children)
            {
                var cn = BuildTreeNode(cid);
                if (cn != null) RenderSubtree(cn, pIndent + 1, "");
            }
        }

        private void ToggleExpand(long pId)
        {
            if (_expanded.Contains(pId)) _expanded.Remove(pId);
            else _expanded.Add(pId);
            Rebuild();
        }

        // 大树点节点→进该节点家族树(保留 back 到本氏支大树)
        private void OnNodeClick(long pActorId)
        {
            if (_mode == Mode.BigTree)
                OpenFamilyTree(pActorId, _backShiId);
            else
                OpenFamilyTree(pActorId, _backShiId); // 家族树内点节点→重新居中
        }

        /// <summary>单节点 DTO(无父母/子女展开,只本体)。复用 GetFamilyTree 的本体部分。</summary>
        private FamilyTreeNode BuildTreeNode(long pId)
        {
            var n = LineageQuery.GetFamilyTree(pId);
            if (n == null) return null;
            // GetFamilyTree 会带 parents/children,大树渲染只用本体字段,清空避免混淆
            n.parents.Clear();
            n.children.Clear();
            return n;
        }

        private void ApplyIndent(FamilyTreeNodeView pView, int pIndent)
        {
            var le = pView.GetComponent<LayoutElement>();
            if (le != null) le.minWidth = 240 + pIndent * 14;
            var hl = pView.GetComponent<HorizontalLayoutGroup>();
            if (hl != null) hl.padding = new RectOffset(pIndent * 14, 0, 0, 0);
        }

        private void Spawn(FamilyTreeNode pNode, string pRelation, int pIndent, bool pToggle)
        {
            var view = FamilyTreeNodeView.Create(_treeRoot);
            view.Bind(pNode, pRelation, OnNodeClick, null, false, false);
            ApplyIndent(view, pIndent);
            _spawned.Add(view);
        }

        private void ClearSpawned()
        {
            foreach (var v in _spawned)
                if (v != null) Destroy(v.gameObject);
            _spawned.Clear();
        }
    }
}
```

（注：懒加载体现在 `RenderSubtree` —— 未展开的节点不递归实例化子树；`PreExpand` 只预置 DEFAULT_DEPTH 层的展开集。点 toggle → `Rebuild` 重画。家族树模式点节点 = 以该人重新居中（`OpenFamilyTree` 重设 center）。大树点节点 = 进该人家族树。两种树共用 `FamilyTreeNodeView`。`Destroy` 重建简单可靠；几十节点足够，符合 spec 懒加载折叠"任意时刻数十节点"。）

- [ ] **Step 2: 把 ShiBranchListItem 接回真跳转**

修改 `Code/ui/items/ShiBranchListItem.cs` 的 `OnClick`：

```csharp
        private void OnClick()
        {
            if (_shiId < 0) return;
            windows.FamilyTreeWindow.OpenBigTree(_shiId);
        }
```

- [ ] **Step 3: 编译验证**

Run: `dotnet build -t:Rebuild --no-incremental`
Expected: 0 错 0 警。

- [ ] **Step 4: Commit**

```bash
git add Code/ui/windows/FamilyTreeWindow.cs Code/ui/items/ShiBranchListItem.cs
git commit -m "家族树/氏族大树窗FamilyTreeWindow:家族树三层+大树懒加载折叠(默认2层,点+展开),回大树按钮,氏支→大树接通"
```

---

## Task 7: 信息窗注入（AW_UnitWindowPatch）

Postfix `UnitWindow.showStatsRows`，对 Xia 有谱系者注入身份行、姓行、氏行（可点击跳转）、家族树按钮行。按合流前/后 + 身份分支显示。

**Files:**
- Create: `Code/patch/AW_UnitWindowPatch.cs`

**Interfaces:**
- Consumes: `LineageService.IsXia(Actor)`；`Actor.data.get(...)`；`LineageKeys`/`LineageStatus`；`StatsWindow.showStatRow(...)`→`KeyValueField`（含 `on_click_value`）；`ShiBranchListWindow.OpenFor`/`FamilyTreeWindow.OpenBigTree`/`FamilyTreeWindow.OpenFamilyTree`；kingdom 合流标记 `kingdom.data.get(LineageKeys.KINGDOM_INTEGRATED, ...)`。
- Produces: `AW_UnitWindowPatch`（`[HarmonyPatch]`，Postfix）。无对外 API。

- [ ] **Step 1: 写信息窗 patch**

Create `Code/patch/AW_UnitWindowPatch.cs`：

```csharp
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.windows;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     Postfix UnitWindow.showStatsRows:Xia 有谱系者注入身份/姓/氏/家族树入口。
    ///     - 合流前贵族:身份「贵族」+ 姓(点→氏支列表) + 氏(点→氏族大树)
    ///     - 合流前平民/奴隶谱系:只身份(不显姓氏)
    ///     - 合流后:身份 + 氏(点→氏族大树),姓隐藏
    ///     - 一律加「家族树」按钮(点→本人家族树)
    /// </summary>
    [HarmonyPatch]
    public static class AW_UnitWindowPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(UnitWindow), nameof(UnitWindow.showStatsRows))]
        public static void ShowStatsRows_Postfix(UnitWindow __instance)
        {
            var actor = Config.selectedUnit;
            if (actor == null || actor.data == null) return;
            if (!LineageService.IsXia(actor)) return;

            actor.data.get(LineageKeys.LINEAGE_ID, out long lineageId, -1L);
            if (lineageId < 0) return; // 无谱系记录不注入

            actor.data.get(LineageKeys.LINEAGE_STATUS, out string status, LineageStatus.NONE);
            actor.data.get(LineageKeys.FAMILY_NAME, out string family, "");
            actor.data.get(LineageKeys.CLAN_NAME, out string clan, "");
            actor.data.get(LineageKeys.SHI_ID, out long shiId, -1L);

            bool integrated = IsKingdomIntegrated(actor);

            // 身份行
            __instance.showStatRow("aw_identity", IdentityText(status));

            bool isNoble = status == LineageStatus.NOBLE;

            // 姓行(合流前贵族显示,点→该姓氏支列表)
            if (!integrated && isNoble && !string.IsNullOrEmpty(family))
            {
                var kvf = __instance.showStatRow("aw_family_name", family);
                if (kvf != null)
                {
                    string f = family;
                    kvf.on_click_value = () => ShiBranchListWindow.OpenFor(f);
                }
            }

            // 氏行(合流前贵族 或 合流后所有人;点→氏族大树)
            if ((isNoble || integrated) && !string.IsNullOrEmpty(clan) && shiId >= 0)
            {
                var kvf = __instance.showStatRow("aw_clan_name", clan);
                if (kvf != null)
                {
                    long s = shiId;
                    kvf.on_click_value = () => FamilyTreeWindow.OpenBigTree(s);
                }
            }

            // 家族树按钮行(有谱系者一律有,点→本人家族树,back 到本氏支)
            var treeRow = __instance.showStatRow("aw_family_tree_entry", "查看家族树");
            if (treeRow != null)
            {
                long center = actor.data.id;
                long backShi = shiId;
                treeRow.on_click_value = () => FamilyTreeWindow.OpenFamilyTree(center, backShi);
            }
        }

        private static bool IsKingdomIntegrated(Actor pActor)
        {
            var kingdom = pActor.kingdom;
            if (kingdom == null || kingdom.data == null) return false;
            kingdom.data.get(LineageKeys.KINGDOM_INTEGRATED, out bool integrated, false);
            return integrated;
        }

        private static string IdentityText(string pStatus)
        {
            if (pStatus == LineageStatus.NOBLE) return "贵族";
            if (pStatus == LineageStatus.COMMON) return "平民谱系";
            if (pStatus == LineageStatus.SLAVE) return "奴隶谱系";
            return "无";
        }
    }
}
```

（注：`Config.selectedUnit` 是当前信息窗显示的 actor；比 patch 方法取 __instance 的 meta 更直接，AW2 也这么用。`showStatRow` 第一个参数是 localization key，未注册时原版会原样显示 key —— 但我们传的是中文值在 pValue,key 仅作行标识;若想 label 也中文，需注册 localization,本任务先用 key 占位 label，进游戏看效果再补本地化（spec 风险节已许可降级）。`on_click_value` 闭包捕获局部变量副本避免循环变量陷阱。）

- [ ] **Step 2: 编译验证**

Run: `dotnet build -t:Rebuild --no-incremental`
Expected: 0 错 0 警。

- [ ] **Step 3: Commit**

```bash
git add Code/patch/AW_UnitWindowPatch.cs
git commit -m "信息窗注入AW_UnitWindowPatch:Xia有谱系者显示身份/姓(点→氏支列表)/氏(点→大树)/家族树按钮,合流前后分支"
```

---

## Task 8: Kingdom 窗注入（AW_KingdomWindowPatch）

Postfix `KingdomWindow.showStatsRows`，注入继承人（头像+点击跳 inspect）、年号、头衔三行。逻辑后端已完成，UI 只读取显示。

**Files:**
- Create: `Code/patch/AW_KingdomWindowPatch.cs`

**Interfaces:**
- Consumes: `HeirService.GetHeir(Kingdom)`→Actor；`YearNameService.GetYearName(Kingdom)`→string；`KingdomTitleService.GetTitleString(Kingdom)`→string；`StatsWindow.showStatRow`/`KeyValueField.on_click_value`；`ScrollWindow.showWindow("inspect_unit")`。
- Produces: `AW_KingdomWindowPatch`（`[HarmonyPatch]`，Postfix）。

- [ ] **Step 1: 写 kingdom 窗 patch**

Create `Code/patch/AW_KingdomWindowPatch.cs`：

```csharp
using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     Postfix KingdomWindow.showStatsRows:注入继承人 / 年号 / 头衔三行(后端已算好,UI 只读)。
    ///     继承人行可点击 → inspect 该继承人。
    /// </summary>
    [HarmonyPatch]
    public static class AW_KingdomWindowPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(KingdomWindow), nameof(KingdomWindow.showStatsRows))]
        public static void ShowStatsRows_Postfix(KingdomWindow __instance)
        {
            var kingdom = Config.selectedKingdom;
            if (kingdom == null || kingdom.data == null) return;

            // 年号
            string yearName = YearNameService.GetYearName(kingdom);
            if (!string.IsNullOrEmpty(yearName))
                __instance.showStatRow("aw_year_name", yearName);

            // 头衔
            string title = KingdomTitleService.GetTitleString(kingdom);
            if (!string.IsNullOrEmpty(title))
                __instance.showStatRow("aw_kingdom_title", title);

            // 继承人(可点击 → inspect)
            var heir = HeirService.GetHeir(kingdom);
            if (heir != null && !heir.isRekt())
            {
                var kvf = __instance.showStatRow("aw_heir", heir.getName());
                if (kvf != null)
                {
                    var h = heir;
                    kvf.on_click_value = () =>
                    {
                        Config.selectedUnit = h;
                        ScrollWindow.showWindow("inspect_unit");
                    };
                }
            }
        }
    }
}
```

（注：spec 提到继承人用小头像 `UiUnitAvatarElement.show`。但 `showStatRow` 行内嵌头像需额外组件挂接，复杂度高；本任务先用"继承人名 + 点击跳 inspect"文本行（信息等价，验证导航通），头像增强留作进游戏后的可选迭代——符合 spec"先验证信息，UI 细节进游戏调"。`Config.selectedKingdom` 是当前 kingdom 窗对象;`inspect_unit` 是原版单位窗 id。`heir.isRekt()` 防继承人已死的竞态。）

- [ ] **Step 2: 编译验证**

Run: `dotnet build -t:Rebuild --no-incremental`
Expected: 0 错 0 警。若 `inspect_unit` 窗口 id 名不对（原版可能叫 `"unit"`），按编译/运行结果改；编译期不会报字符串错，留待游戏验证时确认。

- [ ] **Step 3: Commit**

```bash
git add Code/patch/AW_KingdomWindowPatch.cs
git commit -m "Kingdom窗注入AW_KingdomWindowPatch:年号+头衔+继承人(点→inspect)三行,读后端服务"
```

---

## Task 9: 替换原版家族树（AW_GenealogyReplacePatch）

让"有谱系记录的 Xia 贵族/谱系者"点原版族谱时打开我们的持久化家族树，其他人放行原版。

**Files:**
- Create: `Code/patch/AW_GenealogyReplacePatch.cs`

**Interfaces:**
- Consumes: 原版打开 family 窗的入口方法（`FamilyWindow.OnEnable` 或 `ScrollWindow.showWindow` 对 `"family"` 的调用）；`Config.selectedUnit`；`LineageService.IsXia`；`FamilyTreeWindow.OpenFamilyTree`。
- Produces: `AW_GenealogyReplacePatch`（`[HarmonyPatch]`，Prefix return false 接管）。

- [ ] **Step 1: 核实原版打开 family 窗的确切入口**

Run（在 git-bash）：
```bash
SRC="F:/WorldBox New Mod/AssetRipper_export_20260628_163320/ExportedProject/Assets/Scripts/Assembly-CSharp"
grep -nE "showWindow\(\"family\"|class FamilyWindow|void OnEnable|loadFamily|genealogy" "$SRC/FamilyWindow.cs" "$SRC/UnitWindow.cs" | head -30
```
Expected: 找到 family 窗打开点。**实现据此定 patch 目标**：优先 patch `FamilyWindow` 的 `OnEnable`/`show`（Prefix），按 `Config.selectedUnit` 判定。

- [ ] **Step 2: 写替换 patch**

Create `Code/patch/AW_GenealogyReplacePatch.cs`（以 patch `FamilyWindow` 显示入口为例，方法名按 Step 1 结果填）：

```csharp
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.windows;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     替换原版"死后即消失"的家族树:当被查看单位是 Xia 且有谱系记录(aw_lineage_id>=0),
    ///     接管原版 family 窗,改开我们的持久化家族树(含死人)。其他单位放行原版。
    ///
    ///     patch 目标 = 原版 FamilyWindow 的显示入口(由 Task9 Step1 grep 确定确切方法名)。
    ///     若该入口难精确接管,降级为 patch UnitWindow 打开 family 的上层调用。
    /// </summary>
    [HarmonyPatch]
    public static class AW_GenealogyReplacePatch
    {
        // 占位:实际方法名/类型由 Step1 grep 结果替换。示例针对 FamilyWindow.showWindow 风格入口。
        [HarmonyPrefix]
        [HarmonyPatch(typeof(FamilyWindow), "OnEnable")]
        public static bool FamilyWindow_OnEnable_Prefix()
        {
            var actor = Config.selectedUnit;
            if (actor == null || actor.data == null) return true;   // 放行原版
            if (!LineageService.IsXia(actor)) return true;

            actor.data.get(LineageKeys.LINEAGE_ID, out long lineageId, -1L);
            if (lineageId < 0) return true;                          // 无谱系→放行原版

            actor.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
            // 关掉原版 family 窗(若已在打开流程),改开我们的
            ScrollWindow.get("family")?.clickHide();
            FamilyTreeWindow.OpenFamilyTree(actor.data.id, shiId);
            return false;                                            // 接管,跳过原版
        }
    }
}
```

（注：这是**最高不确定性任务**。`FamilyWindow.OnEnable` 是否存在、能否 Prefix、`Config.selectedUnit` 在此刻是否仍是目标 actor，须以 Step 1 grep 为准。降级路径见 spec 风险节：改 patch `UnitWindow` 中点族谱 tab 的回调，或 patch `ScrollWindow.showWindow` 对 `"family"` id 的调用并按 selectedUnit 改派。`ScrollWindow.get(id)?.clickHide()` 关原版窗——方法名若不对按原版 API 改。`return false` 跳过原版方法体。）

- [ ] **Step 3: 编译验证**

Run: `dotnet build -t:Rebuild --no-incremental`
Expected: 0 错 0 警。若 `FamilyWindow.OnEnable`/`clickHide` 不存在编译报错，按 Step 1 grep 的真实成员名修正后重编。

- [ ] **Step 4: Commit**

```bash
git add Code/patch/AW_GenealogyReplacePatch.cs
git commit -m "替换原版家族树AW_GenealogyReplacePatch:Xia有谱系者点族谱→开我们持久化家族树(含死人),其他人放行原版"
```

---

## Task 10: 全量编译 + 集成自检 + 收尾提交

整套 UI 完成后全量重建，确认 0 错 0 警，并回填前序任务里"待后续任务接回"的占位（确保无残留占位跳转）。

**Files:**
- 复查所有 Task 中标注"占位/待 Task N"的位置已替换。

- [ ] **Step 1: 占位残留扫描**

Run（git-bash）：
```bash
cd "F:/WorldBox New Mod/AncientWarfare3.0"
grep -rnE "待 Task|占位|LogInfo\(\"\[AW3\] (点击姓|点击氏支)" Code/ui/ || echo "无占位残留"
```
Expected: `无占位残留`。若有，对照 Task 3/4 Step 把 `OnClick` 改回真跳转（`ShiBranchListWindow.OpenFor` / `FamilyTreeWindow.OpenBigTree`）。

- [ ] **Step 2: 全量重建**

Run: `dotnet build -t:Rebuild --no-incremental`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`。

- [ ] **Step 3: 自检导航闭环（对照 spec 验收）**

人工核对（读代码确认调用链存在，非运行）：
- 「姓族」tab → `AW_LineageTab` 按钮 → `LineageOverviewWindow.Open` ✓
- 姓行点击 → `ShiBranchListWindow.OpenFor` ✓
- 氏支行点击 → `FamilyTreeWindow.OpenBigTree` ✓
- 大树节点点击 → `FamilyTreeWindow.OpenFamilyTree` ✓
- inspect 窗姓/氏/家族树按钮 → 三入口 ✓
- kingdom 窗继承人/年号/头衔 ✓
- 原版族谱接管 ✓

- [ ] **Step 4: 收尾提交（若 Step 1 有修改）**

```bash
git add -A
git commit -m "姓族UI集成收尾:占位跳转全部接回真窗口,全量重建0错0警"
```

- [ ] **Step 5: 通知用户进游戏验证**

输出提示：阶段5 UI 全部完成、编译 0 错、本地已提交未推送。请用户进游戏验证 spec 验收标准（tab 开总览 / 点姓见成员含死者 / 点人开家族树 / 杀人存档读档后死者亲缘仍在 / kingdom 窗继承人年号头衔）。验证通过后再推送。

---

## Self-Review

**1. Spec coverage（逐节核对）：**
- 整体架构（混合路线）→ Task 2-9 ✓
- 四级导航（姓→氏支列表→氏族大树→家族树）→ Task 3/4/6 ✓
- inspect 三入口（点姓→氏支列表/点氏→大树/家族树按钮）→ Task 7 ✓
- 信息窗合流前后显示规则 → Task 7（按 status+integrated 分支）✓
- kingdom 窗继承人/年号/头衔 → Task 8 ✓
- 替换原版家族树（仅有谱系贵族）→ Task 9 ✓
- 持久化+死人灰调+生卒年 → Task 1（数据）+ Task 5（灰调渲染）✓
- 氏族大树懒加载折叠（默认展开 N 层）→ Task 6 ✓
- 数据层补 4 方法 → Task 1 补 `GetShiBranchFounderId`/`GetShiBranchInfo`；`GetParentIds`/`GetChildIds`/`GetChildrenArchive` 已存在（GetChildIds 即懒加载下一层用，无需 GetChildrenArchive 重复）；`GetSpouseArchive` 配偶 spec 已许可留桩降级，本计划不实现配偶连线（Task 5 节点不画配偶）✓
- 统计字段（姓行总/活/贵/氏支数；氏支行总/活/立年/贵）→ Task 3/4 ✓
- 窗口上下文传递（静态 SetContext/OpenFor）→ Task 4/6（OpenFor/OpenBigTree/OpenFamilyTree 静态入口）✓
- 文件清单 → 与 File Structure 一致（UI 节点改名 `FamilyTreeNodeView` 避开 DTO `FamilyTreeNode` 同名）✓

**2. Placeholder scan：** Task 3/4 故意保留"先占位再 Task N 接回"的 TDD 节奏，Task 10 Step 1 强制扫除残留 → 闭环无遗留。无 "TBD/TODO/实现later" 抽象占位，所有代码步均给完整代码。Task 9 的 patch 目标方法名依赖 Step 1 grep —— 已显式标注"最高不确定性+降级路径"，非抽象占位。

**3. Type consistency：**
- `FamilyTreeNode`(DTO) vs `FamilyTreeNodeView`(UI) —— 明确区分，无同名冲突 ✓
- `SurnameOverview`/`ShiBranchInfo`/`MemberInfo` 字段名与现有 DTO 一致（family_name/total/alive/noble/shi_count/created_time/clan_name）✓
- `OpenFor(string)`/`OpenBigTree(long)`/`OpenFamilyTree(long,long)`/`Open()` 静态方法签名跨任务一致 ✓
- `AbstractListWindow<T,TItem>.CreateItemPrefab` 返回 `AbstractListWindowItem<TItem>`，Task 3/4 实现签名匹配 ✓
- `LineageQuery` 新增 `GetShiBranchFounderId`/`GetShiBranchInfo` 在 Task 1 定义，Task 6 消费 ✓

修正记录：File Structure 的 `FamilyTreeNode.cs` UI 文件已更名 `FamilyTreeNodeView.cs`（与 DTO 区分）；spec 提的 `GetChildrenArchive`/`GetParentsArchive`/`GetSpouseArchive` 中，前两者功能已由现有 `GetChildIds`/`GetParentIds`+`BuildNode` 覆盖（不重复造），配偶降级不实现 —— 已在 Self-Review 注明。

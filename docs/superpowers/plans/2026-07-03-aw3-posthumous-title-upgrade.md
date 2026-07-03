# AW3 Posthumous Title Upgrade Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Upgrade AW3's non-Tianming posthumous title system so ordinary kings, dukes, marquises, counts, and barons receive explainable titles based on their actual reign records, with better scoring and historical fit than shishu1.2.0.

**Architecture:** Keep the feature inside the existing chronology stack: `ReignRecordWriter` captures reign snapshots, `PosthumousTitleService` evaluates and writes titles, `HistoryWriter` records the event, and `HistoryListWindow` displays tooltip detail. Temple names such as 太祖/高祖/世祖/烈祖, Tianming emperor suffixes, qi feedback, and consecutive-bad-title punishment remain deferred to the future Tianming system.

**Tech Stack:** C# net48, NeoModLoader/NML, Harmony, existing AW3 SQLite reflection tables via `[TableDef]`, existing UI list tooltip system.

---

## Scope Boundary

Included now:
- Ordinary posthumous titles for any Xia kingdom ruler with a `KingdomReign` row.
- Suffix from AW3 title rank: 伯/侯/公/王/帝 through `KingdomTitleService.GetTitleChar`.
- Richer non-Tianming scoring: civil population, territory, war result, order/stability, ending.
- Special ordinary-title branches: founder bias, short reign, abdication, kingdom fall, death cause.
- Same-kingdom title character deduplication.
- Tooltip explanation for posthumous-title history rows.

Explicitly excluded and documented only:
- Temple names: 太祖/高祖/世祖/烈祖/中宗/肃宗/宪宗.
- Tianming/qi suffix logic: 皇帝/帝/王 based on qi.
- Qi feedback from title grade.
- Consecutive bad-title qi punishment.
- Tianming dynasty-only title display such as 太祖武皇帝.

## File Map

- Modify `Code/core/db/KingdomReignTableItem.cs`: add end-of-reign snapshots and ordinary-title metadata source fields.
- Modify `Code/core/db/PosthumousTitleTableItem.cs`: add score and reason fields for explanation.
- Modify `Code/core/lineage/ReignRecordWriter.cs`: capture start/end population, army, city count, founder flag, and death cause.
- Create `Code/core/lineage/PosthumousTitleDefs.cs`: title character pools, grades, dimensions, and helper structs.
- Rewrite focused parts of `Code/core/lineage/PosthumousTitleService.cs`: evaluation, title selection, duplicate guard, history event target and tooltip data.
- Modify `Code/core/lineage/ChronicleEvents.cs`: call the richer `CloseOpenReign(Kingdom, ...)` overload.
- Modify `Code/core/lineage/HistoryQuery.cs`: expose posthumous tooltip detail for history rows if needed.
- Modify `Code/ui/windows/HistoryListWindow.cs`: append posthumous score tooltip when event target points to a titled actor.
- Modify `README.md` and `HANDOFF.md`: document the split between ordinary posthumous titles and future Tianming temple-name system.

---

### Task 1: Extend Reign And Posthumous Tables

**Files:**
- Modify: `Code/core/db/KingdomReignTableItem.cs`
- Modify: `Code/core/db/PosthumousTitleTableItem.cs`

- [ ] **Step 1: Add end snapshot fields to `KingdomReignTableItem`**

Add these fields after `start_city_count`:

```csharp
public int start_army_count = 0;
public int end_population = 0;
public int end_city_count = 0;
public int end_army_count = 0;
public int is_founder = 0;
public int war_wins = 0;
public int war_losses = 0;
public int lost_capital = 0;
public string death_cause = "";
```

Rationale: AW3 currently stores start population/cities only. The new scoring needs both start and end snapshots. Reflection migration will auto-add columns to old saves.

- [ ] **Step 2: Add explanation fields to `PosthumousTitleTableItem`**

Add these fields after `score_detail`:

```csharp
public string grade = "";              // praise_high / praise / neutral / blame / blame_high
public string dominant_dimension = ""; // civil / territory / war / order / ending / balanced
public int score_civil = 0;
public int score_territory = 0;
public int score_war = 0;
public int score_order = 0;
public int score_ending = 0;
public int total_score = 0;
public string reason_text = "";
```

Keep existing `eval` for backward-compatible UI and old rows. New code writes both `eval` and `grade`.

- [ ] **Step 3: Build**

Run:

```powershell
& "C:\Program Files\dotnet\dotnet.exe" build
```

Expected: build succeeds. New table columns do not require manual SQL migration.

---

### Task 2: Capture Reign Start And End Data

**Files:**
- Modify: `Code/core/lineage/ReignRecordWriter.cs`
- Modify: `Code/core/lineage/ChronicleEvents.cs`

- [ ] **Step 1: Extend `ReignInfo`**

Add fields:

```csharp
public int StartArmyCount;
public int EndPopulation;
public int EndCityCount;
public int EndArmyCount;
public int IsFounder;
public string DeathCause;
```

- [ ] **Step 2: Add safe helpers**

Add these helpers near `SafePopulation` / `SafeCityCount`:

```csharp
private static int SafeArmyCount(Kingdom pKingdom)
{
    try { return pKingdom?.getArmy() ?? 0; }
    catch { return 0; }
}

private static string ReadDeathCause(Actor pActor)
{
    if (pActor?.data == null) return "";
    pActor.data.get(LineageKeys.DEATH_CAUSE, out string cause, "");
    return cause ?? "";
}
```

- [ ] **Step 3: Write start army and founder flag in `OpenReign`**

In `OpenReign`, after `int cities = SafeCityCount(pKingdom);`, add:

```csharp
int armies = SafeArmyCount(pKingdom);
int isFounder = idx == 1 ? 1 : 0;
```

Add insert columns:

```csharp
ColumnVal.Create("START_ARMY_COUNT", armies),
ColumnVal.Create("END_POPULATION", 0),
ColumnVal.Create("END_CITY_COUNT", 0),
ColumnVal.Create("END_ARMY_COUNT", 0),
ColumnVal.Create("IS_FOUNDER", isFounder),
ColumnVal.Create("WAR_WINS", 0),
ColumnVal.Create("WAR_LOSSES", 0),
ColumnVal.Create("LOST_CAPITAL", 0),
ColumnVal.Create("DEATH_CAUSE", "")
```

- [ ] **Step 4: Add rich close overload**

Add this overload above the existing `CloseOpenReign(long, string)`:

```csharp
public static ReignInfo CloseOpenReign(Kingdom pKingdom, string pReason, Actor pKing = null)
{
    if (pKingdom?.data == null) return ReignInfo.Empty;
    ReignInfo open = ReadOpenReignInfo(pKingdom.id);
    if (!open.IsValid) return ReignInfo.Empty;

    int endPop = SafePopulation(pKingdom);
    int endCities = SafeCityCount(pKingdom);
    int endArmy = SafeArmyCount(pKingdom);
    var (wins, losses) = WarRecordWriter.GetWarRecord(pKingdom.id, open.StartTime, World.world.getCurWorldTime());
    string deathCause = ReadDeathCause(pKing);

    try
    {
        DB.UpdateValue(TABLE,
            new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("REIGN_ID", open.ReignId) },
            ColumnVal.Create("END_TIME", World.world.getCurWorldTime()),
            ColumnVal.Create("END_REASON", pReason ?? ""),
            ColumnVal.Create("END_POPULATION", endPop),
            ColumnVal.Create("END_CITY_COUNT", endCities),
            ColumnVal.Create("END_ARMY_COUNT", endArmy),
            ColumnVal.Create("WAR_WINS", wins),
            ColumnVal.Create("WAR_LOSSES", losses),
            ColumnVal.Create("LOST_CAPITAL", pReason == "kingdom_fell" ? 1 : 0),
            ColumnVal.Create("DEATH_CAUSE", deathCause));
    }
    catch (Exception e)
    {
        ModClass.LogWarning("ReignRecordWriter.CloseOpenReign rich: " + e.Message);
        return ReignInfo.Empty;
    }

    open.EndPopulation = endPop;
    open.EndCityCount = endCities;
    open.EndArmyCount = endArmy;
    open.WarWins = wins;
    open.WarLosses = losses;
    open.DeathCause = deathCause;
    return open;
}
```

Keep the old `CloseOpenReign(long, string)` for compatibility.

- [ ] **Step 5: Update `ReadOpenReignInfo` query**

Select and fill the new fields:

```sql
SELECT REIGN_ID, KINGDOM_ID, KING_ACTOR_ID, START_POPULATION, START_CITY_COUNT,
       START_TIME, START_ARMY_COUNT, IS_FOUNDER
```

Assign:

```csharp
StartArmyCount = SafeInt64(r, 6),
IsFounder = SafeInt64(r, 7)
```

If local helpers do not exist, add:

```csharp
private static int SafeInt64(SQLiteDataReader pReader, int pIndex)
{
    try { return pReader.IsDBNull(pIndex) ? 0 : (int)pReader.GetInt64(pIndex); }
    catch { return 0; }
}
```

- [ ] **Step 6: Route chronology close calls through rich overload**

In `ChronicleEvents`:

```csharp
ReignRecordWriter.CloseOpenReign(pKingdom, "replaced");
ReignRecordWriter.CloseOpenReign(pKingdom, "kingdom_fell");
ReignRecordWriter.ReignInfo reign = ReignRecordWriter.CloseOpenReign(pKingdom, "died", pKing);
ReignRecordWriter.ReignInfo reign = ReignRecordWriter.CloseOpenReign(pKingdom, "abdicated", pKing);
```

Keep old overload untouched for any external code.

- [ ] **Step 7: Build**

Run:

```powershell
& "C:\Program Files\dotnet\dotnet.exe" build
```

Expected: build succeeds.

---

### Task 3: Add Title Definition Layer

**Files:**
- Create: `Code/core/lineage/PosthumousTitleDefs.cs`

- [ ] **Step 1: Create dimensions, grades, and title character definitions**

Create file:

```csharp
namespace AncientWarfare3.core.lineage
{
    internal enum PosthumousDimension
    {
        Civil,
        Territory,
        War,
        Order,
        Ending,
        Balanced
    }

    internal enum PosthumousGrade
    {
        PraiseHigh,
        Praise,
        Neutral,
        Blame,
        BlameHigh
    }

    internal readonly struct PosthumousTitleChar
    {
        public readonly string Char;
        public readonly PosthumousDimension Dimension;
        public readonly PosthumousGrade MinGrade;

        public PosthumousTitleChar(string pChar, PosthumousDimension pDimension, PosthumousGrade pMinGrade)
        {
            Char = pChar;
            Dimension = pDimension;
            MinGrade = pMinGrade;
        }
    }

    internal static class PosthumousTitleDefs
    {
        public static readonly string[] BadChars =
        {
            "厉", "幽", "灵", "炀", "荒", "戾", "暴", "废"
        };

        public static readonly PosthumousTitleChar[] Pool =
        {
            new PosthumousTitleChar("文", PosthumousDimension.Civil, PosthumousGrade.PraiseHigh),
            new PosthumousTitleChar("昭", PosthumousDimension.Civil, PosthumousGrade.Praise),
            new PosthumousTitleChar("景", PosthumousDimension.Civil, PosthumousGrade.Praise),
            new PosthumousTitleChar("康", PosthumousDimension.Civil, PosthumousGrade.Praise),
            new PosthumousTitleChar("惠", PosthumousDimension.Civil, PosthumousGrade.Praise),
            new PosthumousTitleChar("仁", PosthumousDimension.Civil, PosthumousGrade.Praise),
            new PosthumousTitleChar("成", PosthumousDimension.Civil, PosthumousGrade.Praise),

            new PosthumousTitleChar("武", PosthumousDimension.War, PosthumousGrade.PraiseHigh),
            new PosthumousTitleChar("桓", PosthumousDimension.War, PosthumousGrade.Praise),
            new PosthumousTitleChar("烈", PosthumousDimension.War, PosthumousGrade.Praise),
            new PosthumousTitleChar("威", PosthumousDimension.War, PosthumousGrade.Praise),
            new PosthumousTitleChar("襄", PosthumousDimension.War, PosthumousGrade.Praise),
            new PosthumousTitleChar("庄", PosthumousDimension.War, PosthumousGrade.Praise),

            new PosthumousTitleChar("平", PosthumousDimension.Order, PosthumousGrade.Neutral),
            new PosthumousTitleChar("安", PosthumousDimension.Order, PosthumousGrade.Neutral),
            new PosthumousTitleChar("恭", PosthumousDimension.Order, PosthumousGrade.Neutral),
            new PosthumousTitleChar("靖", PosthumousDimension.Order, PosthumousGrade.Neutral),
            new PosthumousTitleChar("简", PosthumousDimension.Order, PosthumousGrade.Neutral),
            new PosthumousTitleChar("顺", PosthumousDimension.Order, PosthumousGrade.Neutral),
            new PosthumousTitleChar("让", PosthumousDimension.Order, PosthumousGrade.Neutral),

            new PosthumousTitleChar("哀", PosthumousDimension.Ending, PosthumousGrade.Blame),
            new PosthumousTitleChar("闵", PosthumousDimension.Ending, PosthumousGrade.Blame),
            new PosthumousTitleChar("悼", PosthumousDimension.Ending, PosthumousGrade.Blame),
            new PosthumousTitleChar("怀", PosthumousDimension.Ending, PosthumousGrade.Blame),
            new PosthumousTitleChar("殇", PosthumousDimension.Ending, PosthumousGrade.Blame),

            new PosthumousTitleChar("厉", PosthumousDimension.Balanced, PosthumousGrade.BlameHigh),
            new PosthumousTitleChar("幽", PosthumousDimension.Balanced, PosthumousGrade.BlameHigh),
            new PosthumousTitleChar("灵", PosthumousDimension.Balanced, PosthumousGrade.BlameHigh),
            new PosthumousTitleChar("炀", PosthumousDimension.Balanced, PosthumousGrade.BlameHigh),
            new PosthumousTitleChar("荒", PosthumousDimension.Balanced, PosthumousGrade.BlameHigh),
            new PosthumousTitleChar("戾", PosthumousDimension.Balanced, PosthumousGrade.BlameHigh),
            new PosthumousTitleChar("暴", PosthumousDimension.Balanced, PosthumousGrade.BlameHigh),
            new PosthumousTitleChar("废", PosthumousDimension.Balanced, PosthumousGrade.BlameHigh)
        };
    }
}
```

This deliberately excludes 太祖/高祖/世祖/烈祖/中宗/肃宗/宪宗 because those are future Tianming temple names.

- [ ] **Step 2: Build**

Run:

```powershell
& "C:\Program Files\dotnet\dotnet.exe" build
```

Expected: build succeeds.

---

### Task 4: Rewrite The Non-Tianming Evaluation

**Files:**
- Modify: `Code/core/lineage/PosthumousTitleService.cs`

- [ ] **Step 1: Replace small title pools with the new defs**

Remove these arrays from `PosthumousTitleService`:

```csharp
private static readonly string[] GOOD_WAR ...
private static readonly string[] GOOD_RULE ...
private static readonly string[] MID ...
private static readonly string[] BAD ...
private static readonly string[] FALL ...
```

Keep:

```csharp
private static readonly System.Random Rng = new System.Random();
```

- [ ] **Step 2: Add evaluation struct**

Add near `TitleScore` or replace `TitleScore`:

```csharp
private struct PosthumousEvaluation
{
    public int Civil;
    public int Territory;
    public int War;
    public int Order;
    public int Ending;
    public int Total;
    public int Wins;
    public int Losses;
    public int CityDelta;
    public int ArmyDelta;
    public int Years;
    public bool Founder;
    public string DeathCause;
    public PosthumousGrade Grade;
    public PosthumousDimension Dominant;
    public string Reason;
}
```

- [ ] **Step 3: Implement score helpers**

Add:

```csharp
private static int ScorePopulation(int pStart, int pEnd)
{
    if (pStart <= 0) return 0;
    float rate = (pEnd - pStart) / (float)pStart;
    if (rate >= 0.50f) return 3;
    if (rate >= 0.25f) return 2;
    if (rate >= 0.05f) return 1;
    if (rate >= -0.05f) return 0;
    if (rate >= -0.25f) return -1;
    if (rate >= -0.50f) return -2;
    return -3;
}

private static int ScoreTerritory(int pCityDelta, string pEndReason)
{
    if (pEndReason == "kingdom_fell") return -3;
    if (pCityDelta >= 5) return 3;
    if (pCityDelta >= 2) return 2;
    if (pCityDelta >= 1) return 1;
    if (pCityDelta == 0) return 0;
    if (pCityDelta >= -2) return -1;
    if (pCityDelta >= -5) return -2;
    return -3;
}

private static int ScoreWar(int pWins, int pLosses)
{
    int total = pWins + pLosses;
    if (total <= 1) return 0;
    float rate = pWins / (float)total;
    if (rate >= 0.80f && total >= 3) return 3;
    if (rate >= 0.60f) return 2;
    if (rate >= 0.50f) return 1;
    if (rate < 0.25f && total >= 3) return -3;
    if (rate < 0.40f) return -2;
    return -1;
}

private static int ScoreOrder(int pYears, string pEndReason)
{
    int score = 0;
    if (pYears >= 60) score += 2;
    else if (pYears >= 20) score += 1;
    else if (pYears < 3) score -= 1;
    if (pEndReason == "abdicated") score += 1;
    if (pEndReason == "replaced") score -= 1;
    return ClampInt(score, -3, 3);
}

private static int ScoreEnding(string pEndReason, string pDeathCause)
{
    if (pEndReason == "kingdom_fell") return -3;
    if (pEndReason == "abdicated") return 1;
    if (!string.IsNullOrEmpty(pDeathCause) && pDeathCause.Contains("战")) return 1;
    if (!string.IsNullOrEmpty(pDeathCause) && pDeathCause.Contains("饿")) return -1;
    return 0;
}
```

Add `ClampInt`:

```csharp
private static int ClampInt(int pValue, int pMin, int pMax)
{
    if (pValue < pMin) return pMin;
    return pValue > pMax ? pMax : pValue;
}
```

- [ ] **Step 4: Implement `Evaluate`**

Use `ReignInfo` instead of live-only current kingdom state:

```csharp
private static PosthumousEvaluation Evaluate(Kingdom pKingdom, string pEndReason, ReignRecordWriter.ReignInfo pReign)
{
    int endPop = pReign.EndPopulation > 0 ? pReign.EndPopulation : SafePopulation(pKingdom);
    int endCities = pReign.EndCityCount > 0 ? pReign.EndCityCount : SafeCityCount(pKingdom);
    int endArmy = pReign.EndArmyCount > 0 ? pReign.EndArmyCount : SafeArmyCount(pKingdom);
    int cityDelta = endCities - pReign.StartCityCount;
    int armyDelta = endArmy - pReign.StartArmyCount;
    int years = Math.Max(1, Date.getYear(World.world.getCurWorldTime()) - Date.getYear(pReign.StartTime) + 1);

    int civil = ScorePopulation(pReign.StartPopulation, endPop);
    int territory = ScoreTerritory(cityDelta, pEndReason);
    int war = ScoreWar(pReign.WarWins, pReign.WarLosses);
    int order = ScoreOrder(years, pEndReason);
    int ending = ScoreEnding(pEndReason, pReign.DeathCause);
    int total = civil + territory + war + order + ending;

    PosthumousGrade grade = total >= 6 ? PosthumousGrade.PraiseHigh :
        total >= 2 ? PosthumousGrade.Praise :
        total >= -1 ? PosthumousGrade.Neutral :
        total >= -5 ? PosthumousGrade.Blame :
        PosthumousGrade.BlameHigh;

    PosthumousDimension dominant = DominantDimension(civil, territory, war, order, ending);
    string reason = $"民生{civil:+0;-0;0} 疆域{territory:+0;-0;0} 战功{war:+0;-0;0} " +
                    $"秩序{order:+0;-0;0} 结局{ending:+0;-0;0} " +
                    $"胜{pReign.WarWins}败{pReign.WarLosses} 城{cityDelta:+0;-0;0} 军{armyDelta:+0;-0;0}";

    return new PosthumousEvaluation
    {
        Civil = civil,
        Territory = territory,
        War = war,
        Order = order,
        Ending = ending,
        Total = total,
        Wins = pReign.WarWins,
        Losses = pReign.WarLosses,
        CityDelta = cityDelta,
        ArmyDelta = armyDelta,
        Years = years,
        Founder = pReign.IsFounder != 0,
        DeathCause = pReign.DeathCause ?? "",
        Grade = grade,
        Dominant = dominant,
        Reason = reason
    };
}
```

- [ ] **Step 5: Implement dominant dimension**

```csharp
private static PosthumousDimension DominantDimension(int pCivil, int pTerritory, int pWar, int pOrder, int pEnding)
{
    int max = Math.Max(Math.Abs(pCivil), Math.Max(Math.Abs(pTerritory), Math.Max(Math.Abs(pWar), Math.Max(Math.Abs(pOrder), Math.Abs(pEnding)))));
    int near = 0;
    if (max - Math.Abs(pCivil) <= 1) near++;
    if (max - Math.Abs(pTerritory) <= 1) near++;
    if (max - Math.Abs(pWar) <= 1) near++;
    if (max - Math.Abs(pOrder) <= 1) near++;
    if (max - Math.Abs(pEnding) <= 1) near++;
    if (near >= 3) return PosthumousDimension.Balanced;
    if (Math.Abs(pEnding) == max) return PosthumousDimension.Ending;
    if (Math.Abs(pWar) == max) return PosthumousDimension.War;
    if (Math.Abs(pTerritory) == max) return PosthumousDimension.Territory;
    if (Math.Abs(pCivil) == max) return PosthumousDimension.Civil;
    return PosthumousDimension.Order;
}
```

- [ ] **Step 6: Build**

Run:

```powershell
& "C:\Program Files\dotnet\dotnet.exe" build
```

Expected: build succeeds.

---

### Task 5: Select Better Ordinary Titles

**Files:**
- Modify: `Code/core/lineage/PosthumousTitleService.cs`

- [ ] **Step 1: Add duplicate guard**

At the start of `OnReignEnded`, after checking DB readiness:

```csharp
if (HasExistingTitle(pKing.data.id, pReign.ReignId)) return;
```

Add helper:

```csharp
private static bool HasExistingTitle(long pActorId, long pReignId)
{
    var db = LineageArchiveManager.Instance?.OperatingDB;
    if (db == null) return false;
    try
    {
        using var cmd = new System.Data.SQLite.SQLiteCommand(db);
        cmd.CommandText =
            $"SELECT 1 FROM {PosthumousTitleTableItem.GetTableName()} " +
            "WHERE ACTOR_ID=@actor OR REIGN_ID=@reign LIMIT 1";
        cmd.Parameters.AddWithValue("@actor", pActorId);
        cmd.Parameters.AddWithValue("@reign", pReignId);
        return cmd.ExecuteScalar() != null;
    }
    catch { return false; }
}
```

- [ ] **Step 2: Add same-kingdom used character query**

```csharp
private static HashSet<string> GetUsedTitleChars(long pKingdomId)
{
    var result = new HashSet<string>();
    var db = LineageArchiveManager.Instance?.OperatingDB;
    if (db == null) return result;
    try
    {
        using var cmd = new System.Data.SQLite.SQLiteCommand(db);
        cmd.CommandText =
            $"SELECT TITLE_CHAR FROM {PosthumousTitleTableItem.GetTableName()} " +
            "WHERE KINGDOM_ID=@kid AND IFNULL(TITLE_CHAR, '')<>''";
        cmd.Parameters.AddWithValue("@kid", pKingdomId);
        using var r = (System.Data.SQLite.SQLiteDataReader)cmd.ExecuteReader();
        while (r.Read())
        {
            string value = r.IsDBNull(0) ? "" : r.GetString(0);
            if (!string.IsNullOrEmpty(value)) result.Add(value);
        }
    }
    catch { }
    return result;
}
```

- [ ] **Step 3: Implement special ordinary-title selection**

```csharp
private static string SelectTitleChar(PosthumousEvaluation pEval, string pEndReason, long pKingdomId)
{
    if (pEndReason == "kingdom_fell")
        return PickByPriority(pEval.Total <= -5
            ? new[] { "厉", "幽", "荒", "废" }
            : new[] { "哀", "闵", "悼" }, pKingdomId);

    if (pEval.Years < 3 && !pEval.Founder)
        return PickByPriority(new[] { "殇", "悼", "怀", "少" }, pKingdomId);

    if (pEndReason == "abdicated" && pEval.Total >= -1)
        return PickByPriority(new[] { "顺", "恭", "安", "让" }, pKingdomId);

    if (pEval.Founder && pEval.Total >= 0)
        return PickByPriority(new[] { "武", "文", "成", "桓", "烈" }, pKingdomId);

    return PickFromPool(pEval, pKingdomId);
}
```

The arrays intentionally do not include 太祖/高祖/世祖/烈祖.

- [ ] **Step 4: Implement pool selection**

```csharp
private static string PickFromPool(PosthumousEvaluation pEval, long pKingdomId)
{
    var candidates = new List<PosthumousTitleChar>();
    foreach (PosthumousTitleChar item in PosthumousTitleDefs.Pool)
    {
        if (!GradeAllows(pEval.Grade, item.MinGrade)) continue;
        if (item.Dimension == pEval.Dominant || item.Dimension == PosthumousDimension.Balanced)
            candidates.Add(item);
    }
    if (candidates.Count == 0)
    {
        foreach (PosthumousTitleChar item in PosthumousTitleDefs.Pool)
            if (GradeAllows(pEval.Grade, item.MinGrade)) candidates.Add(item);
    }
    if (candidates.Count == 0) return "平";

    HashSet<string> used = GetUsedTitleChars(pKingdomId);
    var unused = new List<PosthumousTitleChar>();
    foreach (PosthumousTitleChar item in candidates)
        if (!used.Contains(item.Char)) unused.Add(item);
    if (unused.Count > 0) candidates = unused;

    return candidates[Rng.Next(candidates.Count)].Char;
}
```

Add:

```csharp
private static bool GradeAllows(PosthumousGrade pActual, PosthumousGrade pRequired)
{
    return (int)pActual <= (int)pRequired || pActual == pRequired;
}
```

If enum order makes this unclear during implementation, replace with explicit `switch` ranges.

- [ ] **Step 5: Implement priority picker with deduplication**

```csharp
private static string PickByPriority(string[] pPool, long pKingdomId)
{
    HashSet<string> used = GetUsedTitleChars(pKingdomId);
    foreach (string item in pPool)
        if (!used.Contains(item)) return item;
    return pPool[Rng.Next(pPool.Length)];
}
```

- [ ] **Step 6: Replace old title creation path**

In `OnReignEnded`, replace `BuildScore` / `PickTitleChar` usage with:

```csharp
PosthumousEvaluation eval = Evaluate(pKingdom, pEndReason, pReign);
string titleChar = SelectTitleChar(eval, pEndReason, pKingdom.id);
string suffix = KingdomTitleService.GetTitleChar(KingdomTitleService.GetTitle(pKingdom));
if (string.IsNullOrEmpty(suffix)) suffix = "君";
string fullTitle = FirstChar(pKingdom.name) + titleChar + suffix;
```

- [ ] **Step 7: Build**

Run:

```powershell
& "C:\Program Files\dotnet\dotnet.exe" build
```

Expected: build succeeds.

---

### Task 6: Persist Evaluation Detail And Improve History Events

**Files:**
- Modify: `Code/core/lineage/PosthumousTitleService.cs`

- [ ] **Step 1: Write new score columns**

When inserting into `PosthumousTitle`, add:

```csharp
ColumnVal.Create("GRADE", GradeKey(eval.Grade)),
ColumnVal.Create("DOMINANT_DIMENSION", DimensionKey(eval.Dominant)),
ColumnVal.Create("SCORE_CIVIL", eval.Civil),
ColumnVal.Create("SCORE_TERRITORY", eval.Territory),
ColumnVal.Create("SCORE_WAR", eval.War),
ColumnVal.Create("SCORE_ORDER", eval.Order),
ColumnVal.Create("SCORE_ENDING", eval.Ending),
ColumnVal.Create("TOTAL_SCORE", eval.Total),
ColumnVal.Create("REASON_TEXT", eval.Reason)
```

Keep `SCORE_DETAIL` as `eval.Reason` too.

- [ ] **Step 2: Add key helpers**

```csharp
private static string GradeKey(PosthumousGrade pGrade)
{
    return pGrade switch
    {
        PosthumousGrade.PraiseHigh => "praise_high",
        PosthumousGrade.Praise => "praise",
        PosthumousGrade.Neutral => "neutral",
        PosthumousGrade.Blame => "blame",
        PosthumousGrade.BlameHigh => "blame_high",
        _ => "neutral"
    };
}

private static string DimensionKey(PosthumousDimension pDimension)
{
    return pDimension switch
    {
        PosthumousDimension.Civil => "civil",
        PosthumousDimension.Territory => "territory",
        PosthumousDimension.War => "war",
        PosthumousDimension.Order => "order",
        PosthumousDimension.Ending => "ending",
        _ => "balanced"
    };
}
```

- [ ] **Step 3: Set the history target to the king actor**

Change:

```csharp
HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.POSTHUMOUS, posthumousText);
```

to:

```csharp
HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.POSTHUMOUS, posthumousText, HistoryTarget.Actor(pKing));
```

This makes the row clickable and lets tooltip lookup by `target_id`.

- [ ] **Step 4: Add tooltip query method**

```csharp
public static string BuildTooltip(long pActorId)
{
    var db = LineageArchiveManager.Instance?.OperatingDB;
    if (db == null || pActorId < 0) return "";
    try
    {
        using var cmd = new System.Data.SQLite.SQLiteCommand(db);
        cmd.CommandText =
            $"SELECT FULL_TITLE, GRADE, DOMINANT_DIMENSION, REASON_TEXT " +
            $"FROM {PosthumousTitleTableItem.GetTableName()} " +
            "WHERE ACTOR_ID=@actor ORDER BY DECIDED_TIME DESC LIMIT 1";
        cmd.Parameters.AddWithValue("@actor", pActorId);
        using var r = (System.Data.SQLite.SQLiteDataReader)cmd.ExecuteReader();
        if (!r.Read()) return "";
        string title = r.IsDBNull(0) ? "" : r.GetString(0);
        string grade = r.IsDBNull(1) ? "" : r.GetString(1);
        string dim = r.IsDBNull(2) ? "" : r.GetString(2);
        string reason = r.IsDBNull(3) ? "" : r.GetString(3);
        return "谥号:" + title + "\n评等:" + grade + "\n主因:" + dim + "\n" + reason;
    }
    catch { return ""; }
}
```

- [ ] **Step 5: Build**

Run:

```powershell
& "C:\Program Files\dotnet\dotnet.exe" build
```

Expected: build succeeds.

---

### Task 7: Show Posthumous Detail In History Tooltip

**Files:**
- Modify: `Code/ui/windows/HistoryListWindow.cs`

- [ ] **Step 1: Append tooltip detail for posthumous rows**

In `BuildEventTooltip(HistoryEntry pEntry)`, before `return`, add:

```csharp
if (pEntry.event_type == KingdomEvent.POSTHUMOUS && pEntry.target_type == "actor" && pEntry.target_id >= 0)
{
    string extra = PosthumousTitleService.BuildTooltip(pEntry.target_id);
    if (!string.IsNullOrEmpty(extra))
        return type + time + content + "\n\n" + extra;
}
```

Keep the existing return as fallback.

- [ ] **Step 2: Build**

Run:

```powershell
& "C:\Program Files\dotnet\dotnet.exe" build
```

Expected: build succeeds.

---

### Task 8: Documentation And Deferred Tianming Boundary

**Files:**
- Modify: `README.md`
- Modify: `HANDOFF.md`

- [ ] **Step 1: Update README implemented feature note**

Add under implemented history/posthumous functionality:

```markdown
- 普通谥号系统已升级:非天命君主按民生、疆域、战功、秩序、结局评谥,后缀按爵位生成伯/侯/公/王/帝;太祖/高祖/世祖/烈祖等庙号只留给未来天命系统。
```

- [ ] **Step 2: Update README deferred boundary**

Add to the Tianming deferred-work section:

```markdown
- 天命庙号系统:太祖/高祖/世祖/烈祖/中宗/肃宗/宪宗等只授予天命王朝君主;未来与天命王朝、帝号、气运联动后再实现。
```

- [ ] **Step 3: Update HANDOFF**

In the history/posthumous section, record:

```markdown
- 普通谥号与天命庙号已分层:当前普通谥号只用国名前缀+谥字+爵位后缀;太祖/高祖等庙号不在普通评谥中生成。
```

- [ ] **Step 4: Build**

Run:

```powershell
& "C:\Program Files\dotnet\dotnet.exe" build
```

Expected: build succeeds.

---

### Task 9: Manual Verification Checklist

**Files:**
- No code changes.

- [ ] **Step 1: Build verification**

Run:

```powershell
& "C:\Program Files\dotnet\dotnet.exe" build
```

Expected:

```text
已成功生成。
    0 个警告
    0 个错误
```

- [ ] **Step 2: In-game ordinary title verification**

Create or load a Xia kingdom, let a ruler die or force a succession.

Expected:
- State history records one posthumous row, not a duplicate simple death row.
- Row text resembles `某王驾崩，谥为：周武王` or `某君退位，谥为：周顺侯`.
- Suffix follows current `KingdomTitleService` rank.
- No ordinary ruler receives 太祖/高祖/世祖/烈祖/中宗/肃宗/宪宗.

- [ ] **Step 3: Tooltip verification**

Hover the posthumous history row.

Expected:
- Tooltip shows title, grade, dominant dimension, and score detail.
- Clicking the row targets the actor if still present; if dead and unavailable, click safely does nothing.

- [ ] **Step 4: Special branch verification**

Use controlled scenarios:
- Short reign under 3 years: title prefers 殇/悼/怀/少.
- Abdication: title prefers 顺/恭/安/让 if reign was not disastrous.
- Kingdom fall: title prefers 哀/闵/悼 or 厉/幽/荒/废 depending on score.
- Founder with non-bad score: title prefers 武/文/成/桓/烈, never 太祖/高祖.

- [ ] **Step 5: Old save compatibility**

Load an old save with existing `KingdomReign` rows.

Expected:
- SQLite auto-adds columns.
- Existing rows without end snapshots still display.
- New posthumous titles after load use fallback live snapshot data and do not crash.

---

## Self-Review Notes

- Spec coverage: covers richer non-Tianming scoring, no temple names, no Tianming qi, tooltip detail, persistence, and old save migration.
- Placeholder scan: no implementation step depends on unspecified files or future systems.
- Type consistency: all new fields are lower-case C# fields matching AW3 table reflection style; SQL uses uppercase column names as existing code expects.
- Risk: `GradeAllows` enum ordering should be checked during implementation; if confusing, replace with explicit switch immediately.

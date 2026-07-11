# AW3 Foreign Xiaized Lineage And Pre-Qin Names Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make foreign Xiaized official surnames/branches stable and inheritable across civilized species, synchronize their visible Clans, and replace mixed Xia kingdom names with one broad pre-Qin polity library.

**Architecture:** Keep parsing and eligibility in pure rule classes linked into the temporary correctness harness. Runtime services consume resolved structured name parts only at promotion/birth events; optional Chinese Name resources and no-mod fallback code share an explicitly tested canonical pre-Qin list.

**Tech Stack:** C# 11, .NET Framework 4.8, Harmony, WorldBox Actor/Clan APIs, Chinese Name JSON/word libraries, temporary .NET 9 console tests.

**Execution constraints:** Work directly on `master`, execute inline without subagents, do not add save migration, and never stage the user's intentional `Tests/` or `Verification/` deletions.

---

## File Map

- Modify `Code/core/lineage/ForeignPseudoLineageRules.cs`: pure stable name resolver, suffix cleanup, birth and Clan eligibility.
- Modify `Code/core/lineage/LineageService.cs`: consume resolved names, expand civilized descendant inheritance, and synchronize visible Clans.
- Create `Code/content/XiaPreQinKingdomNameRules.cs`: canonical code-side name list and deterministic fallback picker.
- Modify `Code/content/XiaNameSets.cs`: use the canonical CSV in the vanilla name generator.
- Modify `Code/content/XiaFallbackNameRules.cs`: delegate local fallback selection to the canonical list.
- Modify `name_generators/Xia/kingdoms.json`: use the new Chinese Name word-library key.
- Create `name_generators/lib/先秦诸侯国.txt`: one normalized state name per line.
- Modify only temporarily `F:\tmp\AW3CorrectnessRuleTests\AW3CorrectnessRuleTests.csproj` and `Program.cs`.

### Task 1: Resolve Foreign Given, Family, And Branch Names Idempotently

**Files:**
- Modify: `F:\tmp\AW3CorrectnessRuleTests\AW3CorrectnessRuleTests.csproj`
- Modify: `F:\tmp\AW3CorrectnessRuleTests\Program.cs`
- Modify: `Code/core/lineage/ForeignPseudoLineageRules.cs`

- [ ] **Step 1: Link the pure rule and add failing parsing tests**

Link `ForeignPseudoLineageRules.cs` in the temporary project. Add tests for the
wished-for `ResolveNameParts` API:

```csharp
ForeignPseudoNameParts western = ForeignPseudoLineageRules.ResolveNameParts(
    "John Smith", "London的Smith家族", "", "", "", "", "齐");
Check(western.GivenName == "John" && western.FamilyName == "Smith" && western.ClanName == "Smith",
    "western delimited names must retain a clean surname and given name");

ForeignPseudoNameParts chinese = ForeignPseudoLineageRules.ResolveNameParts(
    "李云", "曲阜的李家族", "", "", "李", "", "鲁");
Check(chinese.GivenName == "云" && chinese.FamilyName == "李" && chinese.ClanName == "李",
    "Chinese Name structured family must split a no-delimiter name");

string[] dirtyClans = { "曲阜的李家族", "李氏族", "李部落", "李家", "李族", "李氏" };
Check(dirtyClans.All(p => ForeignPseudoLineageRules.NormalizeClanName(p) == "李"),
    "all visible Clan suffixes must normalize to the branch name");

ForeignPseudoNameParts existing = ForeignPseudoLineageRules.ResolveNameParts(
    "李云", "曲阜的李家族", "云", "李", "李", "李", "鲁");
Check(existing.GivenName == "云" && existing.FamilyName == "李" && existing.ClanName == "李",
    "existing structured names must remain idempotent across repeated promotion");

ForeignPseudoNameParts lone = ForeignPseudoLineageRules.ResolveNameParts(
    "Elohir", "", "", "", "", "", "燕");
Check(lone.GivenName == "Elohir" && lone.FamilyName == "燕" && lone.ClanName == "燕",
    "one-token actors without a Clan must use the kingdom fallback without duplicating their given name");
```

- [ ] **Step 2: Run RED**

Run:

```powershell
dotnet run --project F:\tmp\AW3CorrectnessRuleTests\AW3CorrectnessRuleTests.csproj
```

Expected: compilation fails because `ForeignPseudoNameParts`, `ResolveNameParts`,
and `NormalizeClanName` do not exist.

- [ ] **Step 3: Implement the pure resolver**

Add `ForeignPseudoNameParts` and implement these rules:

```csharp
public readonly struct ForeignPseudoNameParts
{
    public readonly string GivenName;
    public readonly string FamilyName;
    public readonly string ClanName;

    public ForeignPseudoNameParts(string givenName, string familyName, string clanName)
    {
        GivenName = givenName ?? "";
        FamilyName = familyName ?? "";
        ClanName = clanName ?? "";
    }
}
```

`ResolveNameParts` must:

1. Preserve non-empty existing given/family/clan values.
2. Prefer `chinese_family_name` over text parsing.
3. Use the last delimited personal-name token only when structured family data is absent.
4. Normalize a visible Clan by taking text after the final `的` and repeatedly removing `家族/氏族/部落/家/族/氏`.
5. Use the first useful kingdom character only when no family/Clan source exists.
6. Remove the resolved family from either end of a no-delimiter display name before using the current delimiter fallback.

Keep `ExtractClanName` and `ExtractGivenName` public for compatibility, but route
their fallback cleanup through the new helpers.

- [ ] **Step 4: Run GREEN and commit the pure resolver**

Run the correctness harness; expect `direct-son rules passed`. Then commit only:

```powershell
git add -- Code/core/lineage/ForeignPseudoLineageRules.cs
git commit -m "fix: normalize foreign Xiaized lineage names"
```

### Task 2: Wire Stable Names, Civilized Descendants, And Visible Clan Renaming

**Files:**
- Modify: `F:\tmp\AW3CorrectnessRuleTests\Program.cs`
- Modify: `Code/core/lineage/ForeignPseudoLineageRules.cs`
- Modify: `Code/core/lineage/LineageService.cs`

- [ ] **Step 1: Add failing lifecycle eligibility tests**

```csharp
Check(ForeignPseudoLineageRules.ShouldUseLineageBirth(
        isXia: false, isCivilizedSpecies: true, parentHasLineage: true),
    "civilized foreign descendants must inherit an established AW3 lineage");
Check(!ForeignPseudoLineageRules.ShouldUseLineageBirth(
        isXia: false, isCivilizedSpecies: false, parentHasLineage: true),
    "non-civilized creatures must not enter the lineage birth path");
Check(ForeignPseudoLineageRules.ShouldRenameInstitutionalClan(
        leaderIsXia: false, kingdomUsesXiaizedInstitutions: true,
        hasClan: true, hasBranch: true, hasPlace: true),
    "foreign institutional officials must synchronize their visible Clan");
Check(!ForeignPseudoLineageRules.ShouldRenameInstitutionalClan(
        leaderIsXia: false, kingdomUsesXiaizedInstitutions: false,
        hasClan: true, hasBranch: true, hasPlace: true),
    "unrelated foreign Clans must retain their native naming");
```

- [ ] **Step 2: Run RED**

Run the correctness harness. Expected: missing `ShouldUseLineageBirth` and
`ShouldRenameInstitutionalClan` methods.

- [ ] **Step 3: Implement the pure lifecycle gates**

```csharp
public static bool ShouldUseLineageBirth(bool isXia, bool isCivilizedSpecies,
    bool parentHasLineage)
{
    return isXia || (isCivilizedSpecies && parentHasLineage);
}

public static bool ShouldRenameInstitutionalClan(bool leaderIsXia,
    bool kingdomUsesXiaizedInstitutions, bool hasClan, bool hasBranch, bool hasPlace)
{
    return (leaderIsXia || kingdomUsesXiaizedInstitutions) &&
           hasClan && hasBranch && hasPlace;
}
```

- [ ] **Step 4: Wire civilized descendant inheritance**

In `ShouldUseLineageBirth`, compute:

```csharp
bool parentHasLineage = HasLineageData(pParent1) || HasLineageData(pParent2) ||
                        UsesAwLineageSystem(pParent1) || UsesAwLineageSystem(pParent2);
return ForeignPseudoLineageRules.ShouldUseLineageBirth(
    IsXia(pBaby), pBaby.asset?.civ == true, parentHasLineage);
```

This replaces the current human-only branch.

- [ ] **Step 5: Consume resolved parts without overwriting structured data**

In `EnsureForeignPseudoOfficialLineage`, read existing given/family/Chinese-family/
branch fields and call `ResolveNameParts`. Attempt paternal inheritance before
creating a new lineage. Re-read inherited fields and resolve again after a
successful inheritance. Create `LineageGroup` with `FamilyName`, `ShiBranch` with
`ClanName`, and write each actor field only when its existing value is empty.

After `ApplyDisplayName`, call:

```csharp
RenameClanByLeader(pActor.clan, pActor);
```

- [ ] **Step 6: Broaden `RenameClanByLeader` safely**

Replace the Xia-only guard with the pure gate. Resolve `shi` and `place` first,
then call:

```csharp
bool institutional = XiaizationService.UsesXiaizedInstitutionSystem(pLeader.kingdom);
if (!ForeignPseudoLineageRules.ShouldRenameInstitutionalClan(
        IsXia(pLeader), institutional, pClan?.data != null,
        !string.IsNullOrEmpty(shi), !string.IsNullOrEmpty(place))) return;
```

Keep the existing `place + shi + "氏"` format and idempotent same-name check.

- [ ] **Step 7: Run focused tests, build, and commit runtime wiring**

Run the correctness harness and normal build. Commit:

```powershell
git add -- Code/core/lineage/ForeignPseudoLineageRules.cs Code/core/lineage/LineageService.cs
git commit -m "fix: preserve foreign Xiaized lineages"
```

### Task 3: Replace Xia Kingdom Names With A Broad Pre-Qin Library

**Files:**
- Create: `Code/content/XiaPreQinKingdomNameRules.cs`
- Modify: `Code/content/XiaNameSets.cs`
- Modify: `Code/content/XiaFallbackNameRules.cs`
- Modify: `name_generators/Xia/kingdoms.json`
- Create: `name_generators/lib/先秦诸侯国.txt`
- Modify: `F:\tmp\AW3CorrectnessRuleTests\AW3CorrectnessRuleTests.csproj`
- Modify: `F:\tmp\AW3CorrectnessRuleTests\Program.cs`

- [ ] **Step 1: Add failing canonical-library tests**

Link `XiaPreQinKingdomNameRules.cs` and add:

```csharp
string[] preQin = XiaPreQinKingdomNameRules.All();
Check(preQin.Length >= 160 && preQin.Distinct(StringComparer.Ordinal).Count() == preQin.Length,
    "pre-Qin kingdom library must be broad and unique");
Check(new[] { "齐", "鲁", "晋", "秦", "楚", "吴", "越", "中山", "孤竹", "义渠", "大荔", "犬戎", "山戎" }
      .All(preQin.Contains),
    "pre-Qin library must contain representative central and frontier states");
Check(preQin.All(p => !p.EndsWith("国", StringComparison.Ordinal)),
    "pre-Qin state names must omit the country suffix");
Check(new[] { "汉", "明", "清", "粤", "闽", "赣", "东夷", "西戎", "北狄", "百越", "群蛮" }
      .All(p => !preQin.Contains(p)),
    "later dynasties, modern abbreviations, and generic ethnonyms must be excluded");

string libraryPath = Path.Combine(repoRoot, "name_generators", "lib", "先秦诸侯国.txt");
string[] resourceNames = File.ReadAllLines(libraryPath)
    .Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();
Check(resourceNames.SequenceEqual(preQin),
    "Chinese Name word library and code fallback must remain identical");

string kingdomJson = File.ReadAllText(Path.Combine(repoRoot, "name_generators", "Xia", "kingdoms.json"));
Check(kingdomJson.Contains("{先秦诸侯国}", StringComparison.Ordinal) &&
      !kingdomJson.Contains("{中文国名前缀}", StringComparison.Ordinal),
    "Xia_kingdom must use the dedicated pre-Qin word library");
```

- [ ] **Step 2: Run RED**

Run the correctness harness. Expected: missing `XiaPreQinKingdomNameRules` and
missing word-library resource.

- [ ] **Step 3: Add the canonical code list**

Create `XiaPreQinKingdomNameRules` with this exact ordered list:

```csharp
private static readonly string[] Names =
{
    "齐", "鲁", "晋", "燕", "卫", "宋", "郑", "陈", "蔡", "曹", "滕", "杞", "许", "邢",
    "吴", "越", "秦", "楚", "韩", "赵", "魏", "中山", "代", "梁", "唐", "南燕",
    "管", "霍", "郕", "郜", "毛", "毕", "邘", "应", "蒋", "芮", "沈", "单", "召", "刘",
    "荣", "甘", "樊", "祭", "温", "滑", "苏", "原", "詹", "酆", "聃", "密", "杜", "霸",
    "虢", "东虢", "西虢", "南虢", "北虢", "虞", "贾", "荀", "耿", "冀", "井", "缙", "杨",
    "凡", "共", "巩", "邾", "邹", "小邾", "郳", "莒", "纪", "莱", "谭", "遂", "鄅", "郯",
    "鄫", "任", "宿", "须句", "颛臾", "根牟", "牟", "介", "鄣", "蒲姑", "奄", "薛", "葛",
    "戴", "萧", "徐", "舒", "舒鸠", "舒蓼", "舒庸", "舒龙", "舒鲍", "钟离", "钟吾", "六",
    "英", "黄", "江", "弦", "息", "道", "房", "顿", "胡", "项", "申", "吕", "谢", "鄀",
    "鄾", "鄂", "随", "曾", "罗", "邓", "绞", "权", "庸", "麇", "夔", "郧", "贰", "轸",
    "巴", "蜀", "苴", "鱼", "彭", "巢", "桐", "柏", "赖", "黎", "州", "淳于", "莘", "焦",
    "茅", "费", "郇", "胙", "郐", "鄢", "阳", "章", "程", "习", "邿", "鄟", "蓼", "厉",
    "肥", "鼓", "潞", "蓟", "鲜虞", "仇由", "孤竹", "令支", "无终", "义渠", "大荔", "犬戎",
    "山戎", "骊戎", "姜戎", "陆浑", "白狄", "赤狄", "林胡", "楼烦", "东胡", "戎蛮",
    "曲沃", "安陵", "东周", "西周", "有穷", "甲父", "祝其", "微", "丰", "瑕", "观", "骀",
    "箕", "蓐", "向", "谷", "祝", "聂", "叶"
};
```

Expose `All()`, `Csv`, and deterministic `Pick(long seed)` methods. `All()`
returns a clone; `Pick` uses the existing seed formula from
`XiaFallbackNameRules`.

- [ ] **Step 4: Add the matching word library and JSON template**

Create `先秦诸侯国.txt` with exactly one `Names` entry per line in the same order.
Change `Xia_kingdom` to the `{先秦诸侯国}` template with weight 1.

- [ ] **Step 5: Replace both code fallback paths**

In `XiaNameSets`, register:

```csharp
"name", XiaPreQinKingdomNameRules.Csv
```

In `XiaFallbackNameRules.LocalKingdomName`, return:

```csharp
return XiaPreQinKingdomNameRules.Pick(pSeed);
```

Remove the old mixed `KingdomNames` array.

- [ ] **Step 6: Run GREEN and commit the resource slice**

Run the correctness harness and both symbol builds. Commit:

```powershell
git add -- Code/content/XiaPreQinKingdomNameRules.cs Code/content/XiaNameSets.cs Code/content/XiaFallbackNameRules.cs name_generators/Xia/kingdoms.json name_generators/lib/先秦诸侯国.txt
git commit -m "feat: add broad pre-Qin Xia kingdom names"
```

### Task 4: Final Verification And Boundary Audit

**Files:**
- Verify all production/resource files changed in Tasks 1-3.

- [ ] **Step 1: Run all focused rules without restore**

```powershell
dotnet run --no-restore --project F:\tmp\AW3CorrectnessRuleTests\AW3CorrectnessRuleTests.csproj
dotnet run --no-restore --project F:\tmp\AW3CourtExpansionRuleTests\AW3CourtExpansionRuleTests.csproj
```

Expected: `direct-son rules passed` and `court school rules passed`.

- [ ] **Step 2: Restore from the existing cache and build both configurations**

```powershell
dotnet restore AncientWarfare3.csproj --ignore-failed-sources '-p:RestorePackagesPath=C:\Users\24908\.nuget\packages' '-p:TargetFrameworkRootPath=C:\Users\24908\.nuget\packages\microsoft.netframework.referenceassemblies.net48\1.0.3\build\'
dotnet build AncientWarfare3.csproj --no-restore '-p:RestorePackagesPath=C:\Users\24908\.nuget\packages' '-p:TargetFrameworkRootPath=C:\Users\24908\.nuget\packages\microsoft.netframework.referenceassemblies.net48\1.0.3\build\'
dotnet build AncientWarfare3.csproj --no-restore '-p:RestorePackagesPath=C:\Users\24908\.nuget\packages' '-p:TargetFrameworkRootPath=C:\Users\24908\.nuget\packages\microsoft.netframework.referenceassemblies.net48\1.0.3\build\' -p:DefineConstants=DEBUG%3BTRACE
```

Expected: both builds report 0 warnings and 0 errors.

- [ ] **Step 3: Audit Git boundaries**

Run `git diff --check`, `git status --short`, and `git diff --cached --name-only`.
Confirm no temporary harness file is tracked and the user's test deletions remain
unstaged. Do not push unless the user explicitly requests it.

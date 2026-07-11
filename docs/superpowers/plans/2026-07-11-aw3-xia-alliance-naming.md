# AW3 Xia Alliance Naming Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give a newly founded alliance a stable Chinese name when either founding kingdom is Xia, before the vanilla creation world log is written.

**Architecture:** A pure founder-eligibility rule controls a Xia-specific name generator. An `Alliance.addFounders` Postfix runs after both founders join and before `AllianceManager.newAlliance` proceeds to its log statement; membership changes never rename the alliance.

**Tech Stack:** C# 11, Harmony, WorldBox `NameGeneratorAsset`, existing Xia naming fallback system.

---

## File Map

- Create `Code/content/XiaAllianceNamingRules.cs`: pure founder eligibility.
- Modify `Verification/AW3FocusedRuleTests/Program.cs`: Xia alliance rule regressions.
- Modify `Code/content/XiaNameSets.cs`: Xia alliance generator.
- Modify `Code/content/XiaNamingRepair.cs`: generate and validate alliance names.
- Modify `Code/content/XiaFallbackNameRules.cs`: deterministic local fallback.
- Modify `Code/patch/AW_XiaNamingPatch.cs`: creation-time founder hook.

### Task 1: Add RED founder-eligibility tests

**Files:**
- Modify: `Verification/AW3FocusedRuleTests/Program.cs`

- [ ] **Step 1: Import the content namespace, then add and call `ExpectXiaAllianceNaming()`**

Add `using AncientWarfare3.content;` beside the existing lineage import.

```csharp
private static void ExpectXiaAllianceNaming()
{
    if (!XiaAllianceNamingRules.ShouldUseXiaName(true, false) ||
        !XiaAllianceNamingRules.ShouldUseXiaName(false, true) ||
        !XiaAllianceNamingRules.ShouldUseXiaName(true, true) ||
        XiaAllianceNamingRules.ShouldUseXiaName(false, false))
        throw new Exception("Either Xia founder must activate Xia alliance naming.");
    if (XiaAllianceNamingRules.ShouldRenameAfterCreation(false, true) ||
        !XiaAllianceNamingRules.ShouldRenameAfterCreation(true, true))
        throw new Exception("Naming runs once and only with a valid generated name.");
}
```

- [ ] **Step 2: Run RED**

Run the focused project.

Expected: compilation fails because `XiaAllianceNamingRules` does not exist.

- [ ] **Step 3: Commit test**

```powershell
git add Verification/AW3FocusedRuleTests/Program.cs
git commit -m "test: 覆盖夏联盟中文命名条件"
```

### Task 2: Implement the rule and Xia alliance generator

**Files:**
- Create: `Code/content/XiaAllianceNamingRules.cs`
- Modify: `Code/content/XiaNameSets.cs`

- [ ] **Step 1: Implement the pure rule**

```csharp
namespace AncientWarfare3.content
{
    public static class XiaAllianceNamingRules
    {
        public static bool ShouldUseXiaName(bool pFounder1IsXia, bool pFounder2IsXia)
            => pFounder1IsXia || pFounder2IsXia;

        public static bool ShouldRenameAfterCreation(bool pUsesXiaNaming, bool pValidName)
            => pUsesXiaNaming && pValidName;
    }
}
```

- [ ] **Step 2: Register the generator**

Add `internal const string AllianceGenerator = "Xia_alliance";` and register:

```csharp
RegisterDictionaryGenerator(
    AllianceGenerator,
    new[]
    {
        "root", "\u8bf8\u590f,\u534e\u590f,\u4e5d\u5dde,\u6cb3\u6d1b,\u738b\u757f,\u793c\u4e50,\u5c71\u6cb3,\u6d77\u5185",
        "suffix", "\u76df,\u4f1a\u76df,\u540c\u76df,\u76df\u8a93"
    },
    "root,suffix");
```

- [ ] **Step 3: Run GREEN and commit**

Run focused tests and build; expect success.

```powershell
git add Code/content/XiaAllianceNamingRules.cs Code/content/XiaNameSets.cs
git commit -m "feat: 添加夏联盟名称生成器"
```

### Task 3: Generate, validate, and apply the name before the vanilla log

**Files:**
- Modify: `Code/content/XiaNamingRepair.cs`
- Modify: `Code/content/XiaFallbackNameRules.cs`
- Modify: `Code/patch/AW_XiaNamingPatch.cs`

- [ ] **Step 1: Add a deterministic fallback**

Add `LocalAllianceName(long pId)` selecting one root and one suffix from fixed arrays using the same non-negative modulo pattern as existing fallback methods.

- [ ] **Step 2: Add `GenerateAllianceName`**

Generate through `XiaNameSets.AllianceGenerator` using the alliance ID as seed. Reject `XiaNameRepairRules.IsInvalidGeneratedMetaName`; fall back to `LocalAllianceName` if generation fails.

- [ ] **Step 3: Patch `Alliance.addFounders`**

```csharp
[HarmonyPostfix]
[HarmonyPatch(typeof(Alliance), nameof(Alliance.addFounders))]
public static void AddFounders_Postfix(Alliance __instance,
    Kingdom pKingdom1, Kingdom pKingdom2)
{
    bool founder1IsXia = LineageService.IsXiaKingdom(pKingdom1);
    bool founder2IsXia = LineageService.IsXiaKingdom(pKingdom2);
    bool usesXiaName = XiaAllianceNamingRules.ShouldUseXiaName(
        founder1IsXia, founder2IsXia);
    if (!usesXiaName || __instance?.data == null) return;

    string name = XiaNamingRepair.GenerateAllianceName(__instance);
    bool valid = !XiaNameRepairRules.IsInvalidGeneratedMetaName(name);
    if (!XiaAllianceNamingRules.ShouldRenameAfterCreation(usesXiaName, valid)) return;
    __instance.setName(name, pTrack: false);
}
```

Because Harmony executes this Postfix before returning to `AllianceManager.newAlliance`, the following vanilla `WorldLog.logAllianceCreated(alliance)` sees the Chinese name.

- [ ] **Step 4: Verify and commit**

Run focused tests and build; expect success.

```powershell
git add Code/content/XiaNamingRepair.cs Code/content/XiaFallbackNameRules.cs Code/patch/AW_XiaNamingPatch.cs
git commit -m "feat: 夏创始联盟使用中文名"
```

### Task 4: Xia alliance acceptance

- [ ] **Step 1:** Run focused tests; expect the pass message.
- [ ] **Step 2:** Run `dotnet build AncientWarfare3.csproj`; expect zero errors.
- [ ] **Step 3:** Form Xia + Xia, Xia + non-Xia, and non-Xia + non-Xia alliances. The first two must use Chinese names; the third must retain vanilla naming.
- [ ] **Step 4:** Add and remove later members and confirm the original alliance name does not change.
- [ ] **Step 5:** Confirm the alliance-created world log already contains the final Chinese name.

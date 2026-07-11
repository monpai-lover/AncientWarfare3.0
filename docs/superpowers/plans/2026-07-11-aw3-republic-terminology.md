# AW3 Republic Terminology Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Display Republic rulers as `元首` and registered Republic successors as `元老` across every live surface and freeze those terms into new history snapshots without rewriting monarchy history.

**Architecture:** A pure government-title resolver owns precedence and keys. UI, social-title, archive, and history code call that resolver with an event-time Republic flag; history persists distinct role snapshots so later government changes cannot reinterpret old events.

**Tech Stack:** C# 11, Harmony UI patches, AW3 lineage archive, CSV localization.

---

## File Map

- Create `Code/core/lineage/GovernmentTitleRules.cs`: public pure terminology rules.
- Modify `Verification/AW3FocusedRuleTests/Program.cs`: terminology regressions.
- Modify `Code/core/lineage/HeirTitleRules.cs`: Republic-aware successor keys and social titles.
- Modify `Code/ui/windows/KingdomWindowAddition.cs`: dynamic ruler and successor labels.
- Modify `Code/patch/AW_KingdomWindowPatch.cs`: original kingdom/city stats rows.
- Modify `Code/core/lineage/LineageArchiveWriter.cs`: event-time Republic social-title snapshots.
- Modify `Code/core/lineage/LineageQuery.cs`: live and archived family-tree titles.
- Modify `Code/core/lineage/AncestryAnalysisService.cs`: ancestry live titles.
- Modify `Code/core/lineage/HistoryWriter.cs`: distinct Republic roles and labels.
- Modify `Code/ui/windows/HistoryListWindow.cs`: Republic role localization.
- Modify `locales/others.csv`: Simplified Chinese, English, and Traditional Chinese keys.

### Task 1: Add RED terminology tests

**Files:**
- Modify: `Verification/AW3FocusedRuleTests/Program.cs`

- [ ] **Step 1: Add and call `ExpectRepublicTerminology()`**

```csharp
private static void ExpectRepublicTerminology()
{
    if (GovernmentTitleRules.RulerKey(true) != "aw_republic_head" ||
        GovernmentTitleRules.RulerKey(false) != "aw_label_king")
        throw new Exception("Republic ruler labels must override monarchy labels.");
    if (GovernmentTitleRules.SuccessorKey(true, true) != "aw_republic_elder" ||
        GovernmentTitleRules.SuccessorKey(false, true) != HeirTitleRules.TaiziKey ||
        GovernmentTitleRules.SuccessorKey(false, false) != HeirTitleRules.ShiziKey)
        throw new Exception("Republic must take precedence over Mandate succession titles.");
    if (GovernmentTitleRules.RoleSnapshot(true, true, false, false) != "republic_head" ||
        GovernmentTitleRules.RoleSnapshot(true, false, true, false) != "republic_elder" ||
        GovernmentTitleRules.RoleSnapshot(false, true, false, false) != "king")
        throw new Exception("History role snapshots must freeze event-time government.");
    if (GovernmentTitleRules.BuildSocialTitle("\u9f50", true, false) != "\u9f50 \u5143\u9996" ||
        GovernmentTitleRules.BuildSocialTitle("\u9f50", false, true) != "\u9f50 \u5143\u8001")
        throw new Exception("Republic social titles are incorrect.");
}
```

- [ ] **Step 2: Run RED**

Run the focused project.

Expected: compilation fails because `GovernmentTitleRules` does not exist.

- [ ] **Step 3: Commit test**

```powershell
git add Verification/AW3FocusedRuleTests/Program.cs
git commit -m "test: 覆盖共和国元首元老称谓"
```

### Task 2: Implement the centralized title resolver

**Files:**
- Create: `Code/core/lineage/GovernmentTitleRules.cs`
- Modify: `Code/core/lineage/HeirTitleRules.cs`

- [ ] **Step 1: Implement pure rules**

```csharp
namespace AncientWarfare3.core.lineage
{
    public static class GovernmentTitleRules
    {
        public const string RepublicHeadKey = "aw_republic_head";
        public const string RepublicElderKey = "aw_republic_elder";

        public static string RulerKey(bool pIsRepublic)
            => pIsRepublic ? RepublicHeadKey : "aw_label_king";

        public static string SuccessorKey(bool pIsRepublic, bool pIsMandate)
            => pIsRepublic ? RepublicElderKey : HeirTitleRules.TitleKey(pIsMandate);

        public static string RoleSnapshot(bool pIsRepublic, bool pIsRuler,
            bool pIsSuccessor, bool pIsMandate)
        {
            if (pIsRepublic && pIsRuler) return "republic_head";
            if (pIsRepublic && pIsSuccessor) return "republic_elder";
            if (pIsRuler) return "king";
            if (pIsSuccessor) return HeirTitleRules.RoleSnapshot(pIsMandate);
            return "";
        }

        public static string BuildSocialTitle(string pKingdomName,
            bool pIsHead, bool pIsElder)
        {
            string title = pIsHead ? "\u5143\u9996" : pIsElder ? "\u5143\u8001" : "";
            return string.IsNullOrEmpty(pKingdomName) ? title : pKingdomName + " " + title;
        }

        public static bool IsRepublicSocialTitle(string pTitle)
            => !string.IsNullOrEmpty(pTitle) &&
               (pTitle.EndsWith(" \u5143\u9996") || pTitle.EndsWith(" \u5143\u8001") ||
                pTitle == "\u5143\u9996" || pTitle == "\u5143\u8001");
    }
}
```

- [ ] **Step 2: Make kingdom-aware heir helpers call the resolver**

`HeirTitleRules.TitleKey(Kingdom)` and `BuildSocialTitle(string, Kingdom)` must check `RepublicGovernmentService.IsRepublic` first, then Mandate state.

- [ ] **Step 3: Run GREEN and commit**

Run focused tests and build; expect success.

```powershell
git add Code/core/lineage/GovernmentTitleRules.cs Code/core/lineage/HeirTitleRules.cs
git commit -m "feat: 统一共和国称谓规则"
```

### Task 3: Apply terminology to live windows

**Files:**
- Modify: `Code/ui/windows/KingdomWindowAddition.cs`
- Modify: `Code/patch/AW_KingdomWindowPatch.cs`

- [ ] **Step 1: Refresh both avatar labels dynamically**

In `KingdomWindowAddition.Refresh`, resolve the ruler label with `GovernmentTitleRules.RulerKey(RepublicGovernmentService.IsRepublic(kingdom))` and the successor label with `HeirTitleRules.TitleKey(kingdom)`. Update `LocalizedText` every time the selected kingdom changes.

- [ ] **Step 2: Rewrite original stats labels contextually**

Extend `StatsWindow.tryToShowActor` Prefix: when `pTitle == "king"` and the displayed actor belongs to a Republic, set `pTitle = GovernmentTitleRules.RepublicHeadKey`. Existing heir aliases continue through `HeirTitleRules.TitleKey(actor.kingdom ?? SelectedMetas.selected_kingdom)`. This covers both kingdom and city windows without changing global vanilla localization.

- [ ] **Step 3: Build and commit**

Run focused tests and build; expect success.

```powershell
git add Code/ui/windows/KingdomWindowAddition.cs Code/patch/AW_KingdomWindowPatch.cs
git commit -m "feat: 国家窗口显示元首元老"
```

### Task 4: Apply terminology to lineage, ancestry, and archive snapshots

**Files:**
- Modify: `Code/core/lineage/LineageArchiveWriter.cs`
- Modify: `Code/core/lineage/LineageQuery.cs`
- Modify: `Code/core/lineage/AncestryAnalysisService.cs`

- [ ] **Step 1: Archive event-time social titles**

In `ResolveSocialTitleSnapshot`, test Republic ruler before the monarchy title-character path and use `GovernmentTitleRules.BuildSocialTitle(kingdomName, true, false)`. Republic successors use `HeirTitleRules.BuildSocialTitle`, which is now government-aware.

- [ ] **Step 2: Fix live family-tree and ancestry titles**

In both live-title resolvers, a live Republic king uses `国名 元首`; a registered Republic successor uses `国名 元老`.

- [ ] **Step 3: Preserve archived Republic titles**

In `LineageQuery.ApplyArchivedSocialTitle`, return an explicit `元首/元老` social title before the generic `WasKing` fallback. Do not change old archived monarchy titles.

- [ ] **Step 4: Verify and commit**

Run focused tests and build; expect success.

```powershell
git add Code/core/lineage/LineageArchiveWriter.cs Code/core/lineage/LineageQuery.cs Code/core/lineage/AncestryAnalysisService.cs
git commit -m "feat: 氏族树保留共和国身份称谓"
```

### Task 5: Freeze Republic terminology into new history snapshots

**Files:**
- Modify: `Code/core/lineage/HistoryWriter.cs`
- Modify: `Code/ui/windows/HistoryListWindow.cs`
- Modify: `locales/others.csv`

- [ ] **Step 1: Resolve event-time roles**

In `HistoryWriter.ResolveRoleSnapshot`, when `pActor.isKing()` return `republic_head` if its kingdom is a Republic, otherwise `king`. For the current heir, return `republic_elder` for a Republic before checking Mandate state.

- [ ] **Step 2: Add role labels**

Map `republic_head` to `元首` and `republic_elder` to `元老` in both `HistoryWriter.RoleLabel` and `HistoryListWindow`'s role-label switch.

- [ ] **Step 3: Add localization rows**

```csv
aw_republic_head,元首,Head of State,元首
aw_republic_elder,元老,Elder,元老
```

- [ ] **Step 4: Verify and commit**

Run focused tests and build; expect success.

```powershell
git add Code/core/lineage/HistoryWriter.cs Code/ui/windows/HistoryListWindow.cs locales/others.csv
git commit -m "feat: 编年史记录共和国元首元老"
```

### Task 6: Republic terminology acceptance

- [ ] **Step 1:** Run focused tests; expect the pass message.
- [ ] **Step 2:** Run `dotnet build AncientWarfare3.csproj`; expect zero errors.
- [ ] **Step 3:** In game, inspect a Republic in the kingdom window, one of its cities, the ruler and successor unit panels, family tree, ancestry view, and new history rows; every live ruler/successor label must be `元首/元老`.
- [ ] **Step 4:** Restore monarchy and confirm previously created Republic history stays `元首/元老`, while earlier monarchy history stays `国王/世子/太子`.

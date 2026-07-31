# Legacy Royal Marriage Language Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make legacy royal-marriage history normalization use the persisted sentence language instead of the current UI language.

**Architecture:** Add the missing royal-marriage middle phrase to the UI-independent history localization table, then let `WarDisplayLabelRules` detect the source sentence language from that stable phrase. Keep this as a read-time, idempotent compatibility repair with no schema migration or save rewrite.

**Tech Stack:** C# 11 production code, .NET 9 isolated executable regression slice, PowerShell verification, existing AW3 history localization rules.

---

## File Structure

- Create `Tests/LegacyRoyalMarriageLanguageSlice/LegacyRoyalMarriageLanguageSlice.csproj`: compile the two production rule files in isolation.
- Create `Tests/LegacyRoyalMarriageLanguageSlice/Stubs.cs`: provide only the runtime types required by `HistoryLocalizationRules`.
- Create `Tests/LegacyRoyalMarriageLanguageSlice/Program.cs`: executable regression assertions for source-language detection and idempotence.
- Modify `Code/core/lineage/HistoryLocalizationRules.cs`: expose the stable simplified Chinese, traditional Chinese, and English marriage middle phrases.
- Modify `Code/core/lineage/WarDisplayLabelRules.cs`: detect source language, recognize all known suffixes, and append only the source-language suffix.

### Task 1: Add the failing cross-language regression slice

**Files:**
- Create: `Tests/LegacyRoyalMarriageLanguageSlice/LegacyRoyalMarriageLanguageSlice.csproj`
- Create: `Tests/LegacyRoyalMarriageLanguageSlice/Stubs.cs`
- Create: `Tests/LegacyRoyalMarriageLanguageSlice/Program.cs`

- [ ] **Step 1: Create the isolated project**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="..\..\Code\core\lineage\HistoryLocalizationRules.cs"
             Link="Production\HistoryLocalizationRules.cs" />
    <Compile Include="..\..\Code\core\lineage\WarDisplayLabelRules.cs"
             Link="Production\WarDisplayLabelRules.cs" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add minimal runtime stubs**

```csharp
public sealed class LocalizedTextManager
{
    public static LocalizedTextManager instance;
    public string language;
}

namespace AncientWarfare3.core.lineage
{
    public readonly struct HistoryText
    {
        public readonly string Plain;

        private HistoryText(string pPlain)
        {
            Plain = pPlain ?? "";
        }

        public static HistoryText PlainText(string pText)
        {
            return new HistoryText(pText);
        }
    }
}
```

- [ ] **Step 3: Add the executable regression assertions**

```csharp
using System;
using System.Collections.Generic;
using AncientWarfare3.core.lineage;

Equal("A married B",
    WarDisplayLabelRules.NormalizeHistoryContent(
        "royal_marriage", "A married B", "cz"),
    "English history stays English under simplified Chinese UI");
Equal("甲与乙缔结婚盟",
    WarDisplayLabelRules.NormalizeHistoryContent(
        "royal_marriage", "甲与乙", "en"),
    "simplified Chinese history gets its own suffix under English UI");
Equal("甲與乙締結婚盟",
    WarDisplayLabelRules.NormalizeHistoryContent(
        "royal_marriage", "甲與乙", "cz"),
    "traditional Chinese history gets its own suffix under simplified UI");
Equal("甲与乙缔结婚盟",
    WarDisplayLabelRules.NormalizeHistoryContent(
        "royal_marriage", "甲与乙缔结婚盟", "ch"),
    "simplified suffix is not duplicated after a language switch");
Equal("甲與乙締結婚盟",
    WarDisplayLabelRules.NormalizeHistoryContent(
        "royal_marriage", "甲與乙締結婚盟", "cz"),
    "traditional suffix is not duplicated after a language switch");
Equal("A wed B",
    WarDisplayLabelRules.NormalizeHistoryContent(
        "royal_marriage", "A wed B", "cz"),
    "unknown legacy format fails closed");
Equal("甲与乙",
    WarDisplayLabelRules.NormalizeHistoryContent(
        "war_start", "甲与乙", "cz"),
    "non-marriage history is untouched");

Console.WriteLine("AW3 legacy royal-marriage language rules passed.");

static void Equal<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            name + ": expected " + expected + ", got " + actual);
}
```

- [ ] **Step 4: Run the regression slice and verify RED**

Run:

```powershell
dotnet run --project Tests/LegacyRoyalMarriageLanguageSlice/LegacyRoyalMarriageLanguageSlice.csproj
```

Expected: FAIL because the current code appends the simplified Chinese suffix to `A married B` when the requested UI language is `cz`.

### Task 2: Normalize from the persisted sentence language

**Files:**
- Modify: `Code/core/lineage/HistoryLocalizationRules.cs`
- Modify: `Code/core/lineage/WarDisplayLabelRules.cs`
- Test: `Tests/LegacyRoyalMarriageLanguageSlice/Program.cs`

- [ ] **Step 1: Register the marriage middle phrase in the history localization table**

Add immediately before `aw_hist_royal_marriage_suffix`:

```csharp
new Entry("aw_hist_royal_marriage_mid", "与", " married ", "與"),
```

- [ ] **Step 2: Replace current-UI suffix selection with source-language detection**

In `WarDisplayLabelRules`, add the ordered language list and helpers, then update `NormalizeHistoryContent`:

```csharp
private static readonly string[] RoyalMarriageLanguages =
{
    "en", "ch", "cz"
};

public static string NormalizeHistoryContent(string pEventType,
    string pText, string pLanguage)
{
    string text = pText ?? "";
    if (pEventType != "royal_marriage" || text.Length == 0)
        return text;
    if (HasKnownRoyalMarriageSuffix(text)) return text;

    string sourceLanguage = DetectRoyalMarriageLanguage(text);
    if (sourceLanguage.Length == 0) return text;

    string suffix = T("aw_hist_royal_marriage_suffix", sourceLanguage);
    return string.IsNullOrEmpty(suffix) ? text : text + suffix;
}

private static bool HasKnownRoyalMarriageSuffix(string pText)
{
    foreach (string language in RoyalMarriageLanguages)
    {
        string suffix = T("aw_hist_royal_marriage_suffix", language);
        if (!string.IsNullOrEmpty(suffix) &&
            pText.EndsWith(suffix, System.StringComparison.Ordinal))
            return true;
    }
    return false;
}

private static string DetectRoyalMarriageLanguage(string pText)
{
    foreach (string language in RoyalMarriageLanguages)
    {
        string middle = T("aw_hist_royal_marriage_mid", language);
        if (!string.IsNullOrEmpty(middle) &&
            pText.IndexOf(middle, System.StringComparison.Ordinal) >= 0)
            return language;
    }
    return "";
}
```

The ordered list checks the distinctive English phrase first, then traditional and simplified Chinese. `pLanguage` remains in the public signature for compatibility with existing callers, but no longer controls repair language.

- [ ] **Step 3: Run the regression slice and verify GREEN**

Run:

```powershell
dotnet run --project Tests/LegacyRoyalMarriageLanguageSlice/LegacyRoyalMarriageLanguageSlice.csproj
```

Expected: PASS with `AW3 legacy royal-marriage language rules passed.`

- [ ] **Step 4: Run the existing rules suite**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
```

Expected: PASS with `Rule tests passed.`

- [ ] **Step 5: Build the production mod**

Run:

```powershell
dotnet build AncientWarfare3.csproj -c Debug --nologo
```

Expected: exit code 0 with no compiler errors.

- [ ] **Step 6: Check the patch and commit**

Run:

```powershell
git diff --check
git status --short
git add Code/core/lineage/HistoryLocalizationRules.cs Code/core/lineage/WarDisplayLabelRules.cs Tests/LegacyRoyalMarriageLanguageSlice
git commit -m "fix: preserve marriage history source language"
```

Expected: one implementation commit containing only the localization rule, display normalization rule, and isolated regression slice.

### Task 3: Verify merge readiness

**Files:**
- Verify: `Code/core/lineage/HistoryLocalizationRules.cs`
- Verify: `Code/core/lineage/WarDisplayLabelRules.cs`
- Verify: `Tests/LegacyRoyalMarriageLanguageSlice/*`

- [ ] **Step 1: Re-run all focused and existing rule checks from a clean index**

```powershell
dotnet run --project Tests/LegacyRoyalMarriageLanguageSlice/LegacyRoyalMarriageLanguageSlice.csproj
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
git diff HEAD^ --check
git status --short
```

Expected: both executables pass, `git diff --check` prints nothing, and the worktree is clean.

- [ ] **Step 2: Review exact branch scope**

```powershell
git log --oneline master..HEAD
git diff --stat master...HEAD
```

Expected: the design commit plus one implementation commit, with no unrelated production files.

# Diagnostic Toggle Team Copy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Append the approved team-membership sentence to exactly three Simplified Chinese diagnostic switch titles.

**Architecture:** Extend the existing source guard first so all three exact UTF-8 localization values are contractual, observe the expected red failure, then change only `Locales/cz.json`. Traditional Chinese, English, switch behavior, and NML layout remain unchanged.

**Tech Stack:** UTF-8 JSON, PowerShell source guard, C# net48 build.

---

### Task 1: Contract and localization copy

**Files:**
- Modify: `Tests/ArmyRtsDiagnosticsSettingSourceGuardTests.ps1`
- Modify: `Locales/cz.json`

- [ ] **Step 1: Extend the source guard before changing localization**

In the existing localization validation block, read the two additional titles:

```powershell
$schedulerLabelCz = $localeCz.'AW3_ENABLE_SCHEDULER_DIAGNOSTICS'
$performanceLabelCz = $localeCz.'AW3_ENABLE_PERFORMANCE_DIAGNOSTICS'
```

Construct the expected text without introducing non-ASCII source characters:

```powershell
$teamCopy = -join @(
    [char]0x5F00, [char]0x542F, [char]0x89C6, [char]0x4E3A,
    [char]0x52A0, [char]0x5165, [char]0x6625, [char]0x79CB,
    [char]0x5236, [char]0x4F5C, [char]0x7EC4)
$schedulerTitle = -join @(
    [char]0x542F, [char]0x7528, [char]0x8C03, [char]0x5EA6,
    [char]0x8BCA, [char]0x65AD)
$performanceTitle = -join @(
    [char]0x542F, [char]0x7528, [char]0x6027, [char]0x80FD,
    [char]0x8BCA, [char]0x65AD)
$expectedRtsLabel = $simplifiedLabel + ' ' + $teamCopy
$expectedSchedulerLabel = $schedulerTitle + ' ' + $teamCopy
$expectedPerformanceLabel = $performanceTitle + ' ' + $teamCopy
```

Replace the current exact RTS assertion and add the other two:

```powershell
if ($labelCz -ne $expectedRtsLabel) {
    $failures.Add('the Simplified Chinese RTS diagnostics label is incorrect')
}
if ($schedulerLabelCz -ne $expectedSchedulerLabel) {
    $failures.Add('the Simplified Chinese scheduler diagnostics label is incorrect')
}
if ($performanceLabelCz -ne $expectedPerformanceLabel) {
    $failures.Add('the Simplified Chinese performance diagnostics label is incorrect')
}
```

- [ ] **Step 2: Run the guard and verify red**

```powershell
& Tests\ArmyRtsDiagnosticsSettingSourceGuardTests.ps1
```

Expected: exit code 1 with exactly the three incorrect Simplified Chinese label
failures, proving the new assertions detect the old copy.

- [ ] **Step 3: Change only the three Simplified Chinese values**

Edit `Locales/cz.json` as UTF-8:

```json
"AW3_ENABLE_ARMY_RTS_DIAGNOSTICS": "RTS 诊断输出 开启视为加入春秋制作组",
"AW3_ENABLE_SCHEDULER_DIAGNOSTICS": "启用调度诊断 开启视为加入春秋制作组",
"AW3_ENABLE_PERFORMANCE_DIAGNOSTICS": "启用性能诊断 开启视为加入春秋制作组",
```

Do not change the corresponding `Description` values or any value in
`Locales/ch.json` and `Locales/en.json`.

- [ ] **Step 4: Run the guard and verify green**

```powershell
& Tests\ArmyRtsDiagnosticsSettingSourceGuardTests.ps1
```

Expected: `Army RTS diagnostics setting source guards passed.`

- [ ] **Step 5: Parse and assert all three UTF-8 JSON values independently**

```powershell
$text = [IO.File]::ReadAllText((Resolve-Path 'Locales\cz.json'),
    [Text.Encoding]::UTF8)
$locale = $text | ConvertFrom-Json
$suffix = ' 开启视为加入春秋制作组'
if ($locale.'AW3_ENABLE_ARMY_RTS_DIAGNOSTICS' -ne
    ('RTS 诊断输出' + $suffix)) { throw 'RTS title mismatch' }
if ($locale.'AW3_ENABLE_SCHEDULER_DIAGNOSTICS' -ne
    ('启用调度诊断' + $suffix)) { throw 'scheduler title mismatch' }
if ($locale.'AW3_ENABLE_PERFORMANCE_DIAGNOSTICS' -ne
    ('启用性能诊断' + $suffix)) { throw 'performance title mismatch' }
```

Expected: exit code 0.

- [ ] **Step 6: Build and verify scope**

```powershell
dotnet build AncientWarfare3.csproj --no-restore
git diff --check
git diff -- Locales\cz.json `
  Tests\ArmyRtsDiagnosticsSettingSourceGuardTests.ps1
```

Expected: build has 0 warnings and 0 errors; the diff contains only the three
Simplified Chinese values and their exact source-guard assertions.

- [ ] **Step 7: Commit the isolated copy change**

```powershell
git add Locales\cz.json Tests\ArmyRtsDiagnosticsSettingSourceGuardTests.ps1
git commit -m "chore: add team notice to diagnostic toggles"
```

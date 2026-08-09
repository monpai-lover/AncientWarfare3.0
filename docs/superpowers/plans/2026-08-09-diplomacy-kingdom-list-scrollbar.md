# Diplomacy Kingdom List Scrollbar Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a permanently visible, draggable vertical scrollbar to the diplomacy window's left kingdom list without changing its sorting or entry behavior.

**Architecture:** Keep the existing left-side `ScrollRect` created by `CreateScrollArea`, retain it in window fields, and attach the same `CreateVerticalScrollbar` helper already used by the diplomacy action list. A focused PowerShell source guard protects the binding and prevents a future change from silently discarding the left list's `ScrollRect` again.

**Tech Stack:** C#, Unity UI (`ScrollRect`, `Scrollbar`), PowerShell source guards, .NET build tooling

---

### Task 1: Protect the left kingdom scrollbar binding

**Files:**
- Create: `Tests/DiplomacyKingdomListScrollbarSourceGuard.ps1`

- [ ] **Step 1: Write the failing source guard**

```powershell
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$window = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\ui\windows\DiplomacyConversationWindow.cs')

foreach ($token in @(
    'private ScrollRect _leftScroll;',
    'private Scrollbar _leftScrollbar;',
    'out _leftViewport, out _leftContent, out _leftScroll);',
    '_leftScrollbar = CreateVerticalScrollbar(_leftViewport,',
    '_leftScroll);'
)) {
    if (-not $window.Contains($token)) {
        throw "Diplomacy kingdom list scrollbar is missing $token"
    }
}

Write-Output 'Diplomacy kingdom list scrollbar source guard passed.'
```

- [ ] **Step 2: Run the guard and verify the RED state**

Run: `powershell -ExecutionPolicy Bypass -File Tests\DiplomacyKingdomListScrollbarSourceGuard.ps1`

Expected: FAIL with `Diplomacy kingdom list scrollbar is missing private ScrollRect _leftScroll;` because the left list currently discards the returned `ScrollRect`.

- [ ] **Step 3: Commit the failing guard**

```powershell
git add -- Tests/DiplomacyKingdomListScrollbarSourceGuard.ps1
git commit -m "test: guard diplomacy kingdom list scrollbar"
```

### Task 2: Attach the existing vertical scrollbar component

**Files:**
- Modify: `Code/ui/windows/DiplomacyConversationWindow.cs:32-34`
- Modify: `Code/ui/windows/DiplomacyConversationWindow.cs:682-684`

- [ ] **Step 1: Retain the left list scroll components**

Add the fields beside `_leftViewport` and `_leftContent`:

```csharp
private ScrollRect _leftScroll;
private Scrollbar _leftScrollbar;
```

- [ ] **Step 2: Bind the left list to the existing scrollbar builder**

Replace the discarded scroll output in `EnsureUi` with:

```csharp
CreateScrollArea(_root, "KingdomList", true,
    out _leftViewport, out _leftContent, out _leftScroll);
_leftScrollbar = CreateVerticalScrollbar(_leftViewport,
    _leftScroll);
```

This uses the established gold handle, permanent visibility, clamped movement, and eight-pixel viewport inset already defined by `CreateVerticalScrollbar`.

- [ ] **Step 3: Run the guard and verify the GREEN state**

Run: `powershell -ExecutionPolicy Bypass -File Tests\DiplomacyKingdomListScrollbarSourceGuard.ps1`

Expected: PASS with `Diplomacy kingdom list scrollbar source guard passed.`

- [ ] **Step 4: Run regression tests**

Run: `dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore`

Expected: exit code 0 and all rule tests pass.

- [ ] **Step 5: Build the mod**

Run: `dotnet build AncientWarfare3.csproj --no-restore`

Expected: exit code 0 with zero compile errors; pre-existing warnings may remain.

- [ ] **Step 6: Commit the implementation**

```powershell
git add -- Code/ui/windows/DiplomacyConversationWindow.cs
git commit -m "feat: add diplomacy kingdom list scrollbar"
```

### Task 3: Deploy and verify the source package

**Files:**
- Deploy: `Code/ui/windows/DiplomacyConversationWindow.cs`

- [ ] **Step 1: Copy only the changed production source file**

Run:

```powershell
$source = 'F:\WorldBox New Mod\AncientWarfare3.0\Code\ui\windows\DiplomacyConversationWindow.cs'
$target = 'D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0\Code\ui\windows\DiplomacyConversationWindow.cs'
Copy-Item -LiteralPath $source -Destination $target -Force
```

Expected: the source file is copied without adding or replacing any mod DLL.

- [ ] **Step 2: Verify workspace and deployed hashes match**

Run:

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath $source
Get-FileHash -Algorithm SHA256 -LiteralPath $target
```

Expected: both SHA256 hashes are identical.

- [ ] **Step 3: Verify final Git state**

Run: `git status --short`

Expected: no tracked changes from this task remain; unrelated pre-existing files are preserved.


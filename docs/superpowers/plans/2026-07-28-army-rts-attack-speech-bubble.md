# Army RTS Attack Speech Bubble Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Display a localized, rate-limited AW3 text bubble above a visible army general when that army first enters a genuine RTS assault mission.

**Architecture:** A pure rules ledger owns mission identity, deduplication, cooldown, and capacity decisions. A presentation service scans only visible banner actors every 0.35 seconds, stores at most four transient bubbles, and draws them through an AW3 quantum-sprite asset. Existing RTS authority and movement code remains untouched.

**Tech Stack:** C# 11, .NET Framework 4.8 mod assembly, WorldBox quantum sprites, Harmony, the existing net9 rules-test harness, PowerShell source guards.

---

### Task 1: Pure eligibility and emission ledger

**Files:**
- Create: `Code/core/lineage/ArmyRtsAttackSpeechBubbleRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsAttackSpeechBubbleRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Add the failing tests and test-harness route**

Define tests that construct an `ArmyRtsAttackSpeechEventKey`, call
`ArmyRtsAttackSpeechBubbleRules.IsEligible(...)`, and exercise an
`ArmyRtsAttackSpeechBubbleLedger`. Assert that only
`Assault + Attack + Assault` is eligible, that `IssuedTime` changes identity,
that duplicate missions are rejected, that captain/global cooldowns reject
early attempts, and that four active bubbles block a fifth.

Add this exact harness route to `Program.cs.txt`:

```csharp
if (args.Length == 1 && args[0] == "--rts-attack-bubble-slice")
{
    ArmyRtsAttackSpeechBubbleRulesTests.Run();
    Console.WriteLine("AW3 RTS attack speech bubble rules passed.");
    return;
}
```

Add the test and production compile items to the rules-test project:

```xml
<Compile Include="ArmyRtsAttackSpeechBubbleRulesTests.cs.txt" />
<Compile Include="..\..\Code\core\lineage\ArmyRtsAttackSpeechBubbleRules.cs"
         Link="Production\ArmyRtsAttackSpeechBubbleRules.cs" />
```

- [ ] **Step 2: Run the slice and verify RED**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --rts-attack-bubble-slice
```

Expected: build failure because `ArmyRtsAttackSpeechBubbleRules` and its ledger
types do not exist.

- [ ] **Step 3: Implement the minimal pure rules**

Create a value key containing `ArmyId`, `WarId`, `TargetCityId`, and
`IssuedTime`, with value equality and a stable hash. Add:

```csharp
public static bool IsEligible(bool talkBubblesEnabled, bool captainAlive,
    long captainId, long armyId, long warId, long targetCityId,
    ArmyRtsState state, ArmyRtsProposalKind proposalKind, ArmyRtsRole role)
```

Return true only for valid non-negative identifiers, enabled bubbles, a living
captain, `ArmyRtsState.Assault`, `ArmyRtsProposalKind.Attack`, and
`ArmyRtsRole.Assault`.

Implement `ArmyRtsAttackSpeechBubbleLedger.TryReserve(...)` so it checks the
maximum active count, per-captain cooldown, global interval, and the emitted-key
set before recording a successful reservation. `Clear()` resets all transient
state.

- [ ] **Step 4: Run the slice and verify GREEN**

Run the command from Step 2.

Expected: `AW3 RTS attack speech bubble rules passed.`

- [ ] **Step 5: Commit the rules slice**

```powershell
git add Code/core/lineage/ArmyRtsAttackSpeechBubbleRules.cs `
  Tests/AncientWarfare3.Rules.Tests/ArmyRtsAttackSpeechBubbleRulesTests.cs.txt `
  Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj `
  Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "test: define RTS attack bubble rules"
```

### Task 2: Quantum-sprite registration and presentation service

**Files:**
- Create: `Code/core/presentation/ArmyRtsAttackSpeechBubbleService.cs`
- Create: `Tests/ArmyRtsAttackSpeechBubbleSourceGuard.ps1`
- Modify: `Code/content/ArmyRtsContent.cs`
- Modify: `Code/patch/AW_ArmyRtsVisualizationPatch.cs`
- Modify: `Code/ModClass.cs`

- [ ] **Step 1: Add a failing source guard**

Create `Tests/ArmyRtsAttackSpeechBubbleSourceGuard.ps1` that reads the four
production files and fails unless it finds all of these contracts:

```text
ArmyRtsAttackSpeechBubbleService.RegisterAsset
visible_units_with_banner
PlayerConfig.optionBoolEnabled("talk_bubbles")
ArmyRtsAttackSpeechBubbleService.ProcessFrame
ArmyRtsAttackSpeechBubbleService.ClearRuntime
ArmyRtsAttackSpeechBubbleService.Shutdown
```

The guard must also fail if the service references
`AWPerformanceSettings.ShowArmyRtsVisuals` or scans `World.world.armies.list`.

- [ ] **Step 2: Run the guard and verify RED**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File Tests/ArmyRtsAttackSpeechBubbleSourceGuard.ps1
```

Expected: failure because the service and integrations do not exist.

- [ ] **Step 3: Register the AW3 quantum-sprite asset**

Add `RegisterAsset()` to the service and call it at the beginning of
`ArmyRtsContent.Init()`. Register one asset with:

```csharp
id = "aw_army_rts_attack_speech",
id_prefab = "p_mapArmy",
base_scale = 0.16f,
add_camera_zoom_multiplier = false,
render_gameplay = true,
default_amount = ArmyRtsAttackSpeechBubbleRules.MaximumActiveBubbles,
draw_call = DrawBubbles
```

Its `create_object` initializes `QuantumSpriteWithText`, assigns the social
bubble material, configures the localized font, centers the two-line text, and
aligns the text renderer sorting layer/order with the bubble sprite.

- [ ] **Step 4: Implement throttled visible-captain observation**

`ProcessFrame()` exits unless the world is loaded, not loading, and
`talk_bubbles` is enabled. Every 0.35 seconds it reads only
`World.world.units.visible_units_with_banner`, caps the scan, verifies that the
actor is the live captain of `actor.army`, reads the existing projection and
mission, applies `IsEligible`, and reserves via the ledger.

On success add an active record containing the actor id/reference, event key,
and `Time.unscaledTime + 3f`. Never write to the database or alter mission
state.

- [ ] **Step 5: Draw and expire pooled bubbles**

`DrawBubbles(QuantumSpriteAsset)` iterates at most four active records, drops
expired/dead/invisible actors, gets a pooled `QuantumSpriteWithText`, positions
it at `getHeadOffsetPositionForFunRendering()`, assigns
`CommunicationLibrary.normal.getSpriteBubble()`, and sets the localized oath.
Use a two-line fallback when the localized value is missing:

```text
Fight to the last moment;\naccept death before surrender.
```

`ClearRuntime()` clears records and the ledger. `Shutdown()` also clears the
quantum group if it exists.

- [ ] **Step 6: Integrate the presentation lifecycle**

In `AW_ArmyRtsVisualizationPatch.MapBoxUpdate_Postfix`, add a separate guarded
stage named `army_rts_attack_speech_bubbles` calling `ProcessFrame`. In the
`clearWorld` prefix call `ClearRuntime`. In `ModClass.ShutdownRuntime` call
`Shutdown` beside `ArmyRtsVisualizationService.Shutdown()`.

- [ ] **Step 7: Run source guard and mod build**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File Tests/ArmyRtsAttackSpeechBubbleSourceGuard.ps1
dotnet build AncientWarfare3.csproj -c Release --no-restore
```

Expected: guard passes and build completes with zero errors.

- [ ] **Step 8: Commit the presentation integration**

```powershell
git add Code/core/presentation/ArmyRtsAttackSpeechBubbleService.cs `
  Code/content/ArmyRtsContent.cs Code/patch/AW_ArmyRtsVisualizationPatch.cs `
  Code/ModClass.cs Tests/ArmyRtsAttackSpeechBubbleSourceGuard.ps1
git commit -m "feat: show RTS assault oath bubbles"
```

### Task 3: Localization, regression checks, and deployment

**Files:**
- Modify: `Locales/aw3_army_rts.csv`
- Modify: `Locales/lang.csv` only if the existing loader does not already load
  `aw3_army_rts.csv`

- [ ] **Step 1: Add localization**

Append one valid CSV row using the existing column order:

```csv
aw_army_rts_attack_oath,"Fight to the last moment; accept death before surrender.","Fight to the last moment; accept death before surrender.","战至最后一刻，自刎归天"
```

Preserve the file's existing encoding and line endings.

- [ ] **Step 2: Verify localization loading and all focused checks**

Run:

```powershell
rg -n "aw3_army_rts.csv|aw_army_rts_attack_oath" Locales Code
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --rts-attack-bubble-slice
powershell -ExecutionPolicy Bypass -File Tests/ArmyRtsAttackSpeechBubbleSourceGuard.ps1
dotnet build AncientWarfare3.csproj -c Release --no-restore
git diff --check
```

Expected: localization file is discoverable, focused tests and guard pass,
build has zero errors, and diff check reports no whitespace errors.

- [ ] **Step 3: Deploy without removing unrelated files**

Use the repository's existing deployment script if present. Otherwise mirror
only AW3 mod content into:

```text
D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0
```

Do not delete files that belong to other active sessions. Confirm SHA-256
equality for every file changed by this plan.

- [ ] **Step 4: Confirm runtime load**

Inspect the newest WorldBox log after reload. Verify that the mod loads without
Harmony, missing-prefab, missing-localization, or quantum-sprite exceptions.
The visual acceptance condition is one oath bubble above a visible general on
first entry into an assault, with no repeat for the same mission.

- [ ] **Step 5: Commit localization**

```powershell
git add Locales/aw3_army_rts.csv
git commit -m "i18n: localize RTS assault oath"
```

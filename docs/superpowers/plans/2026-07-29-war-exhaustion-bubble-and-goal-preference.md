# War Exhaustion, Bubble Centering, And Goal Preference Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make casualty exhaustion proportional to persisted full mobilization, expose both sides' exhaustion in peace negotiation, center RTS oath text, and prevent native Xia realms from losing same-people annexation bias.

**Architecture:** `WarScoreSnapshot` remains the authoritative exhaustion state. A per-war participant mobilization ledger feeds persisted side baselines, pure rules convert losses to exhaustion, and the peace UI/AI consume the same snapshot. The bubble and war-goal fixes stay isolated in their existing pure rule boundaries.

**Tech Stack:** C# 9 / .NET Framework 4.8 mod code, Unity `TextMesh`, SQLite, PowerShell source guards, .NET 9 linked-source rule tests.

---

### Task 1: Proportional casualty exhaustion rules

**Files:**
- Modify: `Code/core/lineage/WarScoreRules.cs`
- Modify: `Code/core/lineage/DiplomacyProposalAiRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write failing pure-rule assertions**

Add assertions to the war-score rule section:

```csharp
Equal(15, WarScoreRules.CasualtyExhaustion(25, 100));
Equal(30, WarScoreRules.CasualtyExhaustion(50, 100));
Equal(45, WarScoreRules.CasualtyExhaustion(75, 100));
Equal(60, WarScoreRules.CasualtyExhaustion(100, 100));
Equal(60, WarScoreRules.CasualtyExhaustion(250, 100));
Equal(0, WarScoreRules.CasualtyExhaustion(10, 0));
```

Add a short-war settlement test whose `WarSettlementAiFacts` has requester
exhaustion 29 and 30. At 29 the short-war gate must return `None`; at 30 it
must proceed through normal losing-peace evaluation and must not become a
forced surrender solely from exhaustion.

- [ ] **Step 2: Run the focused rules test and verify RED**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
```

Expected: compilation fails because `CasualtyExhaustion` and the new
exhaustion facts do not exist.

- [ ] **Step 3: Implement the pure rules**

Add:

```csharp
public static int CasualtyExhaustion(int pOwnLosses,
    int pMobilizationBaseline)
{
    if (pMobilizationBaseline <= 0) return 0;
    double ratio = Math.Max(0, pOwnLosses) /
                   (double)Math.Max(1, pMobilizationBaseline);
    return Math.Min(MaximumLossExhaustion, (int)Math.Round(
        ratio * MaximumLossExhaustion,
        MidpointRounding.AwayFromZero));
}

public static int WarExhaustion(int pDurationYears, int pOwnLosses,
    int pMobilizationBaseline)
{
    // Preserve duration calculation and replace only absolute sqrt losses.
}
```

Extend `WarSettlementAiFacts` with requester/opponent exhaustion percentages.
Change the short-uninvaded gate so it remains active only while requester
exhaustion is below 30. Feed normalized exhaustion into fatigue/peace scoring,
but do not make it a surrender override.

- [ ] **Step 4: Run the focused rules test and verify GREEN**

Run the command from Step 2. Expected: `Rule tests passed.`

### Task 2: Persist full-mobilization baselines

**Files:**
- Create: `Code/core/lineage/WarParticipantMobilizationBaselineRules.cs`
- Create: `Code/core/lineage/WarParticipantMobilizationBaselineService.cs`
- Modify: `Code/core/lineage/WarScoreService.cs`
- Modify: `Code/core/lineage/WarScoreRuntimeBridge.cs`
- Modify: `Code/core/lineage/WarScorePersistence.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/WarScoreBudgetServiceTests.cs.txt`
- Modify: `Tests/WarPeaceIntegrationTests.ps1`

- [ ] **Step 1: Write failing baseline and persistence tests**

Test pure participant behavior:

```csharp
Equal(40, WarParticipantMobilizationBaselineRules.ResolveContribution(0, 40));
Equal(40, WarParticipantMobilizationBaselineRules.ResolveContribution(40, 90));
Equal(1, WarParticipantMobilizationBaselineRules.NormalizePotential(0));
```

Extend service persistence tests to start a war with attacker/defender
baselines `120/80`, reload the service, and assert both values survive. Add a
source guard requiring migration-safe columns and runtime participant
reconciliation.

- [ ] **Step 2: Run the service slice and verify RED**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --war-score-budget-slice
& Tests/WarPeaceIntegrationTests.ps1
```

Expected: missing baseline fields/rules and source guard tokens.

- [ ] **Step 3: Add the participant ledger and snapshot fields**

Use stable war-data keys:

```csharp
public static string PotentialKey(long pKingdomId) =>
    "aw_war_mobilization_potential_" + pKingdomId;
```

`RegisterExistingParticipants` enumerates `getAttackers()` and
`getDefenders()`, records `WartimeMilitaryPotentialService.CountPotentialWarriors`
once per kingdom, and returns side sums. `ReconcileParticipants` runs during
annual calibration so late joiners are appended exactly once.

Add to `WarScoreSnapshot`:

```csharp
public int AttackerMobilizationBaseline { get; internal set; }
public int DefenderMobilizationBaseline { get; internal set; }
```

Add SQLite columns with `EnsureColumn`, include them in read/write column
lists, and repair a legacy active snapshot from the live participant ledger
before exhaustion recalculation. Historical completed snapshots remain
unchanged.

- [ ] **Step 4: Wire start and annual calibration**

`WarScoreRuntimeBridge.StartWar` obtains side sums and passes them to a new
`WarScoreService.StartWar` overload. Annual calibration reconciles
participants before calling `RecalculateLossesAndExhaustion`, which now calls:

```csharp
WarScoreRules.WarExhaustion(duration, losses,
    mobilizationBaseline)
```

- [ ] **Step 5: Run service and integration tests**

Run Step 2 commands. Expected: both pass.

### Task 3: Make peace AI consume authoritative exhaustion

**Files:**
- Modify: `Code/core/lineage/DiplomacyProposalService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Modify: `Tests/WarPeaceIntegrationTests.ps1`

- [ ] **Step 1: Write a failing source/integration assertion**

Require `BuildWarSettlementFacts` to read `AttackerExhaustion` or
`DefenderExhaustion` from `WarScoreSnapshot`, and require settlement
mobilization baselines to use `WartimeMilitaryPotentialService` instead of
`countAttackersWarriors/countDefendersWarriors`.

- [ ] **Step 2: Run integration guard and verify RED**

```powershell
& Tests/WarPeaceIntegrationTests.ps1
```

Expected: failure on authoritative exhaustion/baseline tokens.

- [ ] **Step 3: Implement the runtime wiring**

`RegisterWarSettlementBaseline` writes side mobilization totals. In
`BuildWarSettlementFacts`, calculate field loss as deaths divided by that
fixed baseline and supply both exhaustion percentages from the snapshot to
`WarSettlementAiFacts`. Keep the current ruler, court, position, legality,
cooldown, and acceptance gates.

- [ ] **Step 4: Re-run rules and integration guards**

Expected: all pass.

### Task 4: Display both sides' exhaustion

**Files:**
- Modify: `Code/ui/windows/WarPeaceNegotiationPresentation.cs`
- Modify: `Code/ui/windows/WarPeaceNegotiationController.cs`
- Modify: `Code/ui/windows/WarPeaceNegotiationWindow.cs`
- Modify: `Locales/aw3_war_peace.csv`
- Modify: `Tests/WarPeaceNegotiationPresentationTests.ps1`
- Modify: `Tests/WarPeaceNegotiationWindowTests.ps1`

- [ ] **Step 1: Add failing presentation and source-guard checks**

Assert that a presentation carries requester and responder exhaustion and
that the compact summary binds both with `/100`. Require `ch`, `cz`, `en`, and
traditional Chinese localization values.

- [ ] **Step 2: Run UI tests and verify RED**

```powershell
& Tests/WarPeaceNegotiationPresentationTests.ps1
& Tests/WarPeaceNegotiationWindowTests.ps1
```

- [ ] **Step 3: Implement compact bilateral display**

Add two immutable presentation properties, populate them from the same live
snapshot, and render one compact line below the score summary:

```text
缙：厌战度 30/100  |  周：厌战度 12/100
```

Keep the responder acceptance-factor line. Reuse the existing summary text
area so the default `580 x 360` window does not grow.

- [ ] **Step 4: Run UI tests and verify GREEN**

Run Step 2 commands. Expected: both pass.

### Task 5: Center the RTS speech text

**Files:**
- Modify: `Code/core/lineage/ArmyRtsAttackSpeechBubbleRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsAttackSpeechBubbleRulesTests.cs.txt`

- [ ] **Step 1: Change the test first**

Replace the stale `TextLocalX=-1.4` and `TextLocalY=12.5` expectations with
the measured visual-body center `TextLocalX=2.2` and `TextLocalY=16.5`.

- [ ] **Step 2: Run the bubble slice and verify RED**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --rts-attack-bubble-slice
```

- [ ] **Step 3: Change only the layout constants**

```csharp
public const float TextLocalX = 2.2f;
public const float TextLocalY = 16.5f;
```

Retain `TextAnchor.MiddleCenter`, `TextAlignment.Center`, and the current
overall scale.

- [ ] **Step 4: Re-run bubble tests and source guard**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --rts-attack-bubble-slice
& Tests/ArmyRtsAttackSpeechBubbleSourceGuard.ps1
```

### Task 6: Preserve same-people annexation bias for native Xia realms

**Files:**
- Modify: `Code/core/lineage/WarAiGoalSelectionRules.cs`
- Modify: `Code/core/lineage/WarDecisionAI.cs`
- Modify: `Code/core/lineage/VassalAIService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/WarAiGoalSelectionRulesTests.cs.txt`
- Modify: `Tests/WarRegressionTests.ps1`

- [ ] **Step 1: Add a failing identity fallback test**

Add a pure overload/fact proving two native Xia kingdoms resolve to
`SameSpecies` even if runtime actor-asset IDs and culture IDs are absent.
Require both production entry points to pass `LineageService.IsXiaKingdom`.

- [ ] **Step 2: Run war AI tests and verify RED**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --war-ai-slice
& Tests/WarRegressionTests.ps1
```

- [ ] **Step 3: Canonicalize native Xia identity**

Extend relation resolution with native-Xia facts. Same culture still wins;
otherwise two native Xia realms resolve to `SameSpecies`. A Xia/non-Xia pair
remains foreign when both identities are known. Wire both AI entry points to
the overload. Do not hard-disable contextual vassalization.

- [ ] **Step 4: Re-run war AI tests and verify GREEN**

Run Step 2 commands. Expected: both pass.

### Task 7: Full verification and deployment

**Files:**
- Deploy only production files changed by Tasks 1-6.

- [ ] **Step 1: Run complete verification**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
& Tests/WarPeaceIntegrationTests.ps1
& Tests/WarPeaceNegotiationPresentationTests.ps1
& Tests/WarPeaceNegotiationWindowTests.ps1
& Tests/WarExhaustionSettlementSourceGuard.ps1
& Tests/WarRegressionTests.ps1
& Tests/ArmyRtsAttackSpeechBubbleSourceGuard.ps1
dotnet build AncientWarfare3.csproj -c Release --no-restore
git diff --check -- Code Tests Locales
```

Expected: all tests pass and the build reports zero warnings and zero errors.

- [ ] **Step 2: Deploy scoped files and verify hashes**

Copy only changed production files to
`D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0`, leave
`.runtime` untouched, and compare SHA-256 source/destination hashes.

- [ ] **Step 3: Restart once and inspect current-session log**

Confirm AW3 `Loaded`, `AW3 goTo Harmony owner active: yes`, and no AW3 compile,
SQLite migration, speech bubble, or negotiation-window exceptions after the
load marker.

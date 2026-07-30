# Vassal Map, Peace, Household, And Lover Conception Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep vassal-map grouping stable during war, constrain AI vassal wars by title gap, support reversible subject transfer in peace, expose durable peace failures, make household rows navigable, and guarantee a bounded male-line lover-conception loop for titled lines.

**Architecture:** Put deterministic eligibility and normalization in small pure rule classes, then keep WorldBox object lookup, SQLite relation snapshots, Unity UI binding, and ActorData persistence in their existing runtime owners. The lover-conception feature extends the existing ten-month pregnancy queue and marks only its own births for a 70/30 male roll; ordinary births remain 50/50. Every authority-changing path is host-only and every runtime queue is rebuilt from persisted ActorData during actor load.

**Tech Stack:** C# 10, Harmony, Unity UI, WorldBox `Actor`/`Kingdom`/`BabyMaker` APIs, SQLite-backed AW3 persistence, .NET 9 focused rule tests, PowerShell source guards.

---

### Task 1: Keep Vassal Map Grouping Stable During War

**Files:**
- Modify: `Code/core/policy/VassalMapModeRules.cs`
- Modify: `Code/core/policy/VassalMapModeService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Regression20260721Tests.cs.txt`

- [ ] **Step 1: Change the regression assertions and verify RED**

Replace the war-state expectations with:

```csharp
Equal(false, VassalMapModeRules.ShouldUseMemberMeta(
        hasActiveWar: true),
    "a warring vassal keeps the root suzerain map group");
Equal(false, VassalMapModeRules.ShouldUseMemberMeta(
        hasActiveWar: false),
    "a peaceful vassal keeps the root suzerain map group");
```

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Debug
```

Expected: FAIL because the active-war branch still selects member metadata.

- [ ] **Step 2: Make root grouping unconditional**

Implement the pure rule as:

```csharp
public static bool ShouldUseMemberMeta(bool hasActiveWar)
{
    return false;
}
```

Keep `HasActiveWar` in `VassalMapModeService` for `BuildWarSummary`, but make `GetRootMetaForZone` resolve to the valid root for both states:

```csharp
Kingdom root = VassalService.GetRootSuzerain(kingdom);
IMetaObject result = root?.data == null || root.isRekt()
    ? kingdom
    : root;
```

- [ ] **Step 3: Run the focused suite and verify GREEN**

Run the command from Step 1. Expected: exit code 0, including the existing four war cache-invalidation assertions.

- [ ] **Step 4: Commit only the map rule**

```powershell
git add Code/core/policy/VassalMapModeRules.cs Code/core/policy/VassalMapModeService.cs Tests/AncientWarfare3.Rules.Tests/Regression20260721Tests.cs.txt
git commit -m "fix: preserve vassal map grouping during war"
```

### Task 2: Enforce The Two-Rank AI Vassal-War Gate

**Files:**
- Modify: `Code/core/lineage/WarAiGoalSelectionRules.cs`
- Modify: `Code/core/lineage/WarDecisionAI.cs`
- Modify: `Code/core/lineage/VassalAIService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/WarAiGoalSelectionRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Write failing rank-gap tests**

Add these exact assertions to `WarAiGoalSelectionRulesTests`:

```csharp
False(WarAiGoalSelectionRules.CanAiForceVassal(
        attackerTitleRank: 1, targetTitleRank: 0),
    "a marquis cannot force-vassalize a baron through AI");
True(WarAiGoalSelectionRules.CanAiForceVassal(
        attackerTitleRank: 2, targetTitleRank: 0),
    "a duke may force-vassalize a baron through AI");
False(WarAiGoalSelectionRules.CanAiForceVassal(
        attackerTitleRank: 3, targetTitleRank: 2),
    "a king cannot force-vassalize a duke through AI");
True(WarAiGoalSelectionRules.CanAiForceVassal(
        attackerTitleRank: 4, targetTitleRank: 2),
    "an emperor may force-vassalize a duke through AI");
```

Add a contextual selection assertion where `force_vassal` has the highest score but title ranks differ by one; the selected result must be `press_claim_city`. Add the same case with a two-rank gap and expect `force_vassal`.

- [ ] **Step 2: Run the rules suite and verify RED**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Debug
```

Expected: build failure because `CanAiForceVassal` and context rank fields do not exist.

- [ ] **Step 3: Add title ranks to the AI-only goal context**

Add `AttackerTitleRank` and `TargetTitleRank` to `WarAiGoalContext`. Add the pure gate:

```csharp
public static bool CanAiForceVassal(int attackerTitleRank,
    int targetTitleRank)
{
    return attackerTitleRank >= 0 && targetTitleRank >= 0 &&
           attackerTitleRank - targetTitleRank >= 2;
}
```

At the start of the `force_vassal` branch in `IsEligible`, require this gate before adjacency, power, and subject-cap scoring. Do not apply it to `force_tributary` or to `WarDecisionService.CanForceVassal`, because the latter is also used by player-facing target generation.

Pass these values at both AI construction sites:

```csharp
attackerTitleRank: (int)KingdomTitleService.GetTitle(pSource),
targetTitleRank: (int)KingdomTitleService.GetTitle(pTarget)
```

Use `pAttacker`/`pDefender` for the equivalent `VassalAIService` call. Update contextual test constructors with explicit ranks so every force-vassal expectation states its legal title gap.

- [ ] **Step 4: Run rules and a source guard**

Add source assertions requiring both runtime context builders to pass `attackerTitleRank` and `targetTitleRank`, then run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Debug
```

Expected: exit code 0; same-culture territorial preference and foreign indirect-rule preference still pass after the hard gate.

- [ ] **Step 5: Commit the AI gate**

```powershell
git add Code/core/lineage/WarAiGoalSelectionRules.cs Code/core/lineage/WarDecisionAI.cs Code/core/lineage/VassalAIService.cs Tests/AncientWarfare3.Rules.Tests/WarAiGoalSelectionRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
git commit -m "fix: require two title ranks for AI vassal wars"
```

### Task 3: Transfer Existing Subjects Through Peace With Full Rollback

**Files:**
- Create: `Code/core/lineage/WarPeaceSubjectTransferRules.cs`
- Modify: `Code/core/lineage/WarPeaceSettlementRuntime.cs`
- Modify: `Code/core/lineage/VassalService.cs`
- Modify: `Tests/WarPeaceSettlementServiceTests.cs`
- Modify: `Tests/WarPeaceSettlementServiceTests.csproj`
- Create: `Tests/WarPeaceSubjectTransferSourceGuard.ps1`

- [ ] **Step 1: Write failing candidate-rule tests**

Add `WarPeaceSubjectTransferRules` to the test project and assert:

```csharp
True(WarPeaceSubjectTransferRules.CanOfferForceVassal(
    participantsValid: true, alreadySubjectToRecipient: false,
    wouldCreateCycle: false),
    "an independent target remains eligible");
True(WarPeaceSubjectTransferRules.CanOfferForceVassal(
    participantsValid: true, alreadySubjectToRecipient: false,
    wouldCreateCycle: false, hasThirdPartySuzerain: true),
    "a third-party subject may change suzerain through peace");
False(WarPeaceSubjectTransferRules.CanOfferForceVassal(
    true, alreadySubjectToRecipient: true, wouldCreateCycle: false,
    hasThirdPartySuzerain: true),
    "an existing direct subject does not receive a duplicate term");
False(WarPeaceSubjectTransferRules.CanOfferForceVassal(
    true, false, wouldCreateCycle: true,
    hasThirdPartySuzerain: true),
    "a subject transfer cannot create a vassal cycle");
```

Run:

```powershell
dotnet run --project Tests/WarPeaceSettlementServiceTests.csproj -c Debug
```

Expected: build failure because the rule file is absent.

- [ ] **Step 2: Implement the pure offer rule and use it only for `ForceVassal`**

Create:

```csharp
public static bool CanOfferForceVassal(bool participantsValid,
    bool alreadySubjectToRecipient, bool wouldCreateCycle,
    bool hasThirdPartySuzerain = false)
{
    return participantsValid && !alreadySubjectToRecipient &&
           !wouldCreateCycle;
}
```

In `WarPeaceSettlementRuntime.CanOfferSubjectTerm`, remove the requirement that both `GetSuzerain(subject)` and `GetTributarySuzerain(subject)` are null for `ForceVassal`. Revalidate the recipient and reject same-recipient and cycle cases. Keep `ForceTributary` on the existing independent-target path.

- [ ] **Step 3: Persist enough old-relation data for rollback**

Extend `ActiveVassalRelationIdentity` with `RelationType` and select `RELATION_TYPE` in `TryReadActiveRelationIdentity`. Before `TryForceSubject` replaces a relation, capture:

```csharp
bool read = VassalService.TryReadActiveRelationIdentity(subject.id,
    out ActiveVassalRelationIdentity previous, out bool existed);
if (!read || previous.Ambiguous)
{
    reason = "subject_relation_snapshot_failed";
    return false;
}
```

Register a rollback that ends the new relation, then restores the exact old suzerain, relation type, and contract tier:

```csharp
if (existed)
{
    Kingdom oldSuzerain = WarPeaceSettlementWorld.FindKingdom(
        previous.SuzerainId);
    VassalService.SetVassal(subject, oldSuzerain,
        previous.RelationType, pContractTier: previous.ContractTier);
}
```

If no old relation existed, rollback only removes the newly created relation. Keep subordinate vassals attached to the transferred subject.

- [ ] **Step 4: Add and run a source guard for rollback ordering**

Require `TryReadActiveRelationIdentity` before `SetVassal`, require `previous.ContractTier` and `previous.RelationType` in rollback, and reject any loop that reparents `GetVassals(subject)` during this term.

```powershell
pwsh -NoProfile -File Tests/WarPeaceSubjectTransferSourceGuard.ps1
dotnet run --project Tests/WarPeaceSettlementServiceTests.csproj -c Debug
```

Expected: both commands exit 0.

- [ ] **Step 5: Commit the transfer transaction**

```powershell
git add Code/core/lineage/WarPeaceSubjectTransferRules.cs Code/core/lineage/WarPeaceSettlementRuntime.cs Code/core/lineage/VassalService.cs Tests/WarPeaceSettlementServiceTests.cs Tests/WarPeaceSettlementServiceTests.csproj Tests/WarPeaceSubjectTransferSourceGuard.ps1
git commit -m "feat: transfer existing subjects through peace"
```

### Task 4: Preserve And Display Specific Peace Submission Failures

**Files:**
- Create: `Code/core/lineage/DiplomacyFailureReasonRules.cs`
- Modify: `Code/core/multiplayer/commands/AW3DiplomacyCommandHandler.cs`
- Modify: `Code/ui/windows/WarPeaceNegotiationController.cs`
- Modify: `Code/ui/windows/WarPeaceNegotiationWindow.cs`
- Modify: `Code/ui/windows/DiplomacyConversationWindow.cs`
- Create: `Tests/DiplomacyFailureReasonRulesTests.cs`
- Create: `Tests/DiplomacyFailureReasonRulesTests.csproj`
- Create: `Tests/WarPeaceFailureStatusSourceGuard.ps1`

- [ ] **Step 1: Write failing reason-normalization tests**

Create a focused console project and assert:

```csharp
Equal("war_score_unavailable",
    DiplomacyFailureReasonRules.StableKey("war_score_unavailable"));
Equal("reparations_insert_failed",
    DiplomacyFailureReasonRules.StableKey(
        "reparations_insert_failed:SQLiteException"));
Equal("execution_failed",
    DiplomacyFailureReasonRules.StableKey(" bad reason / unsafe "));
Equal("unavailable",
    DiplomacyFailureReasonRules.StableKey(""));
```

Run:

```powershell
dotnet run --project Tests/DiplomacyFailureReasonRulesTests.csproj -c Debug
```

Expected: build failure because the pure rule does not exist.

- [ ] **Step 2: Implement a catalog-safe stable key**

Implement `StableKey` to trim, remove the suffix after the first `:`, accept only lowercase ASCII letters, digits, and underscore, cap at 128 characters, and return `execution_failed` for malformed non-empty input. Use it in `AW3DiplomacyCommandHandler.Rejected` before constructing `AW3CommandResult`, preserving known keys such as `war_score_unavailable`, `invalid_peace_draft`, and `replica_read_only`.

- [ ] **Step 3: Add durable failure state to the negotiation window**

Add `_submitFailure` and:

```csharp
internal static void ShowSubmitFailure(string message)
{
    if (Instance == null) return;
    Instance._submitFailure = message ?? string.Empty;
    Instance.Refresh();
}
```

In `BindSummary`, show `_submitFailure` in red before the normal ready/disabled message. Clear it in `BindPresentation`, `OnTermChanged`, and after an accepted or pending submit. Do not close the negotiation window after rejection.

In the controller rejection branch, compute one message and send it to both surfaces:

```csharp
string key = DiplomacyFailureReasonRules.StableKey(
    result?.MessageKey ?? "execution_failed");
string message = DiplomacyConversationWindow.ProposalFailure(key);
WarPeaceNegotiationWindow.ShowSubmitFailure(message);
WorldTip.showNow(message, false, "top");
```

For unknown safe keys, `ProposalFailure` must return the localized generic explanation plus ` (reason_key)` so diagnostics remain visible.

- [ ] **Step 4: Run tests and UI source guard**

The source guard must require `ShowSubmitFailure` in the controller rejection path, `_submitFailure` precedence in `BindSummary`, and clearing on selection change.

```powershell
dotnet run --project Tests/DiplomacyFailureReasonRulesTests.csproj -c Debug
pwsh -NoProfile -File Tests/WarPeaceFailureStatusSourceGuard.ps1
```

Expected: both commands exit 0.

- [ ] **Step 5: Commit failure propagation**

```powershell
git add Code/core/lineage/DiplomacyFailureReasonRules.cs Code/core/multiplayer/commands/AW3DiplomacyCommandHandler.cs Code/ui/windows/WarPeaceNegotiationController.cs Code/ui/windows/WarPeaceNegotiationWindow.cs Code/ui/windows/DiplomacyConversationWindow.cs Tests/DiplomacyFailureReasonRulesTests.cs Tests/DiplomacyFailureReasonRulesTests.csproj Tests/WarPeaceFailureStatusSourceGuard.ps1
git commit -m "fix: keep peace submission failures visible"
```

### Task 5: Open Actors From Household Rows

**Files:**
- Create: `Code/core/lineage/RulerHouseholdNavigationRules.cs`
- Modify: `Code/ui/windows/RulerHouseholdWindow.cs`
- Create: `Tests/RulerHouseholdNavigationRulesTests.cs`
- Create: `Tests/RulerHouseholdNavigationRulesTests.csproj`
- Create: `Tests/RulerHouseholdNavigationSourceGuard.ps1`

- [ ] **Step 1: Write failing row-eligibility tests**

```csharp
True(RulerHouseholdNavigationRules.CanOpen(
    rowPresent: true, markedAlive: true, actorResolved: true,
    actorAlive: true, actorRekt: false));
False(RulerHouseholdNavigationRules.CanOpen(
    false, true, true, true, false));
False(RulerHouseholdNavigationRules.CanOpen(
    true, false, true, false, false));
False(RulerHouseholdNavigationRules.CanOpen(
    true, true, false, false, false));
False(RulerHouseholdNavigationRules.CanOpen(
    true, true, true, true, true));
```

Run the new project. Expected: RED because the rule is absent.

- [ ] **Step 2: Implement the pure rule and row button**

Add `Button Button` to `HouseholdRowView` and create the row with an `Image` plus `Button`. In every `BindRow`, call `pView.Button.onClick.RemoveAllListeners()` first. Resolve the actor only when clicked:

```csharp
long actorId = pRow.ActorId;
pView.Button.interactable = pRow.Alive;
pView.Button.onClick.AddListener(() =>
{
    Actor actor = null;
    try { actor = World.world?.units?.get(actorId); }
    catch { }
    if (RulerHouseholdNavigationRules.CanOpen(
            rowPresent: true, markedAlive: pRow.Alive,
            actorResolved: actor?.data != null,
            actorAlive: actor?.isAlive() == true,
            actorRekt: actor?.isRekt() != false))
        ActionLibrary.openUnitWindow(actor);
});
```

Empty and unavailable rows set `interactable = false`. Use the existing background image as the button target and preserve the current layout.

- [ ] **Step 3: Run rules and source guard**

Require `RemoveAllListeners`, late `World.world?.units?.get(actorId)`, and `ActionLibrary.openUnitWindow(actor)`.

```powershell
dotnet run --project Tests/RulerHouseholdNavigationRulesTests.csproj -c Debug
pwsh -NoProfile -File Tests/RulerHouseholdNavigationSourceGuard.ps1
```

Expected: both commands exit 0.

- [ ] **Step 4: Commit household navigation**

```powershell
git add Code/core/lineage/RulerHouseholdNavigationRules.cs Code/ui/windows/RulerHouseholdWindow.cs Tests/RulerHouseholdNavigationRulesTests.cs Tests/RulerHouseholdNavigationRulesTests.csproj Tests/RulerHouseholdNavigationSourceGuard.ps1
git commit -m "feat: open actors from household rows"
```

### Task 6: Add Persisted Lover-Conception Requests For Titled Male Lines

**Files:**
- Create: `Code/core/lineage/DynasticLoverConceptionRules.cs`
- Modify: `Code/core/lineage/LineageKeys.cs`
- Modify: `Code/core/lineage/NobleHeirPregnancyService.cs`
- Modify: `Code/patch/AW_LoversPatch.cs`
- Modify: `Code/patch/AW_DynasticReproductionPatch.cs`
- Modify: `Code/patch/AW_NobleHeirPregnancyPatch.cs`
- Modify: `Tests/NobleHeirPregnancyRulesTests.cs`
- Create: `Tests/DynasticLoverConceptionRulesTests.cs`
- Create: `Tests/DynasticLoverConceptionRulesTests.csproj`
- Create: `Tests/DynasticLoverConceptionSourceGuard.ps1`

- [ ] **Step 1: Write failing pure-rule tests**

Define and test these APIs:

```csharp
True(DynasticLoverConceptionRules.IsInScope(
    holdsTitle: true, paternalDistance: 0, actorIsMale: false));
True(DynasticLoverConceptionRules.IsInScope(
    holdsTitle: false, paternalDistance: 1, actorIsMale: true));
True(DynasticLoverConceptionRules.IsInScope(
    false, paternalDistance: 3, actorIsMale: true));
False(DynasticLoverConceptionRules.IsInScope(
    false, paternalDistance: 4, actorIsMale: true));
False(DynasticLoverConceptionRules.IsInScope(
    false, paternalDistance: 2, actorIsMale: false));
True(DynasticLoverConceptionRules.RollMakesMale(69));
False(DynasticLoverConceptionRules.RollMakesMale(70));
True(DynasticLoverConceptionRules.ShouldContinueAfterBirth(
    managedRequest: true, childIsMale: false));
False(DynasticLoverConceptionRules.ShouldContinueAfterBirth(
    managedRequest: true, childIsMale: true));
False(DynasticLoverConceptionRules.ShouldContinueAfterBirth(
    managedRequest: false, childIsMale: false));
```

Add disposition cases proving: ready adults start; an existing pregnancy waits; a dead/broken relationship cancels; an out-of-age mother cancels; a personal offspring cap does not block; a world-law or meta-population block waits.

Run:

```powershell
dotnet run --project Tests/DynasticLoverConceptionRulesTests.csproj -c Debug
```

Expected: build failure because the rule file does not exist.

- [ ] **Step 2: Implement the pure lifecycle rules**

Use `MalePercent = 70`, paternal distance `0..3`, and a `Cancel/Wait/Start` disposition. `Start` requires authority, mutual living lovers, a living adult mother in breeding age, a living adult father, no current pregnancy, fertility, nutrition, safe city, meta-population room, and enabled baby world law. The personal offspring cap is intentionally absent from this decision.

- [ ] **Step 3: Persist and enqueue one request per new lover**

Add ActorData keys:

```csharp
DYNASTIC_LOVER_HEIR_PENDING
DYNASTIC_LOVER_HEIR_ACTIVE
DYNASTIC_LOVER_HEIR_FATHER_ID
DYNASTIC_LOVER_HEIR_RELATION_TOKEN
DYNASTIC_LOVER_HEIR_LAST_RELATION_TOKEN
DYNASTIC_LOVER_HEIR_ATTEMPTS
```

Add `NobleHeirPregnancyService.OnBecameLovers(Actor, Actor)`. Resolve the female mother and male father, require either partner to be a current title holder or a father-line son/grandson/great-grandson of one, and deduplicate by a persisted relation token. Generate the token once from the sorted pair IDs plus the relationship-start world time; if a pending or active request already names the same pair, reuse its token instead of creating a second request. Keep the completed token until the relationship is no longer mutual, so duplicate callbacks cannot restart a finished request; a later genuinely rebuilt relationship receives a new token. A title holder is a current king, active feudatory prince, or actor with an active `NobleRankService.ReadHot` title. Follow at most three live father links; do not scan all actors or query SQLite.

Call the service from `AW_LoversPatch` after `ChronicleEvents.OnBecameLovers`. Persist the request on the mother and enqueue her in the existing bounded pregnancy queue. `OnActorLoaded` restores pending requests. Replica sessions may read but cannot create or advance requests.

- [ ] **Step 4: Start ten-month pregnancies and isolate the 70/30 roll**

When a pending lover request reaches `Start`, call `BabyHelper.babyMakingStart`, add the existing 50-second ten-month pregnancy status, record the father, set `DYNASTIC_LOVER_HEIR_ACTIVE`, clear pending, and increment attempts.

Replace the current broad dynastic sex override in `AW_DynasticReproductionPatch.MakeBaby_Prefix` with:

```csharp
if (pForcedSexType == ActorSex.None &&
    NobleHeirPregnancyService.IsActiveLoverHeirBirth(
        pParent1, pParent2))
    pForcedSexType = DynasticLoverConceptionRules.RollMakesMale(
        Randy.randomInt(0, 100))
        ? ActorSex.Male
        : ActorSex.Female;
```

This is the only 70/30 path. An ordinary birth, including an ordinary king or heir birth, leaves `pForcedSexType == ActorSex.None` and therefore remains vanilla 50/50.

In `MakeBaby_Postfix`, pass the finalized child to `OnLoverHeirChildBorn`. A male child clears active, pending, father, relation token, and attempts while retaining the completed token for callback deduplication. A female child clears active, restores pending for the same mutual lover and relation token, and re-enqueues the mother for another ten-month pregnancy. If the relationship, actors, or breeding-age condition becomes permanently invalid, clear the request and completed-token guard so a later relationship can be registered independently.

- [ ] **Step 5: Run pregnancy rules and source guards**

Update `NobleHeirPregnancyRulesTests` so the old generic 70/30 assertion is removed and the source guard requires `IsActiveLoverHeirBirth`. Require persisted keys, the lovers hook, bounded queue reuse, actor-load restoration, no `World.world.units` scan, and replica gates.

```powershell
dotnet run --project Tests/DynasticLoverConceptionRulesTests.csproj -c Debug
dotnet run --project Tests/NobleHeirPregnancyRulesTests.csproj -c Debug
pwsh -NoProfile -File Tests/DynasticLoverConceptionSourceGuard.ps1
```

Expected: all commands exit 0; female births retain the request and male births clear it.

- [ ] **Step 6: Commit the lover-conception lifecycle**

```powershell
git add Code/core/lineage/DynasticLoverConceptionRules.cs Code/core/lineage/LineageKeys.cs Code/core/lineage/NobleHeirPregnancyService.cs Code/patch/AW_LoversPatch.cs Code/patch/AW_DynasticReproductionPatch.cs Code/patch/AW_NobleHeirPregnancyPatch.cs Tests/NobleHeirPregnancyRulesTests.cs Tests/DynasticLoverConceptionRulesTests.cs Tests/DynasticLoverConceptionRulesTests.csproj Tests/DynasticLoverConceptionSourceGuard.ps1
git commit -m "feat: add titled-line lover conception requests"
```

### Task 7: Regression Verification, Build, Deployment, And In-Game Acceptance

**Files:**
- Verify: all files from Tasks 1-6
- Deploy: repository-configured WorldBox `Mods/AncientWarfare3.0` target

- [ ] **Step 1: Run every focused test and guard**

```powershell
dotnet run --project Tests/DiplomacyFailureReasonRulesTests.csproj -c Release
dotnet run --project Tests/RulerHouseholdNavigationRulesTests.csproj -c Release
dotnet run --project Tests/DynasticLoverConceptionRulesTests.csproj -c Release
dotnet run --project Tests/NobleHeirPregnancyRulesTests.csproj -c Release
dotnet run --project Tests/WarPeaceSettlementServiceTests.csproj -c Release
pwsh -NoProfile -File Tests/WarPeaceSubjectTransferSourceGuard.ps1
pwsh -NoProfile -File Tests/WarPeaceFailureStatusSourceGuard.ps1
pwsh -NoProfile -File Tests/RulerHouseholdNavigationSourceGuard.ps1
pwsh -NoProfile -File Tests/DynasticLoverConceptionSourceGuard.ps1
```

Expected: every command exits 0.

- [ ] **Step 2: Run the broad suite and compile both configurations**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
dotnet build AncientWarfare3.csproj -c Debug --nologo
dotnet build AncientWarfare3.csproj -c Release --nologo
```

Expected: rule suite passes and both builds finish with 0 errors. Record warnings separately against the pre-change baseline.

- [ ] **Step 3: Inspect only the scoped diff**

Run `git diff --check` over the files listed in Tasks 1-6 and inspect `git diff --stat` for those exact paths. Do not stage, delete, or revert unrelated dirty-worktree files.

- [ ] **Step 4: Deploy after WorldBox is closed**

Use the repository's established deployment command. Confirm the deployed source contains `CanAiForceVassal`, `ShowSubmitFailure`, `RulerHouseholdNavigationRules`, and `DYNASTIC_LOVER_HEIR_PENDING` before launching the game.

- [ ] **Step 5: Perform the in-game acceptance matrix**

Verify all of the following in a new world and one existing save:

1. A vassal and root suzerain stay one map color before, during, and after war; the tooltip still lists war state.
2. AI marquis-versus-baron cannot choose forced vassalage; AI duke-versus-baron can; player manual eligibility is unchanged.
3. Peace can transfer an enemy subject from a third-party suzerain; a forced rollback restores the old suzerain and tier; tributary transfer remains unavailable.
4. A rejected peace offer leaves the localized reason in red inside the open window and also shows the short tip.
5. Clicking a living principal wife or consort opens the correct actor; empty and stale rows do nothing.
6. A title holder and father-line son, grandson, and great-grandson each trigger on a new female lover; a daughter-line descendant and father-line great-great-grandson do not.
7. The lover task waits behind an existing pregnancy, uses ten months, rolls male/female at 70/30, immediately queues another ten-month pregnancy after a daughter, and clears after a son.
8. Save and reload during pending and active states; each request resumes once without duplication. Ordinary births remain 50/50 and do not enter the retry loop.

- [ ] **Step 6: Report evidence and commit any test-only correction**

Report exact test commands, build results, deployed path, save used, and observed state transitions. Stage only paths named in this plan.

---

## Self-Review

- Spec coverage: Tasks 1-6 map directly to all six approved design sections; Task 7 covers focused tests, broad regression, builds, deployment, new-save behavior, and save/load recovery.
- Isolation: AI-only title gating does not affect player candidates; tributary transfer remains excluded; household navigation does not retain Actor objects; lover sex bias is scoped to persisted lover requests.
- Persistence: subject rollback captures suzerain, relation type, and tier; lover requests persist father, state, attempt count, and last partner, and rebuild through the existing actor-load hook.
- Performance: no new whole-world actor scan, no SQLite query in pregnancy cycles, bounded existing authority queue reuse, and no per-frame work.
- Scope exclusions: no same-sex or underage pregnancy, no high-age fertility bypass, no instant birth, no ordinary-birth 70/30 override, no forced tributary transfer, and no player-facing vassal-war rank restriction.

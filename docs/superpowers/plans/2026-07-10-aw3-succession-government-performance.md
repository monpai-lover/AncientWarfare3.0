# AW3 Succession, Government, and Maintenance Performance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make AW-lineage succession survive the original game's temporary king vacancy, establish deterministic republican elections, gate ordinary Mandate claims behind Mandate Rites, block vanilla mass fragmentation, and remove the two reported maintenance spikes and clan-tree locate regression.

**Architecture:** Put every timing, eligibility, ranking, batching, and fallback decision in small public pure-rule classes that the existing console test projects can execute without a live WorldBox world. Keep Unity/WorldBox mutation in `HeirService`, `RepublicGovernmentService`, `RoyalGuardService`, `SlaveService`, Harmony patches, and the family-tree window. Treat `timer_new_king` as an explicit transition state shared by succession, republic, rebellion, fragmentation, and guard maintenance.

**Tech Stack:** C# 11, .NET Framework 4.8, Harmony, NeoModLoader, SQLite lineage archive, existing executable rule-test projects.

---

## File Map

- Create `Code/core/lineage/SuccessionTransitionRules.cs`: pure transition, reference-ID, office eligibility, and vanilla-fragmentation rules.
- Create `Code/core/policy/KingdomPolicyInheritanceRules.cs`: sanitize non-transferable government state in split kingdoms.
- Create `Tests/SuccessionGovernmentRuleTests/SuccessionGovernmentRuleTests.csproj`: focused executable regression suite.
- Create `Tests/SuccessionGovernmentRuleTests/Program.cs`: old-king vacancy, dead crown-prince branch, role eligibility, republic ranking, policy inheritance, and clan locate tests.
- Modify `Code/core/lineage/HeirService.cs`: split read-only lookup from mutation and select by stored old-king ID.
- Modify `Code/core/lineage/RepublicGovernmentRules.cs`: deterministic election eligibility/ranking and republic transition rules.
- Modify `Code/core/lineage/RepublicGovernmentService.cs`: elect rank 1/rank 2, preserve republic through succession, and avoid empty republics.
- Modify `Code/core/lineage/LineageKeys.cs`: add republican succession mode and slave-fill cursor/continuation keys.
- Modify `Code/patch/AW_HeirPatch.cs`: route republic succession through the registered successor and remove random/leader fallback.
- Modify `Code/patch/AW_RepublicGovernmentPatch.cs`: do not clear republic when its registered successor becomes king.
- Modify `Code/patch/AW_MandateSuccessionPatch.cs`: protect all managed succession kingdoms from vanilla chaos, while restoring `shattered_crown`.
- Modify `Code/patch/AW_CityLeaderPatch.cs`: use read-only successor lookup.
- Modify `Code/core/lineage/GeneralRebellionService.cs`: suppress missing-heir instability during transition.
- Modify `Code/core/lineage/MandateDeclarationRules.cs`: pure ordinary policy gate.
- Modify `Code/core/lineage/MandateService.cs`: enforce Mandate Rites on ordinary/automatic claims.
- Modify `Code/content/policies/KingdomPolicyDefs.cs`: move and cheapen Mandate Rites.
- Modify `Code/core/policy/KingdomPolicyAI.cs`: research Mandate Rites immediately after Ancestral Rites.
- Modify `Code/core/policy/KingdomPolicyService.cs`: remove historical-figure requirement bypass.
- Modify `Code/core/policy/KingdomPolicyInheritanceService.cs`: sanitize inherited republic state.
- Modify `Code/core/lineage/RoyalGuardMaintenanceRules.cs`: transition preservation and incremental-dismiss rules.
- Modify `Code/core/lineage/RoyalGuardService.cs`: preserve/transfer guards and batch true dissolution.
- Modify `Code/core/lineage/SlaveArmyMaintenanceRules.cs`: capacity-before-promotion, batch, cursor, and continuation rules.
- Modify `Code/core/lineage/SlaveService.cs`: bounded fill pipeline with reused composition counts.
- Modify `Code/core/policy/CityMaintenanceBenchmarkRules.cs`: scan/promotion/attach profiler labels.
- Modify `Code/core/lineage/FamilyTreeRelationRules.cs`: strict agnatic edges/path and locate fallback rules.
- Modify `Code/core/lineage/LineageQuery.cs`: expose strict father-only path/root helpers.
- Modify `Code/ui/windows/FamilyTreeWindow.cs`: stay in big-tree mode, use father-only children, and restore tools.
- Modify `Tests/MandateRulerTitleRuleTests/Program.cs`: Mandate policy and node-layout regressions.
- Modify `Tests/WarFabricationRuleTests/Program.cs`: update ordinary declaration origin tests for the new policy argument.
- Modify `Tests/RoyalGuardActionRuleTests/Program.cs`: guard preservation/dismiss batching regressions.
- Modify `Tests/CityMaintenanceRuleTests/Program.cs`: slave-fill ordering/batching/cursor regressions.

### Task 1: Add the succession transition and exact family regression rules

**Files:**
- Create: `Tests/SuccessionGovernmentRuleTests/SuccessionGovernmentRuleTests.csproj`
- Create: `Tests/SuccessionGovernmentRuleTests/Program.cs`
- Create: `Code/core/lineage/SuccessionTransitionRules.cs`

- [ ] **Step 1: Create the test project and write failing transition/role/family tests**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net48</TargetFramework>
    <LangVersion>11</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\AncientWarfare3.csproj" />
  </ItemGroup>
</Project>
```

`Program.cs` starts with these assertions:

```csharp
long reference = SuccessionTransitionRules.ResolveReferenceKingId(
    pCurrentKingId: -1, pCurrentKingValid: false, pPreviousKingId: 100);
Expect(reference == 100, "Temporary vacancies must retain the dead king ID as succession reference.");

int survivingBrotherTier = HeirGenerationRules.ClassifyTier(
    pIsAgnaticDescendantOfKing: true, pGenerationDelta: 1);
Expect(survivingBrotherTier == HeirGenerationRules.TierDirectDescendant,
    "After the crown prince and his son die, another living son remains a direct heir.");

Expect(SuccessionTransitionRules.IsOfficialRoleEligible(
        pIsKing: false, pIsCityLeader: true, pIsGeneral: true,
        pIsArmyCaptain: true, pHasFief: true),
    "City leaders, generals, captains, and fief holders must remain succession eligible.");
Expect(!SuccessionTransitionRules.IsOfficialRoleEligible(
        pIsKing: true, pIsCityLeader: false, pIsGeneral: false,
        pIsArmyCaptain: false, pHasFief: false),
    "Only an actor already serving as king is excluded by office.");

Expect(!SuccessionTransitionRules.ShouldTreatMissingHeirAsUnstable(
        pSuccessionPending: true, pHasHeir: false),
    "The original timer_new_king vacancy must not become a succession crisis.");
Expect(SuccessionTransitionRules.ShouldBlockVanillaMassFragmentation(
        pUsesManagedLineage: true),
    "Managed lineage kingdoms must block vanilla all-city fragmentation.");
```

- [ ] **Step 2: Run the new project and verify RED**

Run: `dotnet run --project Tests/SuccessionGovernmentRuleTests/SuccessionGovernmentRuleTests.csproj`

Expected: compilation fails because `SuccessionTransitionRules` does not exist.

- [ ] **Step 3: Add the minimal pure rule implementation**

```csharp
namespace AncientWarfare3.core.lineage
{
    public static class SuccessionTransitionRules
    {
        public static bool IsPending(float pTimerNewKing) => pTimerNewKing > 0f;

        public static long ResolveReferenceKingId(long pCurrentKingId, bool pCurrentKingValid,
            long pPreviousKingId)
        {
            if (pCurrentKingValid && pCurrentKingId >= 0) return pCurrentKingId;
            return pPreviousKingId >= 0 ? pPreviousKingId : -1L;
        }

        public static bool IsOfficialRoleEligible(bool pIsKing, bool pIsCityLeader,
            bool pIsGeneral, bool pIsArmyCaptain, bool pHasFief)
        {
            return !pIsKing;
        }

        public static bool ShouldTreatMissingHeirAsUnstable(bool pSuccessionPending, bool pHasHeir)
        {
            return !pSuccessionPending && !pHasHeir;
        }

        public static bool ShouldBlockVanillaMassFragmentation(bool pUsesManagedLineage)
        {
            return pUsesManagedLineage;
        }
    }
}
```

- [ ] **Step 4: Run the new project and verify GREEN**

Run: `dotnet run --project Tests/SuccessionGovernmentRuleTests/SuccessionGovernmentRuleTests.csproj`

Expected: `Succession/government rule tests passed.`

- [ ] **Step 5: Commit the rule seam**

```powershell
git add Code/core/lineage/SuccessionTransitionRules.cs Tests/SuccessionGovernmentRuleTests
git commit -m "test: 覆盖王位交接与官职继承规则"
```

### Task 2: Make monarchy heir lookup vacancy-safe and side-effect-free

**Files:**
- Modify: `Code/core/lineage/HeirService.cs`
- Modify: `Code/patch/AW_CityLeaderPatch.cs`
- Modify: `Code/core/lineage/GeneralRebellionService.cs`
- Modify: `Code/patch/AW_MandateSuccessionPatch.cs`
- Modify: `Tests/SuccessionGovernmentRuleTests/Program.cs`

- [ ] **Step 1: Extend the failing regression with transition decisions**

```csharp
Expect(SuccessionTransitionRules.ShouldUseCachedHeir(
        pSuccessionPending: true, pCachedHeirEligible: true),
    "A prepared heir must survive timer_new_king.");
Expect(!SuccessionTransitionRules.ShouldOverwriteCachedHeir(
        pSuccessionPending: true, pHasReferenceKing: true),
    "Read-only vacancy lookup must not overwrite aw_heir_id.");
```

- [ ] **Step 2: Run the focused project and verify RED**

Run: `dotnet run --project Tests/SuccessionGovernmentRuleTests/SuccessionGovernmentRuleTests.csproj`

Expected: compilation fails for `ShouldUseCachedHeir` and `ShouldOverwriteCachedHeir`.

- [ ] **Step 3: Add the rules and refactor `HeirService` around an ID reference**

Add:

```csharp
public static bool ShouldUseCachedHeir(bool pSuccessionPending, bool pCachedHeirEligible)
    => pSuccessionPending && pCachedHeirEligible;

public static bool ShouldOverwriteCachedHeir(bool pSuccessionPending, bool pHasReferenceKing)
    => !pSuccessionPending && pHasReferenceKing;
```

In `HeirService` introduce these public entry points and keep mutation only in refresh:

```csharp
public static Actor PeekRegisteredHeir(Kingdom pKingdom)
{
    if (pKingdom?.data == null) return null;
    pKingdom.data.get(LineageKeys.KINGDOM_HEIR_ID, out long heirId, -1L);
    Actor heir = heirId >= 0 ? World.world?.units?.get(heirId) : null;
    return IsRegisteredCandidateEligible(heir, pKingdom) ? heir : null;
}

public static Actor GetHeir(Kingdom pKingdom)
{
    if (pKingdom?.data == null) return null;
    Actor cached = PeekRegisteredHeir(pKingdom);
    bool pending = SuccessionTransitionRules.IsPending(pKingdom.data.timer_new_king);
    if (SuccessionTransitionRules.ShouldUseCachedHeir(pending, cached?.data != null)) return cached;
    return RefreshHeirAndReturn(pKingdom);
}
```

Resolve the search ID with current king then `KINGDOM_PRE_SUCCESSION_KING_ID`; pass the ID into `FindHeir`. Replace `king.data.id` and required live-king checks with that ID. Keep `Actor knownKing` only for direct-child fallback and legitimate-line initialization. Remove the duplicate `ClearOldHeirFlag()` call in `RefreshHeir()`.

In `IsHeirBaseEligible`, call `SuccessionTransitionRules.IsOfficialRoleEligible()` and do not inspect leader/general/captain/fief status.

In `AW_CityLeaderPatch.GetHeirId`, replace `HeirService.GetHeir()` with `HeirService.PeekRegisteredHeir()`.

In `GeneralRebellionService`, calculate:

```csharp
bool pending = SuccessionTransitionRules.IsPending(pKingdom.data.timer_new_king);
bool hasHeir = HeirService.PeekRegisteredHeir(pKingdom)?.data != null;
bool successionUnstable = SuccessionTransitionRules.ShouldTreatMissingHeirAsUnstable(pending, hasHeir);
```

Extend the `KingdomBehCheckKing.execute` prefix to remember a dead king for every kingdom using `LineageService.IsXiaKingdom()` or `XiaizationService.UsesXiaizedInstitutionSystem()`.

- [ ] **Step 4: Run focused tests and verify GREEN**

Run: `dotnet run --project Tests/SuccessionGovernmentRuleTests/SuccessionGovernmentRuleTests.csproj --no-restore`

Run: `dotnet run --project Tests/GeneralRebellionRuleTests/GeneralRebellionRuleTests.csproj --no-restore`

Expected: the focused project and the existing rebellion project both print their pass message.

- [ ] **Step 5: Build to catch WorldBox API mismatches**

Run: `dotnet build AncientWarfare3.csproj --no-restore`

Expected: `0 Warning(s), 0 Error(s)`.

- [ ] **Step 6: Commit the vacancy-safe monarchy succession**

```powershell
git add Code/core/lineage/HeirService.cs Code/patch/AW_CityLeaderPatch.cs Code/core/lineage/GeneralRebellionService.cs Code/patch/AW_MandateSuccessionPatch.cs Code/core/lineage/SuccessionTransitionRules.cs Tests/SuccessionGovernmentRuleTests
git commit -m "fix: 保留交接期父系继承人"
```

### Task 3: Replace random republic leadership with ranked leader and successor

**Files:**
- Modify: `Code/core/lineage/RepublicGovernmentRules.cs`
- Modify: `Code/core/lineage/RepublicGovernmentService.cs`
- Modify: `Code/core/lineage/LineageKeys.cs`
- Modify: `Code/core/lineage/HeirService.cs`
- Modify: `Code/patch/AW_HeirPatch.cs`
- Modify: `Code/patch/AW_RepublicGovernmentPatch.cs`
- Modify: `Tests/SuccessionGovernmentRuleTests/Program.cs`

- [ ] **Step 1: Write failing election ranking and state-transition tests**

```csharp
var strongest = new RepublicCandidateScore(11, diplomacy: 8, warfare: 7, stewardship: 6,
    level: 4, combatStrength: 20f, age: 30);
var weaker = new RepublicCandidateScore(12, diplomacy: 6, warfare: 6, stewardship: 6,
    level: 9, combatStrength: 90f, age: 50);
Expect(RepublicGovernmentRules.CompareCandidates(strongest, weaker) < 0,
    "The diplomacy+warfare+stewardship sum must be the primary election score.");

var tieLowId = new RepublicCandidateScore(20, 6, 6, 6, 3, 20f, 30);
var tieHighId = new RepublicCandidateScore(21, 6, 6, 6, 3, 20f, 30);
Expect(RepublicGovernmentRules.CompareCandidates(tieLowId, tieHighId) < 0,
    "Actor ID must make exact election ties deterministic.");

Expect(RepublicGovernmentRules.ShouldEnterRepublic(
        pSuccessionPending: false, pHasMonarchyHeir: false, pElectableCount: 2),
    "True extinction with electable people must create a republic.");
Expect(!RepublicGovernmentRules.ShouldEnterRepublic(
        pSuccessionPending: true, pHasMonarchyHeir: false, pElectableCount: 2),
    "Temporary vacancy must not create a republic.");
Expect(RepublicGovernmentRules.ShouldPreserveRepublicOnSetKing(
        pWasRepublic: true, pWasRegisteredRepublicSuccessor: true,
        pActorMarkedRepublicLeader: false),
    "The registered republican successor must not clear republic state.");
```

- [ ] **Step 2: Run and verify RED**

Run: `dotnet run --project Tests/SuccessionGovernmentRuleTests/SuccessionGovernmentRuleTests.csproj --no-restore`

Expected: compilation fails because `RepublicCandidateScore` and the new rules do not exist.

- [ ] **Step 3: Implement deterministic pure ranking**

Add a public `RepublicCandidateScore` struct with actor ID, three attributes, level, combat strength, and age. Implement `CompareCandidates` in this exact order: attribute sum descending, level descending, combat descending, age descending, ID ascending. Replace `IsEligibleCommonerLeader` with eligibility that does not exclude nobles or office holders and only excludes invalid kingdom, sex, adulthood, life, slave, and king status.

Add:

```csharp
public static bool ShouldEnterRepublic(bool pSuccessionPending, bool pHasMonarchyHeir,
    int pElectableCount)
    => !pSuccessionPending && !pHasMonarchyHeir && pElectableCount > 0;

public static bool ShouldPreserveRepublicOnSetKing(bool pWasRepublic,
    bool pWasRegisteredRepublicSuccessor, bool pActorMarkedRepublicLeader)
    => pWasRepublic && (pWasRegisteredRepublicSuccessor || pActorMarkedRepublicLeader);
```

- [ ] **Step 4: Implement leader/successor election service**

Replace reservoir sampling with a full eligible-candidate list sorted using `CompareCandidates`. Add:

```csharp
public static Actor GetRegisteredSuccessor(Kingdom pKingdom);
public static Actor ElectInitialLeader(Kingdom pKingdom);
public static void RefreshRepublicSuccessor(Kingdom pKingdom, Actor pCurrentLeader);
public static bool IsRegisteredRepublicSuccessor(Kingdom pKingdom, Actor pActor);
```

`ElectInitialLeader` ranks first, returns null without changing government if there is no candidate, then sets `ClassRepublic`, marks rank 1 `REPUBLIC_LEADER`, and stores rank 2 in `KINGDOM_HEIR_ID` with `SuccessionMode.REPUBLIC_ELECTIVE`. `RefreshRepublicSuccessor` excludes the current leader and writes the new rank 1 remaining candidate.

`HeirService.GetHeir()` returns `GetRegisteredSuccessor()` for a republic. `RefreshHeir()` delegates to `RefreshRepublicSuccessor()` for a republic.

In `AW_HeirPatch.GetKingFromLeaders_Prefix`, remove city-leader monarchy fallback. Return the registered republican successor or `ElectInitialLeader()`. In `AW_RepublicGovernmentPatch.SetKing_Postfix`, inspect the still-registered heir before the lower-priority heir postfix refreshes it; preserve republic for either a marked leader or registered republican successor, mark the new leader, and refresh the next successor.

- [ ] **Step 5: Run focused tests and build**

Run: `dotnet run --project Tests/SuccessionGovernmentRuleTests/SuccessionGovernmentRuleTests.csproj --no-restore`

Run: `dotnet build AncientWarfare3.csproj --no-restore`

Expected: test pass message and build with zero errors.

- [ ] **Step 6: Commit republican election**

```powershell
git add Code/core/lineage/RepublicGovernmentRules.cs Code/core/lineage/RepublicGovernmentService.cs Code/core/lineage/LineageKeys.cs Code/core/lineage/HeirService.cs Code/patch/AW_HeirPatch.cs Code/patch/AW_RepublicGovernmentPatch.cs Tests/SuccessionGovernmentRuleTests
git commit -m "fix: 按能力推举共和国首领与继任者"
```

### Task 4: Block vanilla mass fragmentation and republic snapshot inheritance

**Files:**
- Modify: `Code/patch/AW_MandateSuccessionPatch.cs`
- Create: `Code/core/policy/KingdomPolicyInheritanceRules.cs`
- Modify: `Code/core/policy/KingdomPolicyInheritanceService.cs`
- Modify: `Tests/SuccessionGovernmentRuleTests/Program.cs`

- [ ] **Step 1: Write failing fragmentation and policy-inheritance tests**

```csharp
Expect(SuccessionTransitionRules.ShouldBlockVanillaMassFragmentation(true),
    "AW lineage kingdoms must retain their cities when the crown is vacant.");
Expect(!SuccessionTransitionRules.ShouldBlockShatteredCrownEvent(
        pUsesManagedLineage: true),
    "The explicit shattered_crown culture event must remain available.");

Expect(KingdomPolicyInheritanceRules.SanitizeClassStateForNewKingdom("republic", "default") == "default",
    "Split kingdoms must not inherit republic government wholesale.");
Expect(KingdomPolicyInheritanceRules.SanitizeClassStateForNewKingdom("aristocrat", "default") == "aristocrat",
    "Transferable class states must remain unchanged.");
```

- [ ] **Step 2: Run and verify RED**

Run: `dotnet run --project Tests/SuccessionGovernmentRuleTests/SuccessionGovernmentRuleTests.csproj --no-restore`

Expected: compilation fails for the shattered-crown and inheritance rules.

- [ ] **Step 3: Implement and wire the rules**

Add `ShouldBlockShatteredCrownEvent()` returning `false`. Change `CheckKingdomChaos_Prefix` to identify managed lineage via Xia kingdom or Xiaized institution system, and return `false` whenever `ShouldBlockVanillaMassFragmentation()` is true. Remove the `checkShatteredCrownEvent` prefix entirely.

Implement:

```csharp
public static string SanitizeClassStateForNewKingdom(string pSourceClass, string pDefaultClass)
{
    return pSourceClass == KingdomPolicyDefs.ClassRepublic ? pDefaultClass : pSourceClass;
}
```

Use it for `dst.class_state` in `KingdomPolicyInheritanceService`.

- [ ] **Step 4: Run test and build**

Run: `dotnet run --project Tests/SuccessionGovernmentRuleTests/SuccessionGovernmentRuleTests.csproj --no-restore`

Run: `dotnet build AncientWarfare3.csproj --no-restore`

Expected: both succeed.

- [ ] **Step 5: Commit fragmentation/state inheritance fix**

```powershell
git add Code/patch/AW_MandateSuccessionPatch.cs Code/core/policy/KingdomPolicyInheritanceRules.cs Code/core/policy/KingdomPolicyInheritanceService.cs Tests/SuccessionGovernmentRuleTests
git commit -m "fix: 阻止无王时原版全国分裂"
```

### Task 5: Require and advance Mandate Rites

**Files:**
- Modify: `Code/core/lineage/MandateDeclarationRules.cs`
- Modify: `Code/core/lineage/MandateService.cs`
- Modify: `Code/content/policies/KingdomPolicyDefs.cs`
- Modify: `Code/core/policy/KingdomPolicyAI.cs`
- Modify: `Code/core/policy/KingdomPolicyService.cs`
- Modify: `Tests/MandateRulerTitleRuleTests/Program.cs`
- Modify: `Tests/WarFabricationRuleTests/Program.cs`

- [ ] **Step 1: Write failing policy-gate and node-definition tests**

Add `using System.Linq;` and `using AncientWarfare3.content.policies;` to the test file, then add:

```csharp
if (MandateDeclarationRules.CanStartOrdinaryDeclaration(
        pMandateAlreadyExists: false, pMandateRitesCompleted: false, out string missingReason) ||
    missingReason != "requires_mandate_rites")
    throw new Exception("Ordinary Mandate claims must require Mandate Rites.");

if (!MandateDeclarationRules.CanStartOrdinaryDeclaration(
        pMandateAlreadyExists: false, pMandateRitesCompleted: true, out _))
    throw new Exception("Completed Mandate Rites should pass the ordinary policy gate.");

if (MandateDeclarationRules.RequiresMandateRitesForOrigin(
        pDeclarationReason: "tianming_war", pOriginType: "", pClaimantKind: ""))
    throw new Exception("A successful Mandate war must remain a policy-gate exception.");
if (!MandateDeclarationRules.RequiresMandateRitesForOrigin(
        pDeclarationReason: "auto", pOriginType: "", pClaimantKind: ""))
    throw new Exception("Automatic ordinary claims must require Mandate Rites.");

KingdomPolicyDef rites = KingdomPolicyDefs.SocialPolicies.FirstOrDefault(
    p => p.Id == "aw_policy_mandate_rites");
if (rites == null || rites.Column != 4 || rites.Row != 2 || rites.Cost != 90f)
    throw new Exception("Mandate Rites must move earlier and cost 90.");
if (rites.RequiredPolicies.Length != 1 || rites.RequiredPolicies[0] != "aw_policy_ancestral_rites" ||
    rites.RequiredTechs.Length != 1 || rites.RequiredTechs[0] != "aw_tech_rites_music")
    throw new Exception("Mandate Rites dependencies must be Ancestral Rites and Rites/Music only.");
```

- [ ] **Step 2: Run and verify RED**

Run: `dotnet run --project Tests/MandateRulerTitleRuleTests/MandateRulerTitleRuleTests.csproj --no-restore`

Expected: compilation fails because `CanStartOrdinaryDeclaration` lacks the new argument, or the definition assertion fails with the old coordinates/cost/dependencies.

- [ ] **Step 3: Add the ordinary declaration gate and retain special origins**

Change the rule signature to:

```csharp
public static bool CanStartOrdinaryDeclaration(bool pMandateAlreadyExists,
    bool pMandateRitesCompleted, out string pReason)
```

Return `already_exists` first, then `requires_mandate_rites`. In `MandateService.CanDeclareMandate`, pass:

```csharp
KingdomPolicyService.IsCompleted(pKingdom, PolicyNodeKind.Social, "aw_policy_mandate_rites")
```

Add `RequiresMandateRitesForOrigin()` so `tianming_war`, `tianmingrebel_war`, `pseudo_foreign_war`, rebel origins, and foreign-pseudo origins return false while decision/auto ordinary claims return true. In `CanDeclareMandateForOrigin`, a successful `tianming_war` performs the existing basic kingdom/king/no-active-Mandate validation and bypasses the policy, realm-size, and strongest-country gates earned by the military victory. Keep the existing rebel and foreign-pseudo validation branches unchanged.

Update the two existing `WarFabricationRuleTests` calls to pass `pMandateRitesCompleted: true`, preserving their duplicate-period assertions while the new focused tests cover the missing-policy result.

- [ ] **Step 4: Move the node and remove bypasses**

Set Mandate Rites to cost 90, only the approved dependencies, column 4, row 2. Move it in `SocialOrder` immediately after `aw_policy_ancestral_rites`. Delete the historical-figure branch in `KingdomPolicyService.ShouldIgnoreRequirement` for `aw_decision_claim_mandate`.

- [ ] **Step 5: Run the test and build**

Run: `dotnet run --project Tests/MandateRulerTitleRuleTests/MandateRulerTitleRuleTests.csproj --no-restore`

Run: `dotnet build AncientWarfare3.csproj --no-restore`

Expected: both succeed.

- [ ] **Step 6: Commit Mandate policy gate**

```powershell
git add Code/core/lineage/MandateDeclarationRules.cs Code/core/lineage/MandateService.cs Code/content/policies/KingdomPolicyDefs.cs Code/core/policy/KingdomPolicyAI.cs Code/core/policy/KingdomPolicyService.cs Tests/MandateRulerTitleRuleTests Tests/WarFabricationRuleTests
git commit -m "fix: 天命宣称要求提前后的天命礼制"
```

### Task 6: Preserve guards during succession and batch true dissolution

**Files:**
- Modify: `Code/core/lineage/RoyalGuardMaintenanceRules.cs`
- Modify: `Code/core/lineage/RoyalGuardService.cs`
- Modify: `Tests/RoyalGuardActionRuleTests/Program.cs`

- [ ] **Step 1: Write failing preservation and completion tests**

```csharp
if (!RoyalGuardMaintenanceRules.ShouldPreserveGuards(
        pSuccessionPending: true, pIsRepublic: false, pIsRebel: false, pKingdomExtinct: false))
    throw new Exception("Royal guards must survive timer_new_king.");
if (RoyalGuardMaintenanceRules.ShouldPreserveGuards(
        pSuccessionPending: false, pIsRepublic: true, pIsRebel: false, pKingdomExtinct: false))
    throw new Exception("Republics must dissolve royal guards.");
if (RoyalGuardMaintenanceRules.DismissCountForPass(
        pRemainingCount: 20, pBudget: 2) != 2)
    throw new Exception("Guard dissolution must obey its per-pass budget.");
if (RoyalGuardMaintenanceRules.ShouldClearDismissState(
        pDismissComplete: false))
    throw new Exception("Guard hints must remain until all guards are dismissed.");
```

- [ ] **Step 2: Run and verify RED**

Run: `dotnet run --project Tests/RoyalGuardActionRuleTests/RoyalGuardActionRuleTests.csproj --no-restore`

Expected: compilation fails for the new maintenance rules.

- [ ] **Step 3: Add pure rules and make dissolution return completion**

Implement the asserted methods and set a `DISMISS_BATCH_LIMIT = 2`. Change `DismissKingdomGuards` to return `bool complete`. For guard-army and roster fast paths, process at most the budget and return false if valid guards remain. Use bounded fallback scanning with both a scan budget and dismissal budget. Clear guard hints only when the returned value is true.

In `EnsureKingdomGuard`, before the no-king branch, return immediately when `timer_new_king > 0`. Check republic/rebel/extinct as true dissolution cases. On new Xia king, continue through existing refresh/army reuse so guards transfer rather than recreate.

Do not call `ClearKingdomGuardStateHints()` unconditionally after an incomplete call. Keep the existing deferred graphics dirty mechanism so each dismissed actor's expensive work is limited to the batch.

- [ ] **Step 4: Run test and build**

Run: `dotnet run --project Tests/RoyalGuardActionRuleTests/RoyalGuardActionRuleTests.csproj --no-restore`

Run: `dotnet build AncientWarfare3.csproj --no-restore`

Expected: both succeed.

- [ ] **Step 5: Commit guard performance fix**

```powershell
git add Code/core/lineage/RoyalGuardMaintenanceRules.cs Code/core/lineage/RoyalGuardService.cs Tests/RoyalGuardActionRuleTests
git commit -m "perf: 分批解散并交接禁卫军"
```

### Task 7: Bound slave-army scans and promote only after capacity checks

**Files:**
- Modify: `Code/core/lineage/SlaveArmyMaintenanceRules.cs`
- Modify: `Code/core/lineage/SlaveService.cs`
- Modify: `Code/core/lineage/LineageKeys.cs`
- Modify: `Code/core/policy/CityMaintenanceBenchmarkRules.cs`
- Modify: `Tests/CityMaintenanceRuleTests/Program.cs`

- [ ] **Step 1: Write failing fill-order, batch, cursor, and continuation tests**

```csharp
if (SlaveArmyMaintenanceRules.ShouldPromoteCandidate(
        pCompositionAllowsCandidate: false, pAlreadyWarrior: false,
        pPromotionsThisPass: 0, pPromotionLimit: 2))
    throw new Exception("Capacity/composition must be checked before promotion.");
if (!SlaveArmyMaintenanceRules.ShouldPromoteCandidate(
        pCompositionAllowsCandidate: true, pAlreadyWarrior: false,
        pPromotionsThisPass: 1, pPromotionLimit: 2))
    throw new Exception("An eligible second promotion should be allowed.");
if (SlaveArmyMaintenanceRules.ShouldPromoteCandidate(
        pCompositionAllowsCandidate: true, pAlreadyWarrior: false,
        pPromotionsThisPass: 2, pPromotionLimit: 2))
    throw new Exception("A third promotion in one pulse must be deferred.");
if (!SlaveArmyMaintenanceRules.ShouldPreferReadyWarrior(
        pCandidateIsWarrior: true, pHavePromotionCandidate: true))
    throw new Exception("Existing warriors must be attached before converting citizens.");
if (SlaveArmyMaintenanceRules.NextScanCursor(
        pStartCursor: 10, pScanned: 16, pScanComplete: false) != 26)
    throw new Exception("Incomplete candidate scans must persist their cursor.");
if (!SlaveArmyMaintenanceRules.ShouldScheduleContinuation(
        pArmyUnderfilled: true, pScanComplete: false, pAddedThisPass: 2))
    throw new Exception("Underfilled armies with remaining candidates need a short continuation.");
```

- [ ] **Step 2: Run and verify RED**

Run: `dotnet run --project Tests/CityMaintenanceRuleTests/CityMaintenanceRuleTests.csproj --no-restore`

Expected: compilation fails for the new fill rules.

- [ ] **Step 3: Add the rules, keys, and benchmark labels**

Add `SLAVE_ARMY_FILL_SCAN_CURSOR` and `SLAVE_ARMY_FILL_CONTINUE_TIME` to `LineageKeys`. Add `SlaveArmyFillScan`, `SlaveArmyFillPromotion`, and `SlaveArmyFillAttach` to `CityMaintenanceBenchmarkRules.EntryIds`. Implement the asserted methods in `SlaveArmyMaintenanceRules`.

- [ ] **Step 4: Refactor fill into a bounded pipeline**

Set promotion limit to 2 and scan limit to 32. Count army composition once before calling `FillSlaveArmy`, then pass total/slaves/non-slaves by reference. During the bounded city scan, keep small lists for ready warriors and convertible candidates; persist the cursor when the scan stops early. For each candidate:

1. calculate whether total size, cadre cap, and `CanAddSlaveToArmy()` allow attachment;
2. attach ready warriors first;
3. only then call `EnsureWarriorForSlaveArmy()` under the promotion budget;
4. update reused composition counters after successful attach.

Wrap the three phases in their profiler labels. If still underfilled and the scan/candidates indicate more work, set continuation time to `now + 2`; let `EnsureSlaveArmy` run when either the normal staggered schedule or continuation is due. Clear continuation and cursor when stable or exhausted.

- [ ] **Step 5: Run test and build**

Run: `dotnet run --project Tests/CityMaintenanceRuleTests/CityMaintenanceRuleTests.csproj --no-restore`

Run: `dotnet build AncientWarfare3.csproj --no-restore`

Expected: both succeed.

- [ ] **Step 6: Commit slave-army performance fix**

```powershell
git add Code/core/lineage/SlaveArmyMaintenanceRules.cs Code/core/lineage/SlaveService.cs Code/core/lineage/LineageKeys.cs Code/core/policy/CityMaintenanceBenchmarkRules.cs Tests/CityMaintenanceRuleTests
git commit -m "perf: 分批扫描并填充奴隶军"
```

### Task 8: Restore strict agnatic clan-tree locating

**Files:**
- Modify: `Code/core/lineage/FamilyTreeRelationRules.cs`
- Modify: `Code/core/lineage/LineageQuery.cs`
- Modify: `Code/ui/windows/FamilyTreeWindow.cs`
- Modify: `Tests/SuccessionGovernmentRuleTests/Program.cs`

- [ ] **Step 1: Write failing father-path and fallback tests**

```csharp
var fathers = new Dictionary<long, long>
{
    [4] = 2,
    [2] = 1,
    [3] = 9
};
List<long> path = FamilyTreeRelationRules.BuildAgnaticPath(4, 1,
    id => fathers.TryGetValue(id, out long father) ? father : -1L);
Expect(path.Count == 3 && path[0] == 1 && path[1] == 2 && path[2] == 4,
    "Clan locate must build founder-to-target through fathers only.");
Expect(FamilyTreeRelationRules.ShouldIncludeBigTreeEdge(
        pParentId: 2, pFatherId: 2, pChildSex: 0, pChildStatus: "noble"),
    "A visible son belongs under his father.");
Expect(!FamilyTreeRelationRules.ShouldIncludeBigTreeEdge(
        pParentId: 9, pFatherId: 2, pChildSex: 0, pChildStatus: "noble"),
    "A maternal male child must not enter the clan big tree under his mother.");
Expect(FamilyTreeRelationRules.ResolveLocateTarget(
        pRequestedId: 4, pRequestedVisible: false, pNearestVisibleFatherId: 2,
        pRootId: 1, pPathReachable: true) == 2,
    "A hidden target should center its nearest visible paternal ancestor.");
Expect(FamilyTreeRelationRules.ResolveLocateTarget(
        pRequestedId: 4, pRequestedVisible: true, pNearestVisibleFatherId: -1,
        pRootId: 1, pPathReachable: false) == 1,
    "An unreachable target must remain in the big tree at its root.");
```

- [ ] **Step 2: Run and verify RED**

Run: `dotnet run --project Tests/SuccessionGovernmentRuleTests/SuccessionGovernmentRuleTests.csproj --no-restore`

Expected: compilation fails for the new family-tree rules.

- [ ] **Step 3: Implement pure path/edge/fallback rules and query helpers**

Implement `BuildAgnaticPath`, `ShouldIncludeBigTreeEdge`, and `ResolveLocateTarget` exactly as asserted. In `LineageQuery`, add:

```csharp
public static List<long> GetAgnaticPathToAncestor(long pActorId, long pAncestorId)
    => FamilyTreeRelationRules.BuildAgnaticPath(pActorId, pAncestorId, GetFatherId);

public static long GetEarliestReachableAgnaticAncestor(long pActorId)
```

The root helper repeatedly calls `GetFatherId`, stops at missing/cycle/depth 96, and returns the last valid actor ID.

- [ ] **Step 4: Keep big-tree mode and use father-only children**

In `OpenBigTreeLocate`, if the registered founder is missing, use the earliest reachable agnatic ancestor. Seed expansion with the strict path. If the requested node is hidden, replace `_locateActorId` with its nearest visible father. If the path cannot reach the root, set `_locateActorId = _rootActorId`.

Add a big-tree child cache that filters `LineageQuery.GetChildIds(parent)` using `GetFatherId(child) == parent` and male visibility. Use it in layout, live expansion, collapse, and descendant probes. Delete both calls that fall back to `OpenFamilyTree()` on locate failure. Set `showTreeTools = _mode == Mode.BigTree`.

- [ ] **Step 5: Run test and build**

Run: `dotnet run --project Tests/SuccessionGovernmentRuleTests/SuccessionGovernmentRuleTests.csproj --no-restore`

Run: `dotnet build AncientWarfare3.csproj --no-restore`

Expected: both succeed.

- [ ] **Step 6: Commit clan-tree locate fix**

```powershell
git add Code/core/lineage/FamilyTreeRelationRules.cs Code/core/lineage/LineageQuery.cs Code/ui/windows/FamilyTreeWindow.cs Tests/SuccessionGovernmentRuleTests
git commit -m "fix: 恢复氏族大树父系定位"
```

### Task 9: Full verification and requirement audit

**Files:**
- Verify: all modified production/test files
- Modify only if verification exposes a concrete regression, always by adding a failing test first

- [ ] **Step 1: Run every rule-test project from a clean command**

Run:

```powershell
$projects = Get-ChildItem -LiteralPath Tests -Recurse -Filter *.csproj | Sort-Object FullName
foreach ($project in $projects) {
    dotnet run --project $project.FullName --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Rule test failed: $($project.FullName)" }
}
```

Expected: 11 projects print their pass messages and the command exits 0.

- [ ] **Step 2: Run a fresh full build**

Run: `dotnet build AncientWarfare3.csproj --no-restore`

Expected: `Build succeeded`, zero warnings, zero errors.

- [ ] **Step 3: Inspect formatting and changed scope**

Run: `git diff --check`

Expected: no output.

Run: `git status --short`

Expected: only intentional files, or clean after the final commit.

Run: `git diff master...HEAD --stat`

Expected: changes are limited to the design/plan, listed production files, and rule tests.

- [ ] **Step 4: Audit each approved behavior**

Confirm from code and tests:

1. ordinary/automatic Mandate needs Mandate Rites; special military origins remain exempt;
2. Mandate Rites is at column 4/row 2, cost 90, with only the two approved dependencies;
3. old-king ID survives `timer_new_king`, cached heir is not overwritten, and surviving brothers remain eligible;
4. only kings are excluded by office; leaders/generals/captains/fief holders remain eligible;
5. 王子 city-leader priority remains and now uses read-only heir lookup;
6. vanilla `checkKingdomChaos` is blocked for managed lineage while `shattered_crown` remains;
7. republic rank 1 leads, rank 2 succeeds, and succession does not clear republic;
8. new split kingdoms do not inherit republic class state;
9. transition does not add rebellion crisis or dismiss guards;
10. guard dissolution and slave fill are bounded;
11. clan locate is father-only, remains in big-tree mode, and tools are visible.

- [ ] **Step 5: Commit final verification adjustments, if any tested change was required**

```powershell
git add Code Tests docs
git commit -m "test: 完成继承政体修复回归验证"
```

If no adjustment was required, do not create an empty commit.

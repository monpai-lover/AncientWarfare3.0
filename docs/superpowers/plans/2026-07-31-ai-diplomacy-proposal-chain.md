# AI Diplomacy Proposal Chain Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give ordinary AI complete, replay-safe proposal paths for joining wars, diplomatic vassalization, peaceful vassal release, unilateral alliance withdrawal, tributary internalization, and upper-to-lower household offers.

**Architecture:** Capture live game state into small immutable opportunity facts, evaluate those facts through shared pure rules, and carry direction plus exact war identity through synchronous and asynchronous proposal creation. Keep relationship and treaty mutations in dedicated transactional persistence helpers, with the proposal service coordinating live-world effects and recovery.

**Tech Stack:** C# 11, .NET Framework 4.8 production mod, .NET 9 executable rule tests, System.Data.SQLite transaction tests, existing AW3 async strategy and diplomacy proposal services.

---

## File Map

- Create `Code/core/lineage/DiplomacyProposalOpportunityRules.cs`: pure direction, eligibility, urgency, protection-risk, and contract-tier rules.
- Create `Tests/AncientWarfare3.Rules.Tests/DiplomacyProposalOpportunityRulesTests.cs.txt`: behavioral unit tests for all new opportunity rules.
- Modify `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`: compile the new rules and tests.
- Modify `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`: run the new tests.
- Modify `Code/core/lineage/AsyncDiplomacyPlanModels.cs`: append async proposal kinds and include `WarId` in candidate identity.
- Modify `Code/core/lineage/DiplomacyProposalAiRules.cs`: rank the new candidates from data-derived urgency.
- Modify `Code/core/lineage/DiplomacyProposalRules.cs`: direction-aware availability and acceptance, unilateral alliance withdrawal.
- Modify `Code/core/lineage/DiplomacyProposalService.cs`: generate, capture, commit, execute, and recover all new candidates with exact identity.
- Modify `Code/core/lineage/VassalAIService.cs`: route seek-protection through the proposal service.
- Create `Code/core/lineage/VassalRelationConversionPersistence.cs`: atomic tributary-to-vassal database conversion.
- Create `Tests/AncientWarfare3.Rules.Tests/VassalRelationConversionPersistenceTests.cs.txt`: commit and rollback tests.
- Modify `Code/core/lineage/VassalService.cs`: expose internalization and projection updates.
- Modify `Code/core/lineage/DiplomacyTreatyPersistence.cs`: idempotent proposal-keyed truce registration.
- Modify `Tests/AncientWarfare3.Rules.Tests/DiplomacyTreatyPersistenceTests.cs.txt`: alliance-withdrawal truce tests.
- Modify `Code/core/lineage/RulerHouseholdRules.cs`: explicit upper-to-lower offer eligibility.
- Modify `Tests/AncientWarfare3.Rules.Tests/RulerHouseholdRulesTests.cs.txt`: principal-wife and consort direction tests.
- Modify `Code/core/lineage/DiplomacyConversationService.cs`: direction-specific request and result text.
- Modify `Locales/aw3_diplomacy.csv`: action details and specific failure reasons.

### Task 1: Add pure opportunity rules

**Files:**
- Create: `Code/core/lineage/DiplomacyProposalOpportunityRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/DiplomacyProposalOpportunityRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write failing tests for direction and thresholds**

Add assertions covering these exact cases:

```csharp
TestAssert.Equal(OrdinaryDiplomacyDirection.JoinWar,
    DiplomacyProposalOpportunityRules.JoinWarDirection(
        allied: true, requesterInWar: true, responderInWar: false,
        subjectConflict: false));
TestAssert.Equal(OrdinaryDiplomacyDirection.None,
    DiplomacyProposalOpportunityRules.JoinWarDirection(
        allied: true, requesterInWar: true, responderInWar: true,
        subjectConflict: false));

TestAssert.Equal(OrdinaryDiplomacyDirection.VassalizeDemand,
    DiplomacyProposalOpportunityRules.VassalizeDirection(
        atWar: false, allied: false, requesterIsSubject: false,
        responderIsSubject: false, canSetVassal: true,
        requesterToResponderPower: 2.1f, threatened: false,
        defensiveEmergency: false, requesterTributaryOfResponder: false,
        responderImperial: false));
TestAssert.Equal(OrdinaryDiplomacyDirection.VassalizeSeek,
    DiplomacyProposalOpportunityRules.VassalizeDirection(
        atWar: true, allied: false, requesterIsSubject: false,
        responderIsSubject: false, canSetVassal: true,
        requesterToResponderPower: .4f, threatened: true,
        defensiveEmergency: true, requesterTributaryOfResponder: false,
        responderImperial: false));
TestAssert.Equal(OrdinaryDiplomacyDirection.VassalizeInternalize,
    DiplomacyProposalOpportunityRules.VassalizeDirection(
        atWar: false, allied: false, requesterIsSubject: true,
        responderIsSubject: false, canSetVassal: true,
        requesterToResponderPower: .4f, threatened: false,
        defensiveEmergency: false, requesterTributaryOfResponder: true,
        responderImperial: true));
TestAssert.Equal(VassalContractTierRules.Inner,
    DiplomacyProposalOpportunityRules.InternalizationTier(
        requesterTributaryOfResponder: true, responderImperial: true,
        responderHasMandate: true));
TestAssert.Equal(VassalContractTierRules.Outer,
    DiplomacyProposalOpportunityRules.InternalizationTier(
        requesterTributaryOfResponder: true, responderImperial: true,
        responderHasMandate: false));

TestAssert.Equal(OrdinaryDiplomacyDirection.EndVassalRelease,
    DiplomacyProposalOpportunityRules.EndVassalDirection(
        requesterSuzerainOfResponder: true,
        requesterSubjectOfResponder: false));
TestAssert.Equal(OrdinaryDiplomacyDirection.EndVassalRequest,
    DiplomacyProposalOpportunityRules.EndVassalDirection(
        requesterSuzerainOfResponder: false,
        requesterSubjectOfResponder: true));

TestAssert.True(DiplomacyProposalOpportunityRules.ShouldEndAlliance(
    allied: true, opinion: -55, liabilityScore: 20));
TestAssert.False(DiplomacyProposalOpportunityRules.ShouldEndAlliance(
    allied: true, opinion: 70, liabilityScore: 0));

TestAssert.True(DiplomacyProposalOpportunityRules.CanUpperRealmOfferHousehold(
    requesterSuzerainOfResponder: true, candidateAvailable: true,
    recipientRulerEligible: true));
```

- [ ] **Step 2: Register the test and production file**

Add these compile entries:

```xml
<Compile Include="DiplomacyProposalOpportunityRulesTests.cs.txt" />
<Compile Include="..\..\Code\core\lineage\DiplomacyProposalOpportunityRules.cs"
         Link="Production\DiplomacyProposalOpportunityRules.cs" />
```

Call `DiplomacyProposalOpportunityRulesTests.Run();` from `Program.cs.txt`.

- [ ] **Step 3: Run RED**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
```

Expected: compilation fails because `OrdinaryDiplomacyDirection` and `DiplomacyProposalOpportunityRules` do not exist.

- [ ] **Step 4: Implement the pure rules**

Create the enum and methods with stable detail IDs:

```csharp
internal enum OrdinaryDiplomacyDirection
{
    None = 0,
    JoinWar = 1,
    VassalizeDemand = 2,
    VassalizeSeek = 3,
    VassalizeInternalize = 4,
    EndVassalRelease = 5,
    EndVassalRequest = 6,
    EndAlliance = 7,
    UpperHouseholdOffer = 8
}

internal static class DiplomacyProposalOpportunityRules
{
    public const string VassalizeDemandDetail = "vassalize_demand";
    public const string VassalizeSeekDetail = "vassalize_seek";
    public const string VassalizeInternalizeDetail =
        "vassalize_internalize";
    public const string EndVassalReleaseDetail = "end_vassal_release";
    public const string EndVassalRequestDetail = "end_vassal_request";

    public static int InternalizationTier(bool requesterTributaryOfResponder,
        bool responderImperial, bool responderHasMandate)
    {
        if (!requesterTributaryOfResponder || !responderImperial) return -1;
        return responderHasMandate
            ? VassalContractTierRules.Inner
            : VassalContractTierRules.Outer;
    }

    public static int ProtectionRiskPenalty(float enemyToProtectorPower,
        bool excellentRelations, bool sharedEnemy, float warCourt)
    {
        float ratio = Math.Max(0f, enemyToProtectorPower);
        int penalty = ratio > 1.6f ? -70 : ratio >= 1.2f ? -35 :
            ratio <= .8f ? 15 : 0;
        if (excellentRelations) penalty += 15;
        if (sharedEnemy) penalty += 15;
        if (warCourt >= .75f) penalty += 15;
        return penalty;
    }
}
```

Add the remaining methods with internalization evaluated before generic
subject rejection:

```csharp
public static OrdinaryDiplomacyDirection JoinWarDirection(bool allied,
    bool requesterInWar, bool responderInWar, bool subjectConflict)
{
    return allied && requesterInWar && !responderInWar && !subjectConflict
        ? OrdinaryDiplomacyDirection.JoinWar
        : OrdinaryDiplomacyDirection.None;
}

public static OrdinaryDiplomacyDirection VassalizeDirection(bool atWar,
    bool allied, bool requesterIsSubject, bool responderIsSubject,
    bool canSetVassal, float requesterToResponderPower, bool threatened,
    bool defensiveEmergency, bool requesterTributaryOfResponder,
    bool responderImperial)
{
    if (requesterTributaryOfResponder && responderImperial &&
        canSetVassal)
        return OrdinaryDiplomacyDirection.VassalizeInternalize;
    if (allied || requesterIsSubject || responderIsSubject ||
        !canSetVassal)
        return OrdinaryDiplomacyDirection.None;
    if (!atWar && requesterToResponderPower >= 2f)
        return OrdinaryDiplomacyDirection.VassalizeDemand;
    if ((!atWar && threatened || defensiveEmergency) &&
        requesterToResponderPower < 1f)
        return OrdinaryDiplomacyDirection.VassalizeSeek;
    return OrdinaryDiplomacyDirection.None;
}

public static OrdinaryDiplomacyDirection EndVassalDirection(
    bool requesterSuzerainOfResponder, bool requesterSubjectOfResponder)
{
    if (requesterSuzerainOfResponder)
        return OrdinaryDiplomacyDirection.EndVassalRelease;
    return requesterSubjectOfResponder
        ? OrdinaryDiplomacyDirection.EndVassalRequest
        : OrdinaryDiplomacyDirection.None;
}

public static bool ShouldEndAlliance(bool allied, int opinion,
    int liabilityScore)
{
    return allied && (opinion <= -40 || liabilityScore >= 50);
}

public static bool CanUpperRealmOfferHousehold(
    bool requesterSuzerainOfResponder, bool candidateAvailable,
    bool recipientRulerEligible)
{
    return requesterSuzerainOfResponder && candidateAvailable &&
           recipientRulerEligible;
}
```

- [ ] **Step 5: Run GREEN and commit**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
git add Code/core/lineage/DiplomacyProposalOpportunityRules.cs Tests/AncientWarfare3.Rules.Tests
git commit -m "feat: define ordinary diplomacy opportunity rules"
```

Expected: `Rule tests passed.`

### Task 2: Preserve async proposal identity

**Files:**
- Modify: `Code/core/lineage/AsyncDiplomacyPlanModels.cs`
- Modify: `Code/core/lineage/DiplomacyProposalService.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/AsyncDiplomacyProposalIdentityTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write RED identity tests**

Assert existing enum values remain `HouseholdOffering == 9`, then assert:

```csharp
TestAssert.Equal(10, (int)AsyncDiplomacyProposalKind.JoinWar);
TestAssert.Equal(11, (int)AsyncDiplomacyProposalKind.Vassalize);
TestAssert.Equal(12, (int)AsyncDiplomacyProposalKind.EndVassal);

var first = new AsyncDiplomacySelectionIdentity(2, 4,
    AsyncDiplomacyProposalKind.JoinWar, 91, -1, -1, -1, -1, "");
var otherWar = new AsyncDiplomacySelectionIdentity(2, 4,
    AsyncDiplomacyProposalKind.JoinWar, 92, -1, -1, -1, -1, "");
TestAssert.False(first.Matches(otherWar));
```

- [ ] **Step 2: Run RED**

Expected: compile failure for missing enum members and constructor argument.

- [ ] **Step 3: Append enum values and add `WarId`**

Append without renumbering:

```csharp
JoinWar = 10,
Vassalize = 11,
EndVassal = 12
```

Add `WarId` to `AsyncDiplomacySelectionIdentity`, its constructor, and
`Matches`. Add `WarId` to `AsyncDiplomacyCommitCandidate` and pass it into
`Identity`.

- [ ] **Step 4: Map proposal types and run GREEN**

Extend `AsyncKind` for all three types. Update every constructor call to pass
`-1L` until prepared candidates begin supplying exact war IDs in Task 3.

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
git add Code/core/lineage/AsyncDiplomacyPlanModels.cs Code/core/lineage/DiplomacyProposalService.cs Tests/AncientWarfare3.Rules.Tests
git commit -m "feat: preserve async diplomacy war identity"
```

### Task 3: Generate and commit exact ordinary AI candidates

**Files:**
- Modify: `Code/core/lineage/DiplomacyProposalService.cs`
- Modify: `Code/core/lineage/DiplomacyProposalAiRules.cs`
- Create: `Tests/OrdinaryDiplomacyProposalSourceGuardTests.ps1`

- [ ] **Step 1: Add a failing source guard**

The guard must require:

```powershell
Require 'prepared candidates carry war identity' `
    'private sealed class PreparedAiProposal' 'public long WarId = -1L;'
Require 'ordinary join-war candidate exists' `
    'TryBuildOrdinaryAiProposals' 'TryPrepareJoinWarCandidate'
Require 'readonly join-war candidate exists' `
    'TryBuildOrdinaryAiProposalsReadOnly' 'TryPrepareJoinWarCandidateReadOnly'
Require 'prepared creation uses selected war' `
    'TryCreatePreparedOrdinary' 'prepared.WarId'
Require 'async commit uses selected war' `
    'TryCommitAsyncProposal' 'currentSelected.WarId'
```

- [ ] **Step 2: Run RED**

```powershell
pwsh -NoProfile -File Tests/OrdinaryDiplomacyProposalSourceGuardTests.ps1
```

Expected: failure on the first missing requirement.

- [ ] **Step 3: Carry `WarId` in prepared candidates**

Add `public long WarId = -1L;` to `PreparedAiProposal`. Pass it into
`AsyncDiplomacyCommitCandidate`. Change `TryCreatePreparedOrdinary` and
`TryCommitAsyncProposal` to call `TryCreateSelected` with the prepared
selection and exact war ID for every type, replacing the current split that
uses `TryCreate(..., -1L)`.

- [ ] **Step 4: Add deterministic join-war selection**

Add a bounded selector that sorts joinable requester wars by:

1. requester capital threatened;
2. requester losing position;
3. enemy coalition power descending;
4. `war.data.id` ascending.

Create one prepared `JoinWar` candidate with that `WarId`. Use
`AssessWithSelection` or its read-only counterpart with the same ID, and add it
only when expected acceptance passes.

- [ ] **Step 5: Add candidates for vassalization, end-vassal, and end-alliance**

Capture relation direction and construct the stable detail IDs from Task 1.
Add bilateral candidates only when expected acceptance passes. Add
`EndAlliance` without acceptance gating when the pure rule marks it harmful.
Increase the candidate list initial capacity from `6` to `12` in both builders.

- [ ] **Step 6: Rank from urgency**

Extend `DiplomacyProposalAiCandidate` with optional `int urgency = 0` and add
the property. Score the new actions as follows while retaining existing
actions:

```csharp
DiplomacyProposalType.JoinWar => 70 + pCandidate.Urgency + opinion / 4,
DiplomacyProposalType.Vassalize => 65 + pCandidate.Urgency +
    Math.Min(50, (int)(Math.Max(0f,
        pCandidate.RequesterPowerRatio - 1f) * 20f)),
DiplomacyProposalType.EndVassal => 50 + pCandidate.Urgency,
DiplomacyProposalType.EndAlliance => 45 + pCandidate.Urgency - opinion / 2,
```

- [ ] **Step 7: Run guard, rule tests, build, and commit**

```powershell
pwsh -NoProfile -File Tests/OrdinaryDiplomacyProposalSourceGuardTests.ps1
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
dotnet build AncientWarfare3.csproj -c Debug --nologo
git add Code/core/lineage/DiplomacyProposalService.cs Code/core/lineage/DiplomacyProposalAiRules.cs Tests/OrdinaryDiplomacyProposalSourceGuardTests.ps1
git commit -m "feat: generate complete ordinary diplomacy candidates"
```

### Task 4: Execute directional vassalization and emergency protection

**Files:**
- Modify: `Code/core/lineage/DiplomacyProposalRules.cs`
- Modify: `Code/core/lineage/DiplomacyProposalService.cs`
- Modify: `Code/core/lineage/VassalAIService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/DiplomacyProposalOpportunityRulesTests.cs.txt`
- Create: `Tests/VassalAiProposalChainSourceGuardTests.ps1`

- [ ] **Step 1: Add RED acceptance tests**

Add protection risk assertions:

```csharp
TestAssert.True(DiplomacyProposalOpportunityRules.ProtectionRiskPenalty(
    .7f, false, false, .5f) > 0);
TestAssert.True(DiplomacyProposalOpportunityRules.ProtectionRiskPenalty(
    1.4f, false, false, .5f) <= -35);
TestAssert.True(DiplomacyProposalOpportunityRules.ProtectionRiskPenalty(
    1.8f, true, true, .8f) < 0);
```

The source guard must reject `StartDecisionWithTarget(...
"aw_decision_seek_suzerain"...)` in `TryActiveVassal` and require a call to
`DiplomacyProposalService.TryCreateAiProtectionProposal`.

- [ ] **Step 2: Make assessment direction-aware**

Normalize an empty vassalization detail to `vassalize_demand`. Add service
prevalidation before generic availability:

- demand: responder becomes requester `Outer` subject;
- seek: requester becomes responder `Outer` subject;
- internalize: deferred to Task 5.

For seek acceptance, start with base `30`, add opinion and diplomacy, then add
`ProtectionRiskPenalty`. Generic at-war rejection is bypassed only for a stored
active defensive emergency war.

- [ ] **Step 3: Execute demand and seek from persisted direction**

Use the exact direction in the `Vassalize` switch. For emergency seek:

```csharp
bool created = VassalService.SetVassal(requester, responder,
    "diplomatic_protection", pProposal.WarId,
    pContractTier: VassalContractTierRules.Outer);
if (!created) return Failure("subject_write_failed");
if (!TryJoinProtectorToDefensiveWar(responder, requester,
        pProposal.WarId))
{
    VassalService.EndVassal(requester,
        "protection_war_entry_failed");
    return Failure("protection_war_entry_failed");
}
```

`TryJoinProtectorToDefensiveWar` must revalidate that requester is defender,
protector is absent from both sides, and no subject conflict exists before
calling `joinDefenders` under `AllianceCall` scope.

- [ ] **Step 4: Route `VassalAIService` through the proposal**

Replace the direct decision call with a proposal helper that supplies the
chosen protector, threat, defensive war ID when present, and enemy/protector
power ratio. Set `LAST_ACTION_YEAR` only when proposal creation succeeds.

- [ ] **Step 5: Verify and commit**

```powershell
pwsh -NoProfile -File Tests/VassalAiProposalChainSourceGuardTests.ps1
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
dotnet build AncientWarfare3.csproj -c Debug --nologo
git add Code/core/lineage/DiplomacyProposalRules.cs Code/core/lineage/DiplomacyProposalService.cs Code/core/lineage/VassalAIService.cs Tests
git commit -m "feat: negotiate vassal protection through proposals"
```

### Task 5: Convert tributaries transactionally

**Files:**
- Create: `Code/core/lineage/VassalRelationConversionPersistence.cs`
- Modify: `Code/core/lineage/VassalService.cs`
- Modify: `Code/core/lineage/DiplomacyProposalService.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/VassalRelationConversionPersistenceTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write SQLite RED tests**

Create an in-memory `VassalRelation` table with the production columns used by
conversion. Insert one active tributary row, convert it, and assert:

```csharp
TestAssert.Equal(1, ScalarInt(db,
    "SELECT COUNT(*) FROM VassalRelation WHERE ACTIVE=1"));
TestAssert.Equal(VassalContractTierRules.Inner, ScalarInt(db,
    "SELECT CONTRACT_TIER FROM VassalRelation WHERE ACTIVE=1"));
TestAssert.Equal(0, ScalarInt(db,
    "SELECT ACTIVE FROM VassalRelation WHERE RELATION_ID=1"));
```

Add a unique constraint that forces replacement insertion to fail and assert
the old tributary remains active, proving rollback.

- [ ] **Step 2: Run RED**

Expected: compile failure because the persistence class does not exist.

- [ ] **Step 3: Implement transaction persistence**

`TryConvert` must begin one SQLite transaction, select and validate exactly one
active source relation for the requester and current suzerain, close it, insert
the replacement row with the requested tier, and commit. Catching any error
rolls back and returns `false` with a stable reason.

- [ ] **Step 4: Add `VassalService.TryInternalizeTributary`**

Validate the live tributary projection, imperial responder, chosen `Inner` or
`Outer` tier, title hierarchy, adjacency, cycle, and rebel gates. Call the
persistence helper. After commit, decrement tributary count, increment vassal
count, replace actor-data projection keys, dirty the map, write history, and
mark both strategy revisions.

- [ ] **Step 5: Execute `vassalize_internalize`**

Require requester tributary suzerain to equal responder and resolve tier with
`InternalizationTier`. Call `TryInternalizeTributary`; never call generic
`SetVassal` for this direction.

- [ ] **Step 6: Verify and commit**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
dotnet build AncientWarfare3.csproj -c Debug --nologo
git add Code/core/lineage/VassalRelationConversionPersistence.cs Code/core/lineage/VassalService.cs Code/core/lineage/DiplomacyProposalService.cs Tests/AncientWarfare3.Rules.Tests
git commit -m "feat: internalize tributaries atomically"
```

### Task 6: Implement peaceful release and unilateral alliance withdrawal

**Files:**
- Modify: `Code/core/lineage/DiplomacyProposalRules.cs`
- Modify: `Code/core/lineage/DiplomacyProposalService.cs`
- Modify: `Code/core/lineage/DiplomacyTreatyPersistence.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/DiplomacyTreatyPersistenceTests.cs.txt`

- [ ] **Step 1: Write RED truce persistence tests**

Call a new `EnsureProposalTruce` twice with the same withdrawal proposal ID.
Assert both calls succeed, exactly one accepted `truce` row exists, and
`TREATY_UNTIL_YEAR == currentYear + BrokenPactTruceYears`.

- [ ] **Step 2: Implement idempotent truce persistence**

Add `EnsureProposalTruce` keyed by source proposal ID in `DETAIL_ID` as
`alliance_withdrawal:<id>`. It returns the existing adequate truce or inserts
one accepted truce row in a transaction.

- [ ] **Step 3: Make `EndAlliance` unilateral and retry-safe**

Include `EndAlliance` in `IsUnilateral`. In `Execute`, leave the requester only
if still allied, then ensure the five-year truce. Change `EffectAlreadyApplied`
to require both `!SafeAllied(requester, responder)` and an active adequate
proposal-keyed truce.

- [ ] **Step 4: Execute both end-vassal directions**

For `end_vassal_release`, require responder remains requester's direct subject.
For `end_vassal_request`, require requester remains responder's direct subject.
Legacy empty detail retains current live-relation inference. Both call
`EndVassal` only after acceptance.

- [ ] **Step 5: Verify and commit**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
dotnet build AncientWarfare3.csproj -c Debug --nologo
git add Code/core/lineage/DiplomacyProposalRules.cs Code/core/lineage/DiplomacyProposalService.cs Code/core/lineage/DiplomacyTreatyPersistence.cs Tests/AncientWarfare3.Rules.Tests/DiplomacyTreatyPersistenceTests.cs.txt
git commit -m "feat: negotiate release and protect alliance withdrawal"
```

### Task 7: Add upper-to-lower household offers

**Files:**
- Modify: `Code/core/lineage/RulerHouseholdRules.cs`
- Modify: `Code/core/lineage/DiplomacyProposalService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/RulerHouseholdRulesTests.cs.txt`

- [ ] **Step 1: Write RED household direction tests**

Assert a direct suzerain with a valid candidate can offer, an unrelated realm
cannot use the special branch, no principal wife selects `PrincipalWife`, and
an existing principal wife with spare capacity selects `Consort`.

- [ ] **Step 2: Add the explicit suzerain candidate branch**

When requester is the responder's direct vassal or tributary suzerain, call
`TryPrepareAiOffer(requester, responder)`. Preserve the returned actor and
ruler IDs in selection. Do not require independent realms for this offer.

- [ ] **Step 3: Preserve normal execution**

Keep `HouseholdOffering` execution through `RulerHouseholdService.TryCommit`.
Commit-time preview must prove the candidate still belongs to the suzerain and
the receiving actor is still the lower realm's ruler.

- [ ] **Step 4: Verify and commit**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
dotnet build AncientWarfare3.csproj -c Debug --nologo
git add Code/core/lineage/RulerHouseholdRules.cs Code/core/lineage/DiplomacyProposalService.cs Tests/AncientWarfare3.Rules.Tests/RulerHouseholdRulesTests.cs.txt
git commit -m "feat: let suzerains offer spouses to subject rulers"
```

### Task 8: Localize directions and failure reasons

**Files:**
- Modify: `Code/core/lineage/DiplomacyConversationService.cs`
- Modify: `Code/core/lineage/DiplomacyProposalService.cs`
- Modify: `Locales/aw3_diplomacy.csv`
- Create: `Tests/AiDiplomacyProposalLocalizationSourceGuardTests.ps1`

- [ ] **Step 1: Write RED localization guard**

Require simplified Chinese, English, and traditional Chinese values for:

```text
aw_diplomacy_detail_vassalize_demand
aw_diplomacy_detail_vassalize_seek
aw_diplomacy_detail_vassalize_internalize
aw_diplomacy_detail_end_vassal_release
aw_diplomacy_detail_end_vassal_request
aw_diplomacy_failure_join_war_stale
aw_diplomacy_failure_protector_war_conflict
aw_diplomacy_failure_protection_war_entry
aw_diplomacy_failure_internalization_target
aw_diplomacy_failure_internalization_write
aw_diplomacy_failure_alliance_truce_write
```

- [ ] **Step 2: Add conversation routing and locale rows**

Render the persisted direction in proposal request, response, and history
summaries. Map every new reason in the UI failure switch instead of returning
the generic unavailable message.

- [ ] **Step 3: Verify and commit**

```powershell
pwsh -NoProfile -File Tests/AiDiplomacyProposalLocalizationSourceGuardTests.ps1
dotnet build AncientWarfare3.csproj -c Debug --nologo
git add Code/core/lineage/DiplomacyConversationService.cs Code/core/lineage/DiplomacyProposalService.cs Locales/aw3_diplomacy.csv Tests/AiDiplomacyProposalLocalizationSourceGuardTests.ps1
git commit -m "feat: localize directional diplomacy proposals"
```

### Task 9: Full verification and deployment readiness

**Files:**
- Verify all files above.

- [ ] **Step 1: Run focused guards and rule tests**

```powershell
pwsh -NoProfile -File Tests/OrdinaryDiplomacyProposalSourceGuardTests.ps1
pwsh -NoProfile -File Tests/VassalAiProposalChainSourceGuardTests.ps1
pwsh -NoProfile -File Tests/AiDiplomacyProposalLocalizationSourceGuardTests.ps1
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
```

Expected: every command exits `0`; rules print `Rule tests passed.`

- [ ] **Step 2: Re-run the async identity tests through the tracked rules entry**

The repository does not contain separate tracked async or multiplayer test
projects at this baseline. Run the tracked rule executable again after a clean
build; it compiles `AsyncDiplomacyPlanModels.cs` and executes the identity tests
added in Task 2.

```powershell
dotnet clean Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --nologo
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
```

- [ ] **Step 3: Build production**

```powershell
dotnet build AncientWarfare3.csproj -c Debug --nologo
```

Expected: build succeeds with zero errors and zero warnings.

- [ ] **Step 4: Audit diff and branch scope**

```powershell
git diff --check
git status --short
git log --oneline master..HEAD
git diff --stat master...HEAD
```

Expected: no whitespace errors, a clean worktree, and no files outside this
plan's file map.

- [ ] **Step 5: Deploy only changed production files after merge**

Stop WorldBox before copying. From the merged `master`, copy only these changed
runtime files to the same relative paths under
`D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`:

```text
Code/core/lineage/DiplomacyProposalOpportunityRules.cs
Code/core/lineage/AsyncDiplomacyPlanModels.cs
Code/core/lineage/DiplomacyProposalAiRules.cs
Code/core/lineage/DiplomacyProposalRules.cs
Code/core/lineage/DiplomacyProposalService.cs
Code/core/lineage/VassalAIService.cs
Code/core/lineage/VassalRelationConversionPersistence.cs
Code/core/lineage/VassalService.cs
Code/core/lineage/DiplomacyTreatyPersistence.cs
Code/core/lineage/RulerHouseholdRules.cs
Code/core/lineage/DiplomacyConversationService.cs
Locales/aw3_diplomacy.csv
```

Use `Copy-Item -Force` per file. Do not mirror-delete the installed mod and do
not copy tests, docs, `bin`, `obj`, or `.runtime`. Compare `Get-FileHash` for
each source and destination, then run:

```powershell
dotnet build 'D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0/AncientWarfare3.csproj' -c Debug --no-restore --nologo
```

In game, verify one example of each direction and confirm logs contain no
generic `unavailable`, stale async identity, or proposal recovery loop.

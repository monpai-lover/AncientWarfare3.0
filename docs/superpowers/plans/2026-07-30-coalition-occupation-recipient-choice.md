# Coalition Occupation Recipient Choice Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a winning-side war leader choose whether an allied-occupied enemy city is ceded to the occupying ally or to the war leader.

**Architecture:** Frozen `WarScoreControl` rows remain authoritative for the source realm and controller. Candidate generation emits one controller-recipient term and, when applicable, one war-leader-recipient term; pure authorization rules constrain both generation and execution. The presentation carries `CityId` so the existing toggle UI can make recipient alternatives mutually exclusive while the settlement service retains duplicate-city defense in depth.

**Tech Stack:** C# 10/net48 game mod, PowerShell source and presentation tests, isolated net9.0 rule tests, Harmony/NeoModLoader runtime deployment.

---

### Task 1: Frozen Occupation Recipient Authority

**Files:**
- Modify: `Code/core/lineage/WarPeaceSettlementValidationRules.cs:188-230`
- Modify: `Tests/WarPeaceSettlementServiceTests.cs:2322-2425`

- [ ] **Step 1: Write the failing recipient-authority tests**

Add isolated assertions for a pure rule with this contract:

```csharp
Equal(true,
    WarPeaceSettlementValidationRules.CanReceiveFrozenOccupation(
        recipientIsController: true,
        recipientIsWarLeader: false,
        recipientOnControllerSide: true),
    "the occupation controller may receive its captured city");
Equal(true,
    WarPeaceSettlementValidationRules.CanReceiveFrozenOccupation(
        recipientIsController: false,
        recipientIsWarLeader: true,
        recipientOnControllerSide: true),
    "the controller-side war leader may take the captured city");
Equal(false,
    WarPeaceSettlementValidationRules.CanReceiveFrozenOccupation(
        recipientIsController: false,
        recipientIsWarLeader: false,
        recipientOnControllerSide: true),
    "an ordinary ally cannot be selected as an arbitrary recipient");
Equal(false,
    WarPeaceSettlementValidationRules.CanReceiveFrozenOccupation(
        recipientIsController: false,
        recipientIsWarLeader: true,
        recipientOnControllerSide: false),
    "the opposing war leader cannot receive the city");
```

Extend execution assertions to cover `currentOwnerMatchesController` when the
chosen recipient is the controller-side war leader.

- [ ] **Step 2: Run the isolated suite and verify RED**

Run:

```powershell
dotnet run --project Tests/WarPeaceSettlementServiceTests.csproj --no-restore
```

Expected: FAIL because `CanReceiveFrozenOccupation` does not exist or does not
authorize the controller-side war leader.

- [ ] **Step 3: Implement the minimal pure rules**

Add:

```csharp
public static bool CanReceiveFrozenOccupation(
    bool recipientIsController, bool recipientIsWarLeader,
    bool recipientOnControllerSide)
{
    return recipientOnControllerSide &&
           (recipientIsController || recipientIsWarLeader);
}
```

Add a six-fact execution overload which permits a live owner matching the
source, selected recipient, or recorded controller only when frozen authority
exists. Keep the five-fact overload as a compatibility wrapper for existing
callers and tests.

- [ ] **Step 4: Run the isolated suite and verify GREEN**

Run the command from Step 2. Expected: `War peace settlement isolated tests passed.`

- [ ] **Step 5: Commit the pure rule slice**

```powershell
git add -- Code/core/lineage/WarPeaceSettlementValidationRules.cs Tests/WarPeaceSettlementServiceTests.cs
git commit -m "feat: authorize war leaders as occupation recipients"
```

### Task 2: Emit Controller And War-Leader City Candidates

**Files:**
- Modify: `Code/core/lineage/WarPeaceSettlementRuntime.cs:639-780`
- Modify: `Tests/WarPeaceCandidateEligibilitySourceGuard.ps1`
- Modify: `Tests/WarPeaceSettlementServiceTests.cs`

- [ ] **Step 1: Write failing candidate-generation guards**

Require the frozen-occupation loop to add a controller candidate and a second
war-leader candidate only when controller and leader IDs differ. Require the
controller candidate priority to exceed the leader candidate priority so AI
selects the controller first at equal cost.

```powershell
if (-not $method.Contains('AddFrozenOccupationCandidate(') -or
    -not $method.Contains('recipient.id != beneficiary.id') -or
    -not $method.Contains('controllerPriority: 76') -or
    -not $method.Contains('warLeaderPriority: 75')) {
    throw 'Frozen occupations do not expose controller and war-leader recipients.'
}
```

Add an isolated default-offer test with two same-city candidates ordered at
priorities 76 and 75; assert that the selected term uses the controller's
recipient ID.

- [ ] **Step 2: Run candidate tests and verify RED**

```powershell
& Tests/WarPeaceCandidateEligibilitySourceGuard.ps1
dotnet run --project Tests/WarPeaceSettlementServiceTests.csproj --no-restore
```

Expected: the source guard fails because only the controller candidate exists.

- [ ] **Step 3: Implement bounded dual-candidate generation**

Inside `BuildDefaultCandidates`, resolve the controller from each bounded
home-kingdom occupation row. Add a helper that constructs the shared city facts
and term:

```csharp
private static void AddFrozenOccupationCandidate(
    List<WarPeaceDefaultTermCandidate> result, City city, Kingdom payer,
    Kingdom recipient, int priority)
{
    WarPeaceCityValueFacts facts = CityFacts(city, recipient.id, payer.id);
    result.Add(new WarPeaceDefaultTermCandidate(
        new WarPeaceSettlementTermDraft
        {
            Kind = WarPeaceTermKind.CedeCity,
            RequestedCost = WarPeaceTermsRules.CityCessionCost(facts),
            FromKingdomId = payer.id,
            ToKingdomId = recipient.id,
            CityId = city.id
        }, false, priority, true));
}
```

Add the controller term at priority 76. If `beneficiary` is the main attacker or
main defender on the controller side and has a different ID, add the leader
term at priority 75. Add the city ID to the territorial-basis dedupe set only
after both alternatives are emitted.

- [ ] **Step 4: Run candidate and settlement tests and verify GREEN**

Run both commands from Step 2. Expected: both pass and AI selects the priority
76 controller candidate for a duplicated city.

- [ ] **Step 5: Commit the candidate slice**

```powershell
git add -- Code/core/lineage/WarPeaceSettlementRuntime.cs Tests/WarPeaceCandidateEligibilitySourceGuard.ps1 Tests/WarPeaceSettlementServiceTests.cs
git commit -m "feat: offer occupied cities to ally or war leader"
```

### Task 3: Validate And Execute The Selected Recipient

**Files:**
- Modify: `Code/core/lineage/WarPeaceSettlementRuntime.cs:290-425,940-1020,1347-1380,1880-1930`
- Modify: `Tests/WarPeaceIntegrationTests.ps1:647-703`
- Modify: `Tests/WarPeaceSettlementServiceTests.cs`

- [ ] **Step 1: Write failing execution tests**

Add cases proving that a term to the actual controller and a term to its
same-side war leader both materialize, while a term to an ordinary ally fails
with `no_territorial_basis`. Add a projected-owner case where the live owner is
the recorded controller and the chosen recipient is its war leader.

- [ ] **Step 2: Run settlement and integration tests and verify RED**

```powershell
dotnet run --project Tests/WarPeaceSettlementServiceTests.csproj --no-restore
& Tests/WarPeaceIntegrationTests.ps1
```

Expected: the war-leader recipient fails because current validation calls
`HasFrozenOccupation(warId, cityId, toId)` and requires the recipient to be the
exact controller.

- [ ] **Step 3: Implement authoritative recipient resolution**

Add a bounded helper that reads the source kingdom's frozen rows, finds the
city row, resolves the controller and verifies:

```csharp
recipientIsController ||
recipientIsWarLeader && recipientSide == control.ControllerSide
```

Return the controller ID to validation. Use it in `TryValidateTerm`, execution
baseline capture, and `TryCedeCity`. Call the six-fact pure execution rule with
`ownerId == controllerId`. Preserve core/claim cessions by requiring their live
owner to remain the source kingdom.

- [ ] **Step 4: Run settlement and integration tests and verify GREEN**

Run both commands from Step 2. Expected: both pass; third-party recipients and
owners remain rejected.

- [ ] **Step 5: Commit the execution slice**

```powershell
git add -- Code/core/lineage/WarPeaceSettlementRuntime.cs Tests/WarPeaceIntegrationTests.ps1 Tests/WarPeaceSettlementServiceTests.cs
git commit -m "fix: execute war leader occupation cessions"
```

### Task 4: Make Same-City Recipient Choices Mutually Exclusive

**Files:**
- Modify: `Code/ui/windows/WarPeaceNegotiationPresentation.cs:345-400,549-620`
- Modify: `Code/ui/windows/WarPeaceNegotiationController.cs:575-615`
- Modify: `Code/ui/windows/WarPeaceNegotiationWindow.cs:417-465`
- Modify: `Tests/WarPeaceNegotiationPresentationTests.ps1`
- Modify: `Tests/WarPeaceNegotiationWindowTests.ps1`

- [ ] **Step 1: Write failing presentation and window tests**

Add `CityId` to cession presentations and test a pure conflict rule:

```csharp
WarPeaceRecipientChoiceRules.Conflicts(
    cedeToController, cedeToLeader) == true;
WarPeaceRecipientChoiceRules.Conflicts(
    cedeToController, anotherCity) == false;
```

Add a source assertion requiring `OnTermChanged` to call
`RemoveOtherCityRecipientTerms(term.Id)` before adding the selected ID.

- [ ] **Step 2: Run UI tests and verify RED**

```powershell
& Tests/WarPeaceNegotiationPresentationTests.ps1
& Tests/WarPeaceNegotiationWindowTests.ps1
```

Expected: FAIL because presentations do not carry `CityId` and the window does
not remove an alternative recipient.

- [ ] **Step 3: Implement presentation identity and toggle exclusion**

Add `CityId` with `-1` defaults to existing constructors, pass `term.CityId`
from `BuildTermPresentations`, and add:

```csharp
public static bool Conflicts(WarPeaceTermPresentation left,
    WarPeaceTermPresentation right)
{
    return left?.Kind == WarPeaceTermKind.CedeCity &&
           right?.Kind == WarPeaceTermKind.CedeCity &&
           left.CityId >= 0 && left.CityId == right.CityId &&
           !string.Equals(left.Id, right.Id, StringComparison.Ordinal);
}
```

In the window, remove every selected term for which `Conflicts(selected,
other)` is true before adding the newly selected term. Keep the existing
server-side duplicate-city rejection unchanged. Confirm `BuildTermDetail`
continues displaying `source -> recipient: city` for both alternatives.

- [ ] **Step 4: Run UI tests and verify GREEN**

Run both commands from Step 2. Expected: both pass and the two terms remain
visually distinct by recipient realm.

- [ ] **Step 5: Commit the UI slice**

```powershell
git add -- Code/ui/windows/WarPeaceNegotiationPresentation.cs Code/ui/windows/WarPeaceNegotiationController.cs Code/ui/windows/WarPeaceNegotiationWindow.cs Tests/WarPeaceNegotiationPresentationTests.ps1 Tests/WarPeaceNegotiationWindowTests.ps1
git commit -m "feat: choose recipient for allied occupations"
```

### Task 5: Full Verification And Deployment

**Files:**
- Deploy scoped runtime files to `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0/`

- [ ] **Step 1: Run all affected tests**

```powershell
dotnet run --project Tests/WarPeaceSettlementServiceTests.csproj --no-restore
& Tests/WarPeaceCandidateEligibilitySourceGuard.ps1
& Tests/WarPeaceCandidateOrderingSourceGuard.ps1
& Tests/WarPeaceNegotiationPresentationTests.ps1
& Tests/WarPeaceNegotiationWindowTests.ps1
& Tests/WarPeaceIntegrationTests.ps1
```

Expected: all six commands exit 0.

- [ ] **Step 2: Build the source project**

```powershell
dotnet build AncientWarfare3.csproj -c Debug --no-restore
```

Expected: 0 warnings, 0 errors.

- [ ] **Step 3: Deploy only the five changed runtime files**

Copy these paths while the game is stopped:

```text
Code/core/lineage/WarPeaceSettlementValidationRules.cs
Code/core/lineage/WarPeaceSettlementRuntime.cs
Code/ui/windows/WarPeaceNegotiationPresentation.cs
Code/ui/windows/WarPeaceNegotiationController.cs
Code/ui/windows/WarPeaceNegotiationWindow.cs
```

- [ ] **Step 4: Build and hash-check the installed mod**

Run the installed `AncientWarfare3.csproj` build, then compare SHA-256 hashes
for all five deployed files. Expected: build exits 0 with no warnings/errors
and every source/install hash matches.

- [ ] **Step 5: Perform in-game acceptance**

Open a coalition war where an ally controls an enemy city. Verify two rows show
the same city with different recipients, selecting either clears the other,
both can be submitted separately, and the accepted treaty transfers the city
to the selected recipient.

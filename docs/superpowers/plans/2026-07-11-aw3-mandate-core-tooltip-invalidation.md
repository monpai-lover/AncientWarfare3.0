# AW3 Mandate Core Tooltip Invalidation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Recompute Mandate legal-core control immediately after a current legal-core city changes ownership.

**Architecture:** Keep the existing live hover pipeline. Add one pure invalidation rule and a narrow `MandateService` transfer hook that dirties the cached dynamic report and active Mandate core map only for relevant legal-core transfers. Wire it into the existing post-transfer Harmony path.

**Tech Stack:** C# 11, .NET Framework 4.8, Harmony, WorldBox city ownership hooks, existing focused executable tests.

---

## File Map

- Create `Code/core/lineage/MandateCoreTransferRules.cs`: pure decision for whether a transfer can invalidate Mandate core totals.
- Modify `Verification/AW3FocusedRuleTests/Program.cs`: regression coverage for legal-core and irrelevant transfers.
- Modify `Code/core/lineage/MandateService.cs`: invalidate an already-cached report and active core map without scanning or querying on transfer.
- Modify `Code/patch/AW_ChroniclePatch.cs`: notify Mandate state after `City.setKingdom` completes.

### Task 1: Add the legal-core transfer invalidation rule with TDD

**Files:**
- Create: `Code/core/lineage/MandateCoreTransferRules.cs`
- Modify: `Verification/AW3FocusedRuleTests/Program.cs`

- [ ] **Step 1: Write the failing focused test**

Add the call in `Main()`:

```csharp
ExpectMandateCoreTransferInvalidation();
```

Add the test method:

```csharp
private static void ExpectMandateCoreTransferInvalidation()
{
    if (!MandateCoreTransferRules.ShouldInvalidate(true, true) ||
        MandateCoreTransferRules.ShouldInvalidate(false, true) ||
        MandateCoreTransferRules.ShouldInvalidate(true, false))
        throw new Exception("Only a current legal-core transfer may invalidate Mandate control totals.");
}
```

- [ ] **Step 2: Run the test and verify RED**

Run:

```powershell
dotnet run --project Verification/AW3FocusedRuleTests/AW3FocusedRuleTests.csproj
```

Expected: compilation fails because `MandateCoreTransferRules` does not exist.

- [ ] **Step 3: Add the minimal pure rule**

Create `Code/core/lineage/MandateCoreTransferRules.cs`:

```csharp
namespace AncientWarfare3.core.lineage
{
    public static class MandateCoreTransferRules
    {
        public static bool ShouldInvalidate(bool pHasCurrentPeriod, bool pIsLegalCore)
        {
            return pHasCurrentPeriod && pIsLegalCore;
        }
    }
}
```

- [ ] **Step 4: Run the test and verify GREEN**

Run:

```powershell
dotnet run --project Verification/AW3FocusedRuleTests/AW3FocusedRuleTests.csproj
```

Expected: `AW3 focused rule tests passed.`

- [ ] **Step 5: Commit the tested rule**

```powershell
git add -- Code/core/lineage/MandateCoreTransferRules.cs Verification/AW3FocusedRuleTests/Program.cs
git commit -m "test: cover Mandate core transfer invalidation"
```

### Task 2: Invalidate cached Mandate totals after a relevant transfer

**Files:**
- Modify: `Code/core/lineage/MandateService.cs`
- Modify: `Code/patch/AW_ChroniclePatch.cs`

- [ ] **Step 1: Add the runtime invalidation boundary**

Add this method next to `OnKingdomCoreCreated` in `MandateService`:

```csharp
public static void OnCityTransferred(City pCity)
{
    if (pCity?.data == null || pCity.isRekt()) return;
    if (_cacheDirty || _cachedReport == null) return;
    if (!MandateCoreTransferRules.ShouldInvalidate(
            _cachedReport.period_id >= 0, _coreCityIds.Contains(pCity.id))) return;

    MarkDirty();
    MandateCoreMapModeService.DirtyMapIfActive();
}
```

This deliberately does not call `ReadReport()`: if the report is already dirty,
there is nothing to invalidate; if it is clean, `_coreCityIds` is the matching
current-period cache. The next consumer performs one fresh dynamic recomputation.

- [ ] **Step 2: Notify Mandate state after ownership changes**

In `AW_ChroniclePatch.CitySetKingdom_Postfix`, after the existing transfer service calls, add:

```csharp
MandateService.OnCityTransferred(__instance);
```

The existing `if (pFromLoad) return;` keeps save loading out of this runtime path.

- [ ] **Step 3: Run focused tests**

Run:

```powershell
dotnet run --project Verification/AW3FocusedRuleTests/AW3FocusedRuleTests.csproj
```

Expected: `AW3 focused rule tests passed.`

- [ ] **Step 4: Build the complete mod**

Run:

```powershell
dotnet build AncientWarfare3.csproj --no-restore
```

Expected: build succeeds with zero warnings and zero errors.

- [ ] **Step 5: Check diff and ownership boundaries**

Run:

```powershell
git diff --check
git status --short
```

Expected: no whitespace errors; user-owned `Tests/*` deletions remain unstaged and untouched.

- [ ] **Step 6: Commit the runtime fix**

```powershell
git add -- Code/core/lineage/MandateService.cs Code/patch/AW_ChroniclePatch.cs
git commit -m "fix: refresh Mandate core control after city transfer"
```

### Task 3: Final verification

**Files:**
- Modify only if verification exposes a defect in the files listed above.

- [ ] **Step 1: Re-run focused verification after commits**

```powershell
dotnet run --project Verification/AW3FocusedRuleTests/AW3FocusedRuleTests.csproj
```

Expected: `AW3 focused rule tests passed.`

- [ ] **Step 2: Rebuild after commits**

```powershell
dotnet build AncientWarfare3.csproj --no-restore
```

Expected: zero warnings and zero errors.

- [ ] **Step 3: Record the in-game acceptance boundary**

In the Mandate core map, first hover while the Mandate holder controls 60 percent,
then transfer all remaining active legal-core cities to that holder. On the next
hover, both the dynasty-wide core count and control ratio must show the live total
and 100 percent. The CLI build cannot execute this Unity interaction.


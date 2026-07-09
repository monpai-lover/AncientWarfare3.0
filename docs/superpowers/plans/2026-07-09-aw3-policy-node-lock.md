# AW3 Policy Node Lock Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a per-kingdom player lock for any AW3 policy, tech, or decision node so player and AI cannot select locked nodes.

**Architecture:** Store locked node ids on kingdom data as a semicolon set. Add pure rule helpers for tests, expose lock APIs from `KingdomPolicyService`, and check them at every research, decision, queue, AI, and dedicated core-fabrication entry point. Extend `KingdomPolicyWindow` nodes with a small lock toggle and localized tooltips.

**Tech Stack:** C# mod code, Unity UI, existing AW3 `KingdomPolicyService`, `KingdomPolicyDefs`, `KingdomPolicyWindow`, and `WarFabricationRuleTests`.

---

### Task 1: Rule Tests And Lock Helper

**Files:**
- Create: `Code/core/policy/PolicyNodeLockRules.cs`
- Modify: `Tests/WarFabricationRuleTests/Program.cs`

- [ ] **Step 1: Write failing tests**

Add a test method in `Program.cs` that calls a pure rule class before it exists:

```csharp
private static void ExpectPolicyNodeLockRules()
{
    if (!PolicyNodeLockRules.IsLocked("aw_policy_slave_army;aw_tech_city_defense", "aw_policy_slave_army"))
        throw new Exception("Expected node to be locked.");
    if (PolicyNodeLockRules.IsLocked("aw_policy_slave_army;aw_tech_city_defense", "aw_policy_name_integration"))
        throw new Exception("Expected unrelated node to be unlocked.");
    if (PolicyNodeLockRules.ShouldAllowStart("aw_decision_claim_mandate", "aw_decision_claim_mandate"))
        throw new Exception("Expected locked decision start to be rejected.");
    if (!PolicyNodeLockRules.ShouldAllowStart("aw_decision_claim_mandate", "aw_decision_year_name"))
        throw new Exception("Expected unlocked decision start to be allowed.");
    if (!PolicyNodeLockRules.ShouldClearCurrent("aw_tech_city_defense", "aw_tech_city_defense"))
        throw new Exception("Expected matching current node to be cleared.");
    if (PolicyNodeLockRules.ShouldClearCurrent("aw_tech_city_defense", "aw_tech_writing"))
        throw new Exception("Expected different current node to remain.");
    if (!PolicyNodeLockRules.ShouldClearCoreFabrication("aw_decision_fabricate_core"))
        throw new Exception("Expected core fabrication lock to clear dedicated slot.");
    if (PolicyNodeLockRules.ShouldClearCoreFabrication("aw_decision_fabricate_weak_claim"))
        throw new Exception("Expected weak claim lock not to clear core fabrication.");
}
```

Call it from the existing main test runner.

- [ ] **Step 2: Verify RED**

Run:

```powershell
dotnet run --project .\Tests\WarFabricationRuleTests\WarFabricationRuleTests.csproj
```

Expected: compile failure because `PolicyNodeLockRules` does not exist.

- [ ] **Step 3: Implement minimal helper**

Create `PolicyNodeLockRules.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.policy
{
    public static class PolicyNodeLockRules
    {
        public const string CoreFabricationDecisionId = "aw_decision_fabricate_core";

        public static bool IsLocked(string lockedRaw, string nodeId)
        {
            if (string.IsNullOrEmpty(lockedRaw) || string.IsNullOrEmpty(nodeId)) return false;
            foreach (string part in lockedRaw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                if (part == nodeId) return true;
            return false;
        }

        public static string SetLocked(string lockedRaw, string nodeId, bool locked)
        {
            var set = new HashSet<string>(Split(lockedRaw));
            if (string.IsNullOrEmpty(nodeId)) return string.Join(";", set);
            if (locked) set.Add(nodeId);
            else set.Remove(nodeId);
            return string.Join(";", set);
        }

        public static bool ShouldAllowStart(string lockedRaw, string nodeId)
        {
            return !IsLocked(lockedRaw, nodeId);
        }

        public static bool ShouldClearCurrent(string lockedNodeId, string currentNodeId)
        {
            return !string.IsNullOrEmpty(lockedNodeId) && lockedNodeId == currentNodeId;
        }

        public static bool ShouldClearCoreFabrication(string lockedNodeId)
        {
            return lockedNodeId == CoreFabricationDecisionId;
        }

        private static IEnumerable<string> Split(string raw)
        {
            if (string.IsNullOrEmpty(raw)) yield break;
            foreach (string part in raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                yield return part;
        }
    }
}
```

- [ ] **Step 4: Verify GREEN**

Run the same `dotnet run` command. Expected: pass.

### Task 2: Service Storage And Cleanup

**Files:**
- Modify: `Code/core/lineage/LineageKeys.cs`
- Modify: `Code/core/policy/KingdomPolicyService.cs`
- Modify: `Code/core/db/KingdomPolicyStateTableItem.cs`

- [ ] **Step 1: Add failing tests for raw set behavior**

Extend `ExpectPolicyNodeLockRules()` with:

```csharp
string locked = PolicyNodeLockRules.SetLocked("", "aw_policy_slave_army", true);
if (!PolicyNodeLockRules.IsLocked(locked, "aw_policy_slave_army"))
    throw new Exception("Expected SetLocked to add node.");
locked = PolicyNodeLockRules.SetLocked(locked, "aw_policy_slave_army", false);
if (PolicyNodeLockRules.IsLocked(locked, "aw_policy_slave_army"))
    throw new Exception("Expected SetLocked false to remove node.");
```

- [ ] **Step 2: Verify RED**

If `SetLocked` already passes from Task 1, add no new production code in this sub-step and treat this as regression coverage.

- [ ] **Step 3: Add kingdom storage API**

Add `LineageKeys.POLICY_LOCKED_NODES`, `KingdomPolicySnapshot.locked_nodes`, snapshot DB column `locked_nodes`, and service methods:

```csharp
public static string GetLockedNodesRaw(Kingdom pKingdom)
public static bool IsNodeLocked(Kingdom pKingdom, string pNodeId)
public static bool SetNodeLocked(Kingdom pKingdom, string pNodeId, bool pLocked)
public static bool ToggleNodeLocked(Kingdom pKingdom, string pNodeId)
private static void CleanLockedNodeSideEffects(Kingdom pKingdom, string pNodeId)
```

Cleanup must clear current tech/social/decision, normal decision queue, and core fabrication slot/queue when applicable.

- [ ] **Step 4: Verify**

Run:

```powershell
dotnet run --project .\Tests\WarFabricationRuleTests\WarFabricationRuleTests.csproj
dotnet build .\AncientWarfare3.csproj
```

Expected: pass.

### Task 3: Runtime Enforcement

**Files:**
- Modify: `Code/core/policy/KingdomPolicyService.cs`
- Modify: `Code/core/policy/KingdomPolicyAI.cs`

- [ ] **Step 1: Add failing tests for start gating rules**

Add pure coverage in `ExpectPolicyNodeLockRules()`:

```csharp
if (!PolicyNodeLockRules.ShouldAllowStart("", "aw_decision_declare_war"))
    throw new Exception("Expected empty lock set to allow start.");
if (PolicyNodeLockRules.ShouldAllowStart("aw_decision_declare_war", "aw_decision_declare_war"))
    throw new Exception("Expected locked war decision to be rejected.");
```

- [ ] **Step 2: Verify RED or regression**

Run `dotnet run --project .\Tests\WarFabricationRuleTests\WarFabricationRuleTests.csproj`.

- [ ] **Step 3: Add enforcement**

Add `IsNodeLocked(...)` checks to:

- `GetStatus()`
- `StartResearch()`
- `ForceStartResearch()`
- `StartDecisionWithTarget()`
- `StartFabricationDecision()`
- `StartWarDecision()`
- `StartCoreFabrication()`
- `TryStartCoreFabrication()`
- `AdvanceCoreFabrication()`
- `StartNextQueuedDecisionIfEmpty()`
- `StartNextQueuedCoreFabrication()`
- `CanStartQueuedDecision()`
- `KingdomPolicyAI.PickDecision()`
- `KingdomPolicyAI.PickResearch()`

- [ ] **Step 4: Verify**

Run tests and build. Expected: pass.

### Task 4: UI Lock Toggle

**Files:**
- Modify: `Code/ui/windows/KingdomPolicyWindow.cs`

- [ ] **Step 1: Add UI helper with no backend changes**

Add a compact lock toggle in `BuildNode()` after node creation:

```csharp
AddNodeLockToggle(box.transform, pKingdom, pDef);
```

The helper creates a small button in the upper right of the node, calls `KingdomPolicyService.ToggleNodeLocked`, and refreshes the window.

- [ ] **Step 2: Ensure node click remains independent**

The lock toggle must have its own `Button` and `TipButton`. The main node button still starts or force-switches research.

- [ ] **Step 3: Add locked node visual**

Use `KingdomPolicyService.IsNodeLocked(pKingdom, pDef.Id)` to grey the node and append localized lock status to the tooltip.

- [ ] **Step 4: Verify**

Build the project. Expected: pass.

### Task 5: Localization And Core Sidebar Text

**Files:**
- Modify: `Locales/aw3_policy_ui.csv` or another existing policy localization csv.
- Modify: `Code/ui/windows/KingdomPolicyWindow.cs`

- [ ] **Step 1: Add localization keys**

Add these keys to the policy localization csv with Chinese, English, and Traditional Chinese values matching the listed meanings:

```text
aw_policy_node_locked: Locked
aw_policy_node_locked_by_player: Locked by player
aw_policy_lock_node: Lock Node
aw_policy_unlock_node: Unlock Node
aw_policy_lock_node_desc: Player and AI cannot select this node
aw_policy_unlock_node_desc: Restore normal selection for this node
aw_policy_locked_count: Locked Nodes
aw_policy_core_fabrication_locked: Core fabrication is locked
```

- [ ] **Step 2: Use localization in tooltips**

Use the keys in node tooltip and core fabrication sidebar.

- [ ] **Step 3: Verify**

Run build and `rg -n "aw_policy_node_locked|aw_policy_core_fabrication_locked" Locales Code`.

### Task 6: Final Verification And Commit

**Files:**
- All modified feature files.

- [ ] **Step 1: Run focused tests**

```powershell
dotnet run --project .\Tests\WarFabricationRuleTests\WarFabricationRuleTests.csproj
```

Expected: pass.

- [ ] **Step 2: Run build**

```powershell
dotnet build .\AncientWarfare3.csproj
```

Expected: 0 errors.

- [ ] **Step 3: Check diff**

```powershell
git diff --stat
git status --short
```

Expected: policy lock files are modified; pre-existing vassal support dirty files may still appear and must not be reverted.

- [ ] **Step 4: Commit relevant implementation files only**

Commit policy lock files separately from pre-existing dirty vassal files.

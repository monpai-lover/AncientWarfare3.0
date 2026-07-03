# AW3 Policy Tree Lines Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a larger first-pass policy/technology tree and draw dependency lines between policy nodes.

**Architecture:** Keep policy content in `KingdomPolicyDefs.cs`, using existing `RequiredPolicies`, `RequiredTechs`, `Column`, and `Row` fields as the single source of truth. Extend `KingdomPolicyWindow.cs` to draw a non-interactive background line layer from dependency edges before node buttons are created.

**Tech Stack:** C#/.NET Framework 4.8 mod code, Unity UI `Image`/`RectTransform`, NML window APIs, CSV locale files.

---

### Task 1: Add Policy Content

**Files:**
- Modify: `Code/content/policies/KingdomPolicyDefs.cs`
- Modify: `Locales/aw3_policy_ui.csv`

- [ ] Add six technology nodes: pottery casting, well-field surveying, chariot training, city defenses, granary accounting, rites and music.
- [ ] Add seven social policy nodes: household registration, corvee labor, noble council, ancestral rites, military merit ranks, border enfeoffment, early law code.
- [ ] Keep dependencies in `RequiredPolicies` and `RequiredTechs`; do not create a separate edge table.
- [ ] Add matching Chinese, English, and Traditional Chinese text for every new `NameKey` and `DescKey`.

### Task 2: Draw Dependency Lines

**Files:**
- Modify: `Code/ui/windows/KingdomPolicyWindow.cs`

- [ ] Add a line rendering pass before nodes in research mode.
- [ ] Resolve every required policy/tech into a source node and target node.
- [ ] Draw lines behind buttons with `Image` rectangles using section offsets, columns, rows, and node dimensions.
- [ ] Color lines by target node status: completed green, current/available gold, locked gray.

### Task 3: Verify

**Files:**
- Check: `Code/content/policies/KingdomPolicyDefs.cs`
- Check: `Code/ui/windows/KingdomPolicyWindow.cs`
- Check: `Locales/aw3_policy_ui.csv`

- [ ] Run source assertions for new policy ids, locale keys, and line drawing method.
- [ ] Run `dotnet build` with `DOTNET_ROLL_FORWARD=Major`.

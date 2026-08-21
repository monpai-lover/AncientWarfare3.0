# 逐鹿战争首都与州府突破扩张 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task with verification checkpoints.

**Goal:** 在逐鹿战争中占领敌方首都或法理州府后，自动把符合条件的敌对参战国城市转移给占领方，加快战争推进而不改变普通战争与 RTS 链。

**Architecture:** 新增纯规则层负责候选筛选和触发判定，新增运行时服务负责读取逐鹿战争、解析法理州/邻接城市、幂等记录并调用现有 `City.joinAnotherKingdom` 转移链。Harmony 只在 `finishCapture` 成功后调用服务；普通战争直接返回，不进入新逻辑。

**Tech Stack:** C#/.NET Framework 4.8, Harmony, WorldBox `City`/`War`, 现有 `DeJureRegionStore`, `WarTerritoryService`, 数据字典持久化。

---

### Task 1: Add deterministic expansion rules and tests

**Files:**
- Create: `Code/core/lineage/ZhuluCapitalBreakthroughRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/ZhuluCapitalBreakthroughRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Write the failing rule tests** covering: active Zhulu only; capital or de jure seat triggers; ordinary city does not; hostile participant accepted; ally/same-side/neutral/unrelated owner rejected; union of region and direct-neighbor sets is deduplicated; neighbor-of-neighbor is excluded; repeat key is rejected.
- [ ] **Step 2: Run the focused test project and confirm the new type/methods fail to compile.**
- [ ] **Step 3: Implement minimal pure methods:** `ShouldTrigger(bool isZhulu, bool isCapital, bool isSeat, bool alreadyProcessed)`, `ShouldTransferCity(bool ownerIsEnemyParticipant, bool ownerIsAttacker, bool ownerIsFriendlyParticipant, bool ownerIsNeutral, bool eligibleCity)`, and `MergeCityIds(IEnumerable<long> regionIds, IEnumerable<long> neighborIds, long breakthroughCityId)`.
- [ ] **Step 4: Run the focused rules test and confirm all cases pass.**
- [ ] **Step 5: Commit the rules and tests.**

### Task 2: Add runtime expansion service and persistence key

**Files:**
- Modify: `Code/core/lineage/LineageKeys.cs`
- Create: `Code/core/lineage/ZhuluCapitalBreakthroughService.cs`

- [ ] **Step 1: Add a persisted per-war processed-key constant** storing `warId:breakthroughCityId` values on the `War.data` dictionary; include safe read, append, and duplicate checks.
- [ ] **Step 2: Implement `TryApplyAfterCapture(City pCapturedCity, Kingdom pNewKingdom)`:** find active Zhulu wars where the new owner is on the attacker side, resolve the declared defender, verify the captured city is the defender capital or the `DeJureRegionStore` seat, and skip if the key is already processed.
- [ ] **Step 3: Build the candidate set:** use the captured city’s de jure region member IDs; if it is the declared defender capital, append `capturedCity.neighbours_cities`; filter live non-stronghold cities whose current owner is a hostile Zhulu participant, excluding the new owner and friendly/allied participants.
- [ ] **Step 4: Transfer candidates one by one via `city.joinAnotherKingdom(pNewKingdom)` inside try/catch; continue after individual failures; mark the processed key only after the trigger has been evaluated so repeated Harmony callbacks cannot replay it.**
- [ ] **Step 5: Clear/dirty existing war and map caches after processing and log only actionable failures with `LogError` for unexpected service exceptions.**

### Task 3: Wire the post-capture hook without affecting ordinary wars

**Files:**
- Modify: `Code/patch/AW_CityOccupationAccelerationPatch.cs`

- [ ] **Step 1: In `FinishCapture_Postfix`, after the original capture has changed ownership, call `ZhuluCapitalBreakthroughService.TryApplyAfterCapture(__instance, __instance?.kingdom)`.**
- [ ] **Step 2: Guard the call so failed/blocked captures, rebellion-direct transfers, bandit strongholds, and ordinary wars do nothing.**
- [ ] **Step 3: Preserve the existing garrison, war director, rebellion, reserve, and settlement calls in their current order.**
- [ ] **Step 4: Add a source guard test that verifies the hook is post-capture and the service is not called from `updateCapture` or ordinary-war branches.**

### Task 4: Compile, focused verification, and deploy

**Files:**
- Modify only if compilation reveals missing project links: `AncientWarfare3.csproj`, `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Run `dotnet build AncientWarfare3.csproj -c Debug --no-restore`.**
- [ ] **Step 2: Run `dotnet build AncientWarfare3.csproj -c Release --no-restore`.**
- [ ] **Step 3: Run the focused rules/source-guard tests; record any pre-existing missing-source failures separately.**
- [ ] **Step 4: Run `deploy-local.ps1` and verify deployed DLL hashes match `bin/Release/net48/AncientWarfare3.dll`.**
- [ ] **Step 5: Review `git diff` and `git status`; do not stage unrelated court, bandit, school, localization, or map-mode changes.**
- [ ] **Step 6: Commit the implementation and push `origin master` only after the builds and focused checks pass.**

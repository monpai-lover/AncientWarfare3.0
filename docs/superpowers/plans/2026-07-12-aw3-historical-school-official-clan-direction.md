# AW3 Historical School, Official Clan, And Direction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make schools persistent personal identities, central offices male-only, official Shi/Clan inheritance consistent, and court composition a stable AI direction input.

**Architecture:** Separate personal identity from office state, keep all selection and weighting rules pure, invoke lineage creation only at appointment/promotion/birth events, and aggregate direction from bounded role caches.

**Tech Stack:** C# net48, WorldBox actor/Clan APIs, AW3 SQLite/history services, temporary net9 rule harness.

---

## File Structure

- Create `Code/core/court/CourtSchoolIdentityRules.cs`: event-time personal-school selection.
- Modify `Code/core/court/CourtSchoolAssignmentRules.cs`: compatibility scoring only.
- Modify `Code/core/court/CourtService.cs`: preserve school and validate central gender.
- Modify `Code/core/court/CourtRules.cs`: central male eligibility rule.
- Modify `Code/core/court/CourtDirectionService.cs`: no fabricated schools.
- Modify `Code/core/court/CourtReadModelService.cs`: no Ru/Military fallbacks.
- Create `Code/core/lineage/OfficialShiRules.cs`: pure official grant/reuse decisions.
- Modify `Code/core/lineage/LineageService.cs`: `EnsureOfficialShiAndClan` and birth-visible Clan sync.
- Modify promotion patches for central officer, leader, and general events.
- Modify biography/history writers to persist office tenure.
- Modify `F:/tmp/AW3CourtExpansionRuleTests/*`: regression harness.

### Task 1: Failing Identity And Eligibility Tests

- [ ] Add tests proving existing schools survive transfer/dismissal, no-school remains empty, office changes only compatibility score, central women are ineligible, and leaders/generals remain gender-neutral.
- [ ] Add `OfficialShiRules` tests for reuse, grant, matching-descendant sync, and distinct-branch preservation.
- [ ] Run the court harness and verify missing types/signatures fail compilation.

### Task 2: Persistent Personal School

- [ ] Implement `CourtSchoolIdentityRules.Resolve` with existing school, parental school, city influence, attributes, and deterministic jitter; allow an empty result.
- [ ] Change `CourtSchoolAssignmentRules` to return candidate compatibility bonuses and stop treating office ID as a forced school.
- [ ] Remove `COURT_SCHOOL` clearing and school-trait removal from `CourtService.ClearOfficer`.
- [ ] Assign a school only on first political entry; synchronize its presentation trait without making the trait authoritative.
- [ ] Remove office/stat fallback school fabrication from `CourtDirectionService` and Ru/Military defaults from `CourtReadModelService`.
- [ ] Run the court harness and commit with `git commit -m "fix: persist personal school identity"`.

### Task 3: Central Gender Rules

- [ ] Add `CanHoldLayerOffice(string layer, bool isMale, bool otherwiseEligible)` returning `isMale && otherwiseEligible` only for `CourtOfficeLayer.Central`.
- [ ] Apply it in central candidate filtering, appointment, and annual roster validation.
- [ ] Keep king, heir, governor, general, and city roles outside the restriction.
- [ ] Run tests and commit with `git commit -m "fix: enforce male central offices"`.

### Task 4: Official Shi And Visible Clan

- [ ] Add failing boundary tests for the official-title roll (`0` and `19` use the title; `20` and `99` use the word library), every known office ID, unknown-office fallback, and preservation of an existing Shi.
- [ ] Implement pure decisions in `OfficialShiRules`: reuse inherited Shi+Clan, grant `official_grant` when absent, resolve canonical office-title Shi names, and synchronize only matching branch descendants.
- [ ] Add `LineageService.EnsureOfficialShiAndClan(Actor, string officeId)` using existing ID allocation, branch insertion, `ClanManager.newClan`, and `RenameClanByLeader` patterns. Only a Shi-less first appointment rolls: 20 percent office title and 80 percent `LineageNamePool.RandomShi()`.
- [ ] Call it from successful central appointment, city-leader promotion, and general promotion.
- [ ] After AW3 chooses the patrilineal birth source, join the child to the same visible Clan instead of retaining vanilla random parent Clan choice.
- [ ] Bound late descendant synchronization at 512 and preserve distinct branches.
- [ ] Run harness and build; commit with `git commit -m "feat: grant officials inheritable Shi clans"`.

### Task 5: Office History And Family Tree Data

- [ ] Route appointment, transfer, dismissal, governor, and general events through one office-tenure writer.
- [ ] Store actor, kingdom, office/layer, city, start/end time, and end reason.
- [ ] Read current office in family-tree nodes and complete tenure history in biography.
- [ ] Verify duplicate appointment events are idempotent.
- [ ] Build and commit with `git commit -m "feat: record official careers"`.

### Task 6: Stable National Direction

- [ ] Make every school definition expose livelihood, war, aggression, peace, order, commerce, and technology components.
- [ ] Aggregate king, heir, officers, leaders, and generals from bounded caches; count one actor once at their highest role weight.
- [ ] Add smoothing/hysteresis so small roster changes do not flip direction.
- [ ] Feed cached direction into existing research, diplomacy, war, vassal, and peace scoring without forcing decisions.
- [ ] Run direction tests, court harness, and both builds.
- [ ] Commit with `git commit -m "feat: steer kingdoms from historical court factions"`.

### Task 7: Verification

- [ ] Verify no `pKingdom.getUnits()` or world actor scan was added to annual court work.
- [ ] In game, appoint/dismiss/transfer officers and verify school persists.
- [ ] Verify a central woman is rejected while a female leader/general remains valid.
- [ ] Promote a Shi-less official, inspect visible Clan, create descendants, and verify patrilineal consistency.
- [ ] Verify family tree office and biography tenure history.
- [ ] Let AI kingdoms run and confirm direction biases decisions without yearly oscillation.

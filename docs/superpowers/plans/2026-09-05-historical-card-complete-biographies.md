# Historical Card Complete Biographies Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace every generic historical-card biography with a card-specific, three-sentence Chinese biography and derive each background summary from that same curated text.

**Architecture:** Keep biography data separate from card construction in six era-specific C# dictionaries joined by one duplicate-safe aggregator. Route monarch and minister summary/detail generation through the aggregator, preserve dynamic supporter biographies, and enforce complete runtime-catalogue coverage with executable rules tests.

**Tech Stack:** C# 11/.NET Framework 4.8, source-linked .NET 9 rules tests, existing historical-card catalogue and Unity UI.

---

### Task 1: Add Complete-Coverage Tests

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardCatalogRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] Add assertions that every non-supporter card has a card-ID-specific biography, at least three sentences and 60 characters, a specific first-sentence background, and none of the banned generic fragments.
- [ ] Assert same-name identity pairs such as `three_liu_yu`/`song_wudi` and `sui_an_lushan`/`an_lushan` resolve to different text.
- [ ] Run the focused historical-card executable and verify RED with the audited baseline of 623 generic biographies.

### Task 2: Add Biography Resolver

**Files:**
- Create: `Code/content/figures/HistoricalFigureCardCuratedBiographies.cs`
- Modify: `Code/content/figures/HistoricalFigureCardNarratives.cs`
- Modify: `Code/content/figures/HistoricalFigureCardCatalog.cs`
- Modify: `Code/content/figures/HistoricalFigureCardMinisterSeeds.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] Add duplicate-safe aggregation, `TryGet`, `Contains`, and first-sentence extraction.
- [ ] Route monarch and minister background/detail generation through the resolver and remove deployment instructions from historical detail text.
- [ ] Make catalogue validation reject non-supporter cards without curated biography coverage.
- [ ] Run tests and verify failures now report only missing data IDs.

### Task 3: Populate Pre-Qin And Qin

**Files:**
- Create: `Code/content/figures/HistoricalFigureCardBiographiesPreQin.cs`

- [ ] Add separate three-sentence entries for every missing Xia, Shang, Zhou, Spring and Autumn, Warring States, and Qin card ID.
- [ ] Run the catalogue audit and verify this period has zero missing or generic entries.

### Task 4: Populate Han

**Files:**
- Create: `Code/content/figures/HistoricalFigureCardBiographiesHan.cs`

- [ ] Add separate entries for every missing Western Han, Xin, Eastern Han, and late-Han card identity.
- [ ] Run the catalogue audit and verify the Han period has zero missing or generic entries.

### Task 5: Populate Three Kingdoms Through Northern And Southern Dynasties

**Files:**
- Create: `Code/content/figures/HistoricalFigureCardBiographiesThreeSix.cs`

- [ ] Add separate entries for every missing Three Kingdoms, Jin, Sixteen Kingdoms, Northern Dynasties, and Southern Dynasties identity.
- [ ] Keep monarch and minister versions of the same person distinct.
- [ ] Run the catalogue audit and verify the period has zero missing or generic entries.

### Task 6: Populate Sui And Tang

**Files:**
- Create: `Code/content/figures/HistoricalFigureCardBiographiesSuiTang.cs`

- [ ] Add separate entries for every missing Sui, Tang, Wu Zhou, rebellion, and transition-period identity.
- [ ] Run the catalogue audit and verify the period has zero missing or generic entries.

### Task 7: Populate Five Dynasties, Ten Kingdoms, Song, Liao, Jin, And Western Xia

**Files:**
- Create: `Code/content/figures/HistoricalFigureCardBiographiesFiveSong.cs`

- [ ] Add separate entries for every missing card in this period, including short-lived rulers and military/civil ministers.
- [ ] Run the catalogue audit and verify the period has zero missing or generic entries.

### Task 8: Populate Yuan, Ming, Qing, And Author Card

**Files:**
- Create: `Code/content/figures/HistoricalFigureCardBiographiesYuanMingQing.cs`

- [ ] Add separate entries for every missing Yuan, Ming, Southern Ming, Qing, transition, and `mengpai` identity.
- [ ] Run the catalogue audit and verify the period has zero missing or generic entries.

### Task 9: Full Audit And Delivery

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardCatalogRulesTests.cs.txt`
- Modify: `docs/superpowers/plans/2026-09-05-historical-card-complete-biographies.md`

- [ ] Run the runtime audit and require `MISSING_SPECIFIC=0` across all 708 cards.
- [ ] Run focused historical-card tests, the formal rules suite where available, `dotnet build AncientWarfare3.csproj --no-restore`, and `git diff --check`.
- [ ] Deploy with `.\deploy-local.ps1` and compare hashes for every biography source and resolver file.
- [ ] Commit, push the current b branch, and verify a clean worktree synchronized with origin.

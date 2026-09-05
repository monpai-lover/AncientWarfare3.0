# Historical Card Supporter Crate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `赞助者` historical-card crate sourced from `supporters.csv`, with one equally weighted pink minister card per distinct supporter and the existing shared gold pool.

**Architecture:** A shared supporter roster parser owns CSV parsing and runtime file loading. A focused supporter-card seed builder merges duplicate names and maps roster records into stable card definitions; the existing catalogue, draw service, deployment service, and crate UI then reuse their normal paths.

**Tech Stack:** C#/.NET, Unity UI, WorldBox mod APIs, CSV locale files, source-linked rules test executable.

---

### Task 1: Shared Supporter Roster

**Files:**
- Create: `Code/content/supporters/SupporterRosterData.cs`
- Modify: `Code/ui/SupporterLeaderboardData.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Create: `Tests/AncientWarfare3.Rules.Tests/SupporterRosterRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [x] **Step 1: Write failing parser tests**

Test quoted CSV values, malformed/blank names, rank normalization, and preservation of duplicate-name rows in the shared roster parser.

- [x] **Step 2: Run the focused test and verify RED**

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --supporter-roster`

Expected: compilation fails because `SupporterRosterData` does not exist.

- [x] **Step 3: Implement the shared parser and runtime loader**

Create `SupporterRosterEntry`, `SupporterRosterData.Parse(IEnumerable<string>)`, and `SupporterRosterData.Read()`. `Read()` resolves only `<mod folder>/supporters.csv`; missing or unreadable files return an empty roster. Replace the leaderboard's built-in roster and parser with a mapping from this shared source.

- [x] **Step 4: Run the focused test and verify GREEN**

Run the `--supporter-roster` command and expect `Supporter roster rules passed.`

### Task 2: Supporter Card Seeds And Crate Rules

**Files:**
- Create: `Code/content/figures/HistoricalFigureCardSupporterSeeds.cs`
- Modify: `Code/content/figures/HistoricalFigureCardCatalog.cs`
- Modify: `Code/content/figures/HistoricalFigureCardCrates.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Create: `Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardSupporterSeedTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardCrateRulesTests.cs.txt`

- [x] **Step 1: Write failing card-seed tests**

Assert case-insensitive duplicate merging, aggregated amount/date/contribution biography, order-independent SHA-256-based IDs, pink rarity, minister role, civil-official subtype, `supporters` collection, and one card per distinct current CSV name.

- [x] **Step 2: Run focused crate tests and verify RED**

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --historical-figure-card-crates`

Expected: failure because the supporter seed builder and seventh crate are absent.

- [x] **Step 3: Implement seed mapping and catalogue integration**

Build cards from `SupporterRosterData.Read()`, group names with `OrdinalIgnoreCase`, derive IDs from normalized names, aggregate unique record details, and use the standard portrait fallback. Append the generated cards in `HistoricalFigureCardCatalog.BuildAll()`.

- [x] **Step 4: Add the supporter crate**

Register crate id `supporters`, display name `赞助者`, description `赞助，你也可以进入游戏`, and a non-period year range that cannot absorb unrelated cards. Keep the existing shared-gold behavior unchanged.

- [x] **Step 5: Run focused tests and verify GREEN**

Run the crate test command and expect `Historical figure card crate rules passed.`

### Task 3: Uniform Draw And UI Contract

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardDrawRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardSourceGuardTests.cs.txt`
- Modify: `Code/ui/windows/HistoricalFigureDrawWindow.cs`
- Modify: `Locales/aw3_historical_cards.csv`
- Create: `GameResources/ui/historical_cards/crates/supporters.png`

- [x] **Step 1: Write failing draw and source-guard tests**

Assert every distinct supporter occupies one pink pool slot, fixed random indices can select every pink supporter, the shared gold pool is still used, selecting the supporter crate forces minister mode, role buttons are hidden in its contents, and detail source uses localized crate metadata.

- [x] **Step 2: Run draw/deployment guards and verify RED**

Run the draw and deployment test commands; expect the new supporter-specific assertions to fail.

- [x] **Step 3: Implement supporter UI behavior and localization**

Force `HistoricalFigureCardRole.Minister` in `SelectCrate("supporters")`, hide role switches while that crate is open, resolve detail collection through `HistoricalFigureCardCrates.Get`, and add Simplified Chinese, English, and Traditional Chinese crate strings.

- [x] **Step 4: Add crate artwork**

Create a dedicated `supporters.png` from an existing crate asset as the initial production-safe artwork so the crate never renders blank.

- [x] **Step 5: Run focused tests and verify GREEN**

Run `--supporter-roster`, `--historical-figure-card-draw`, `--historical-figure-card-crates`, and `--historical-figure-card-deployment`.

### Task 4: Integration, Deployment, And Push

**Files:**
- Modify: `docs/superpowers/plans/2026-09-05-historical-card-supporter-crate.md`

- [x] **Step 1: Build the main mod**

Run: `dotnet build AncientWarfare3.csproj --no-restore`

Expected: exit code 0.

- [x] **Step 2: Validate the patch**

Run: `git diff --check` and inspect `git status --short` plus the scoped diff.

- [x] **Step 3: Deploy and compare hashes**

Run: `.\deploy-local.ps1`, then compare SHA-256 for changed source, locale, supporter CSV, and crate image between repository and deployed mod.

- [x] **Step 4: Commit and push**

Commit the implementation and updated plan, then push `b/20260822-baseline-non-path-port` to origin.

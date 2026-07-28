# RTS War Plan PNG Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce authoritative RTS war-plan PNGs in each WorldBox save and expose the real mission state needed to diagnose idle Armies, missed friendly-city recovery, transport delays, and target handoff failures.

**Architecture:** Capture WorldBox objects into immutable DTOs on the main thread, fingerprint material plan state, then rasterize and write PNG/manifest artifacts on one bounded background worker. Pure projection, rendering, encoding, revision, and path rules remain testable without a running game; runtime adapters only translate indexed RTS state and lifecycle hooks.

**Tech Stack:** C# 9, Unity/WorldBox mod source, Harmony patches, pure RGBA rasterizer, pure PNG writer using stored zlib blocks, PowerShell source guards, .NET 9 rule-test harness.

---

## File Structure

- Create `Code/core/presentation/ArmyRtsPlanModels.cs`: immutable snapshot, map, city, Army, color, point, and artifact DTOs.
- Create `Code/core/presentation/ArmyRtsPlanRules.cs`: enable gate, canvas projection, style selection, stable fingerprint, revision suppression, and artifact paths.
- Create `Code/core/presentation/ArmyRtsPlanRasterizer.cs`: draw zones, borders, city/Army markers, solid/dashed arrows, arrowheads, and numeric labels into RGBA bytes.
- Create `Code/core/presentation/ArmyRtsPlanPngEncoder.cs`: encode RGBA bytes as a standards-compliant PNG without Unity calls.
- Create `Code/core/presentation/ArmyRtsPlanArtifactWriter.cs`: bounded latest-per-war worker, atomic file writes, staging, save publication, and shutdown.
- Create `Code/core/presentation/ArmyRtsPlanSnapshotService.cs`: main-thread live-state capture, pending revision scheduler, manifest formatting, and lifecycle coordination.
- Modify `Code/core/presentation/ArmyRtsVisualizationService.cs`: read `AWPerformanceSettings.ShowArmyRtsVisuals` directly so the existing setting actually controls the overlay.
- Modify `Code/patch/AW_ArmyRtsVisualizationPatch.cs`: process pending debug snapshots after the normal route overlay and clear them with the world.
- Modify `Code/patch/AW_WarPatch.cs`: request the initial plan after RTS war-start services and clear ended-war state.
- Modify `Code/core/lineage/ArmyRtsControllerService.cs`: notify the snapshot scheduler only when a mission materially changes.
- Modify `Code/patch/AW_SavePatch.cs`: observe loaded/saved directories and publish staged artifacts after a successful save.
- Modify `Code/ModClass.cs`: shut down the artifact worker.
- Create `Tests/AncientWarfare3.Rules.Tests/ArmyRtsPlanPngTests.cs.txt`: rule, raster, PNG, fingerprint, revision, and path tests.
- Modify `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`: link pure production files and compile the new tests.
- Modify `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`: add `--rts-plan-png-slice` and full-suite invocation.
- Create `Tests/ArmyRtsPlanPngSourceGuard.ps1`: guard authoritative hooks, setting wiring, save lifecycle, and absence of Unity access in the worker path.

### Task 1: Pure Plan And Revision Rules

**Files:**
- Create: `Code/core/presentation/ArmyRtsPlanModels.cs`
- Create: `Code/core/presentation/ArmyRtsPlanRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsPlanPngTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write failing style, projection, fingerprint, and revision tests**

Create tests that construct plain snapshots and assert:

```csharp
Equal(ArmyRtsPlanArrowStyle.Recovery,
    ArmyRtsPlanRules.ArrowStyle(defenseMission, friendlyOccupied: true,
        transportActive: false));
Equal(ArmyRtsPlanArrowStyle.Transport,
    ArmyRtsPlanRules.ArrowStyle(attackMission, friendlyOccupied: false,
        transportActive: true));
Equal(firstFingerprint, ArmyRtsPlanRules.Fingerprint(equivalentSnapshot));
NotEqual(firstFingerprint,
    ArmyRtsPlanRules.Fingerprint(snapshotWithDifferentTarget));
True(revisions.TryReserve(9, firstFingerprint, 100d, out int revision));
Equal(0, revision);
Equal(false, revisions.TryReserve(9, firstFingerprint, 120d, out _));
```

Projection tests use a `400 x 200` world and assert a `1024` long-edge canvas
produces `1024 x 512`, with `(0,0)` and `(399,199)` inside image bounds.

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --rts-plan-png-slice
```

Expected: compilation fails because `ArmyRtsPlanRules` and its DTOs do not exist.

- [ ] **Step 3: Implement immutable DTOs and pure rules**

Implement explicit DTO constructors and these rule boundaries:

```csharp
public static ArmyRtsPlanCanvas Project(int worldWidth, int worldHeight,
    int maximumLongEdge = 1024);
public static ArmyRtsPlanArrowStyle ArrowStyle(ArmyRtsPlanArmy army);
public static ulong Fingerprint(ArmyRtsPlanSnapshot snapshot);
public static string FileStem(long warId, int worldYear, int revision);
public static string ResolveOutputDirectory(string saveDirectory);
public static string ResolveStagingDirectory(string modDirectory, int processId);
```

`ArmyRtsPlanRevisionLedger.TryReserve` suppresses identical fingerprints and
allocates monotonically increasing revisions per war. It accepts the newest
material change during cooldown as pending rather than losing it.

- [ ] **Step 4: Run the focused test and verify GREEN**

Run the Task 1 command. Expected: `AW3 RTS war-plan PNG rules passed.`

- [ ] **Step 5: Commit Task 1 files only**

```powershell
git add Code/core/presentation/ArmyRtsPlanModels.cs Code/core/presentation/ArmyRtsPlanRules.cs Tests/AncientWarfare3.Rules.Tests/ArmyRtsPlanPngTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "test: define RTS war plan snapshot rules"
```

### Task 2: Raster And PNG Encoder

**Files:**
- Create: `Code/core/presentation/ArmyRtsPlanRasterizer.cs`
- Create: `Code/core/presentation/ArmyRtsPlanPngEncoder.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsPlanPngTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Write failing pixel and PNG tests**

Build a `64 x 32` test world with one owned zone, two cities, a recovery Army,
and a transporting Army. Assert gold and cyan pixels occur along the expected
segments, dashed transport has transparent/background gaps, and both arrowheads
end within three pixels of target markers. Encode the buffer and assert:

```csharp
SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 },
    png.Take(8).ToArray(), "PNG signature");
Equal(64, ReadBigEndianInt32(png, 16), "PNG width");
Equal(32, ReadBigEndianInt32(png, 20), "PNG height");
True(ValidateChunkCrcs(png), "all PNG chunks have valid CRC32");
```

- [ ] **Step 2: Run the focused test and verify RED**

Run the Task 1 command. Expected: compilation fails because rasterizer and PNG
encoder do not exist.

- [ ] **Step 3: Implement bounded raster primitives and PNG encoding**

Rasterizer operations are `FillRect`, `DrawLine`, `DrawDashedLine`,
`DrawArrowHead`, `DrawMarker`, and `DrawTinyText`. All coordinates are clipped.
PNG encoding writes signature, IHDR, one IDAT containing zlib stored blocks and
Adler32, and IEND; each chunk uses CRC32. No Unity type or API is referenced.

- [ ] **Step 4: Run focused tests and decode the artifact**

Run the Task 1 command. Expected: all tests pass and the encoder test validates
every chunk, dimensions, decompressed row count, and RGBA color type.

- [ ] **Step 5: Commit Task 2 files only**

```powershell
git add Code/core/presentation/ArmyRtsPlanRasterizer.cs Code/core/presentation/ArmyRtsPlanPngEncoder.cs Tests/AncientWarfare3.Rules.Tests/ArmyRtsPlanPngTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
git commit -m "feat: render RTS war plans as PNG"
```

### Task 3: Bounded Artifact Writer And Save Publication

**Files:**
- Create: `Code/core/presentation/ArmyRtsPlanArtifactWriter.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsPlanPngTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Write failing staging and publication tests**

Use temporary directories. Enqueue two revisions for the same war before the
worker consumes them and assert only the newest pending revision remains. Write
without a save directory and assert PNG/manifest appear in the process staging
directory. Call `PublishToSave(savePath)` and assert both appear under
`savePath/aw3_rts_plans`, temporary files do not remain, and source staged files
remain only until successful publication.

- [ ] **Step 2: Run focused tests and verify RED**

Run the Task 1 command. Expected: compilation fails because the artifact writer
does not exist.

- [ ] **Step 3: Implement one below-normal bounded writer**

Link `Code/core/asyncwork/AWAsyncWorkQueue.cs` into the rule-test project. The
writer owns one `AWBoundedLatestQueue<ArmyRtsPlanArtifact>` keyed by war id,
one background thread, and a cancellation flag. It rasterizes, encodes, writes
`*.tmp`, flushes, then atomically moves PNG and manifest into place. `Shutdown`
joins with a finite timeout. No WorldBox or Unity object crosses the queue.

- [ ] **Step 4: Run focused tests and verify GREEN**

Run the Task 1 command. Expected: staging, atomic write, latest-per-war queue,
publication, and shutdown tests pass.

- [ ] **Step 5: Commit Task 3 files only**

```powershell
git add Code/core/presentation/ArmyRtsPlanArtifactWriter.cs Tests/AncientWarfare3.Rules.Tests/ArmyRtsPlanPngTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
git commit -m "feat: persist RTS war plan artifacts"
```

### Task 4: Runtime Capture And Lifecycle Hooks

**Files:**
- Create: `Code/core/presentation/ArmyRtsPlanSnapshotService.cs`
- Modify: `Code/core/presentation/ArmyRtsVisualizationService.cs`
- Modify: `Code/patch/AW_ArmyRtsVisualizationPatch.cs`
- Modify: `Code/patch/AW_WarPatch.cs`
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs`
- Modify: `Code/patch/AW_SavePatch.cs`
- Modify: `Code/ModClass.cs`
- Create: `Tests/ArmyRtsPlanPngSourceGuard.ps1`

- [ ] **Step 1: Write a failing integration source guard**

Assert exact integration boundaries:

```powershell
Assert-Contains $warPatch 'ArmyRtsPlanSnapshotService.OnWarStarted(__result)'
Assert-Contains $controller 'ArmyRtsPlanSnapshotService.OnMissionChanged('
Assert-Contains $visualPatch 'ArmyRtsPlanSnapshotService.ProcessFrame'
Assert-Contains $savePatch 'ArmyRtsPlanSnapshotService.ObserveSaveDirectory(pFolder)'
Assert-Contains $visualService 'AWPerformanceSettings.ShowArmyRtsVisuals'
Assert-NotContains $writer 'UnityEngine'
Assert-NotContains $writer 'World.world'
```

- [ ] **Step 2: Run the source guard and verify RED**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File Tests/ArmyRtsPlanPngSourceGuard.ps1
```

Expected: failure on the missing war-start hook.

- [ ] **Step 3: Implement main-thread capture and hooks**

`OnWarStarted` records a pending initial request. `OnMissionChanged` records a
material request only when `Controllers.AssignMission` reports changed.
`ProcessFrame` checks authoritative RTS `On`, visual setting enabled, world
loaded, not loading, and not replica; it captures at most one pending war.

Capture iterates city zones and indexed participant Armies only. Friendly
recovery is true when the target city belongs to the Army kingdom but frozen
occupation or active enemy capture points at a hostile controller. Transport
style uses `ArmyRtsTransportService.HasActiveVoyage`. Route anchors come from
the existing controller target APIs. The manifest serializes all captured DTOs
without reading live objects on the writer thread.

Save/load hooks update the active directory and publish staging only after a
successful save. Clear/load/new-map/end-war/shutdown clear or dispose state.
`ArmyRtsVisualizationService.ProcessFrame` reads
`AWPerformanceSettings.ShowArmyRtsVisuals` directly, removing the orphaned
private enable state.

- [ ] **Step 4: Run source guard and focused rules**

Run both Task 1 and Task 4 commands. Expected: both pass.

- [ ] **Step 5: Build Debug and Release**

Run:

```powershell
dotnet build AncientWarfare3.csproj -c Debug
dotnet build AncientWarfare3.csproj -c Release
```

Expected: zero errors and zero warnings in both configurations.

- [ ] **Step 6: Commit Task 4 files only**

```powershell
git add Code/core/presentation/ArmyRtsPlanSnapshotService.cs Code/core/presentation/ArmyRtsVisualizationService.cs Code/patch/AW_ArmyRtsVisualizationPatch.cs Code/patch/AW_WarPatch.cs Code/core/lineage/ArmyRtsControllerService.cs Code/patch/AW_SavePatch.cs Code/ModClass.cs Tests/ArmyRtsPlanPngSourceGuard.ps1
git commit -m "feat: capture RTS war plans from live missions"
```

### Task 5: Regression, Deployment, And Runtime Acceptance

**Files:**
- Verify: all files above and existing RTS rule/source-guard suites
- Deploy: `Code/**` to the installed `AncientWarfare3.0/Code/**`

- [ ] **Step 1: Run focused RTS regression suites**

Run Release rule slices, RTS source guards, shared captain-path guards, captain
career guards, occupied-target handoff guards, and the adversarial RTS
simulation. Expected: every command exits zero with no warning/error output.

- [ ] **Step 2: Verify WorldBox is closed and deploy source only**

Require no `worldbox.exe` process. Recursively copy the development `Code`
directory into the installed mod `Code` directory without deleting destination
extras, copying DLLs, or touching `.runtime`.

- [ ] **Step 3: Verify deployment hashes**

Hash every development source file and its deployed counterpart. Expected:
every development `Code` file exists at the destination and all hashes match.

- [ ] **Step 4: Start WorldBox and inspect fresh startup evidence**

Start a new process, then inspect `Player.log`. Require RTS mode `on`, the
selected scheduler owner, active AW3 movement ownership, no compilation failure,
and no PNG-worker fault.

- [ ] **Step 5: Exercise a real war in a saved world**

Verify `aw3_rts_plans/war_*_000.png` is created and visually nonblank. Compare
the manifest against live Army missions. Confirm a friendly occupied city gets
a Defense mission and gold arrow, the assigned captain and at least 80 percent
of members move continuously, the flag identity remains stable, capture hands
the Army to another city, and cross-water missions show and execute transport.

- [ ] **Step 6: Inspect the new-process log and PNG**

Reject completion if the log shows repeated mission churn, captain identity
changes while alive, formation-member watchdog samples, route ownership loss,
staging/write failures, or a Defense mission that remains in Rally, Replenish,
or Retreat while departure-ready. Keep the goal active until runtime evidence
proves idle, non-attack, and flag-flicker failures are absent.

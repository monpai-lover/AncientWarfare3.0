# RTS War Plan Animated GIF Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:test-driven-development` for every behavior change and `superpowers:verification-before-completion` before reporting success. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace per-revision RTS PNG/TXT artifacts with bounded animated GIF89a histories written only during normal shutdown.

**Architecture:** Capture actual WorldBox terrain and ownership on the main thread, render directly to deterministic indexed pixels, retain a bounded chronological frame ledger, and encode each retained war/session sequence atomically at shutdown. `AWPerformanceSettings.ArmyRtsDiagnosticsEnabled` gates every diagnostic lifecycle operation.

**Tech Stack:** C# 9, Unity/WorldBox source APIs, repository-native indexed raster and GIF LZW encoder, PowerShell source guards, .NET 9 rule-test harness.

---

### Task 1: Specify GIF And Bounded Ledger Behavior

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsPlanPngTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] Write tests for the fixed palette, indexed raster dimensions, GIF89a signature, loop extension, multiple image descriptors, trailer, and independent LZW decoding.
- [ ] Write tests for duplicate suppression and bounded frame/sequence retention that preserve first and latest frames.
- [ ] Write writer tests proving save observation creates no files and shutdown creates only one GIF plus one manifest per sequence.
- [ ] Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --rts-plan-gif-slice` and record the expected RED failure on missing GIF APIs.

### Task 2: Implement Indexed Models, Raster, Ledger, And Encoder

**Files:**
- Modify: `Code/core/presentation/ArmyRtsPlanModels.cs`
- Modify: `Code/core/presentation/ArmyRtsPlanRules.cs`
- Modify: `Code/core/presentation/ArmyRtsPlanRasterizer.cs`
- Replace: `Code/core/presentation/ArmyRtsPlanPngEncoder.cs` with `Code/core/presentation/ArmyRtsPlanGifEncoder.cs`
- Modify: `Code/core/presentation/ArmyRtsPlanArtifactWriter.cs`

- [ ] Add immutable terrain samples and indexed-frame DTOs without retaining RGBA buffers.
- [ ] Add named defaults for 768-pixel long edge, 32 frames per sequence, 48 global frames, 8 sequences, and 75-centisecond delay.
- [ ] Render terrain, ownership boundaries, cities, fronts, arrows, transport, recovery, and stalled markers directly into palette indices.
- [ ] Implement deterministic first/latest-preserving decimation and global completed-sequence eviction.
- [ ] Implement a GIF89a encoder with global palette, loop extension, graphics controls, image descriptors, LZW sub-blocks, and trailer.
- [ ] Re-run the focused slice after each minimal implementation increment until GREEN.

### Task 3: Implement Shutdown-Only Lifecycle

**Files:**
- Modify: `Code/core/presentation/ArmyRtsPlanArtifactWriter.cs`
- Modify: `Code/core/presentation/ArmyRtsPlanSnapshotService.cs`

- [ ] Change save/load methods to directory observation only.
- [ ] Retain closed world/war sequences within configured bounds until normal shutdown.
- [ ] Gate capture, queueing, observation, clear behavior, and shutdown output on `AWPerformanceSettings.ArmyRtsDiagnosticsEnabled`.
- [ ] Discard all pending diagnostic memory when disabled without deleting historical files.
- [ ] Build one compact sequence manifest and write GIF/manifest pairs atomically at shutdown with per-sequence failure isolation.
- [ ] Re-run focused tests and verify no file exists before shutdown.

### Task 4: Capture Actual Terrain And Guard Integration

**Files:**
- Modify: `Code/core/presentation/ArmyRtsPlanSnapshotService.cs`
- Replace: `Tests/ArmyRtsPlanPngSourceGuard.ps1` with `Tests/ArmyRtsPlanGifSourceGuard.ps1`

- [ ] Sample `WorldTile.getColor()` and zone ownership on the main thread into the immutable snapshot.
- [ ] Assert the exact diagnostics setting gate, main-thread sampling, shutdown-only GIF output, atomic temporary names, and absence of production PNG writes.
- [ ] Run `powershell -ExecutionPolicy Bypass -File Tests/ArmyRtsPlanGifSourceGuard.ps1` and make it GREEN.

### Task 5: Verification And Evidence

- [ ] Generate a multi-frame sample GIF in a temporary test directory and independently decode every LZW frame.
- [ ] Run the focused Release slice and GIF source guard.
- [ ] Run the complete rule-test harness in Debug and Release.
- [ ] Run `dotnet build AncientWarfare3.csproj -c Debug` and `dotnet build AncientWarfare3.csproj -c Release`.
- [ ] Inspect the final diff for forbidden settings/UI/log-call-site changes, per-frame files, external dependencies, and unrelated churn.
- [ ] Report RED/GREEN commands, GIF signature/frame/decoding evidence, build evidence, and remaining runtime risks. Do not commit, deploy, mutate saves, or control WorldBox.

## Verification Record

RED was observed first from the focused Release slice on missing `ArmyRtsPlanIndexedRaster`/`ArmyRtsPlanGifFrame`, then from the GIF source guard on the old diagnostics gate. Follow-up RED cases covered terrain composition, terrain fingerprinting, Hold/stalled rendering, completed-sequence eviction, and load/save lifecycle ordering.

The completed GIF change passed:

- focused GIF Release slice;
- GIF lifecycle source guard;
- complete rule harness in Debug and Release;
- `AncientWarfare3.csproj` Debug and Release builds with zero warnings and errors;
- independent LZW decode in the focused tests;
- Windows `System.Drawing` decode of a 64x32, two-frame `GIF89a` sample with trailer `0x3B`.

After that matrix completed, unrelated concurrent `WarScorePersistence` changes referenced missing `WarScoreSnapshot.AttackerExhaustionRelief` and `DefenderExhaustionRelief` members. Current from-source builds are therefore blocked outside this plan's files; the already-built final GIF slice and source guard still pass. No deployment, commit, save mutation, or WorldBox process control was performed.

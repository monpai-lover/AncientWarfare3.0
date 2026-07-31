# AW3 Cultiway Large Scheduler Update Design

## Objective

Update AW3's opt-in large scheduler from the current implementation to the
latest scheduler behavior in `Cultiway-Reborn-perf`. The port includes the
simulation stage burst model, authoritative simulation time, background actor
and building execution, and immutable presentation snapshots.

The existing AW3 scheduler switch remains the ownership boundary. When the
switch is disabled, WorldBox's native simulation and presentation paths run
without AW3 scheduler interception. When enabled, the updated large scheduler
owns the full simulation cycle.

## Source Of Truth

The behavioral source is the current filesystem snapshot under:

- `Cultiway-Reborn-perf/Source/Core/Performance/CooperativeSimulationRunner.cs`
- `Cultiway-Reborn-perf/Source/Core/Performance/CooperativeBatchRunner.cs`
- `Cultiway-Reborn-perf/Source/Core/Performance/FramePriorityGovernor.cs`
- `Cultiway-Reborn-perf/Source/Core/Performance/SimulationTime.cs`
- `Cultiway-Reborn-perf/Source/Core/Performance/SimulationStepContext.cs`
- `Cultiway-Reborn-perf/Source/Patch/PatchFramePriorityScheduler.cs`
- `Cultiway-Reborn-perf/Source/Const/PerformanceSettings.cs`

The port is a semantic mirror rather than a blind file copy. Cultiway-only ECS
and gameplay systems are excluded. AW3-specific authority, multiplayer, worker
ownership, diagnostics, and native fallback behavior are retained at explicit
adapter boundaries.

## Architecture

### Mirrored Scheduler Core

The AW3 runner mirrors Cultiway's current stage order and burst execution
rules. A frame may advance several safe stages while the current domain owns
budget. A burst stops when it completes the tick, reaches an asynchronous read
boundary, crosses a simulation domain, reaches its deadline, or reaches the
stage-count limit.

The updated stage graph includes the `AnimationTime` stage. The runner uses an
AW3 simulation clock equivalent to Cultiway's `SimulationTime`: time is bound
to the current world, a pending value is exposed during a tick, and committed
time advances only when the tick completes. World clear, load, cancellation,
and failure invalidate the binding.

### Per-Domain Frame Governor

The governor accounts separately for vanilla simulation and AW3 authority
work. It exposes the remaining simulation budget so the runner can decide
whether deferred parallel work should run synchronously or overlap the
presentation frame. Starvation protection is evaluated every render frame so
heavy worlds cannot remain permanently stuck at a zero-budget stage.

AW3's configured frame target and simulation budget remain authoritative.
Worker counts continue to come from `AWSchedulerResourceOwnership` and
`AWFrameSchedulerRules`; the port must not create a competing Cultiway-owned
pool that oversubscribes actor pathfinding or RTS routing.

### Actor And Building Background Work

Actor and building batch runners preserve Cultiway's split between main-thread
stages, deferred parallel work, and completion boundaries. Deferred work may
start eagerly when budget and presentation state permit. A batch must publish
completion before any caller is allowed to read mutable actor or building
state directly.

Every background operation has one owner and one completion path. Save, pause,
replica transition, world clear, failure recovery, and native-mode transition
must either join or cancel owned work before releasing scheduler state.

### Immutable Presentation Snapshots

The full-port scope includes AW3-prefixed equivalents of the Cultiway actor and
world-object presentation snapshot services required by the scheduler. Actor,
building, overlay, transient-effect, status-animation, and positional
presentation code reads the most recently published immutable snapshot while
simulation mutation is in flight.

At unsupported presentation paths, missing-snapshot paths, debugging paths, or
special WorldBox rendering paths, the patch establishes an actor/building read
boundary first and then permits the native read. This makes the fallback safe
instead of allowing render code to race the scheduler.

AW3's existing paused-position snapping, flash-effect refresh, cursor
lifecycle, minimap visibility, and presentation interpolation fixes are merged
into this snapshot pipeline rather than overwritten.

## AW3 Integration Boundaries

### Authority Cycle

`AWAuthorityCycleService.ProcessCooperativeCycle` executes once at the end of
each completed authoritative simulation tick. It never executes once per
render frame and never executes on a multiplayer replica. Native mode retains
the existing native-cycle callback.

### Multiplayer

`AW3MultiplayerReplicaScope.IsReplicaSession` remains authoritative. Entering a
replica session stops admission and aborts local in-progress simulation after
owned background work is made safe. Applying replicated state remains outside
the scheduler.

### Pause, Save, Load, And Clear

Pause admits no new simulation tick. Presentation may continue from the last
snapshot, with AW3's paused interpolation behavior snapping to authoritative
positions.

Save and autosave requests execute only at a complete cycle boundary. If a
cycle is active, the request is deferred until all mutation and snapshot
publication are complete. Load, clear, and failed world creation abort the
cycle, clear presentation state, cancel pending simulation time, and reset the
governor. Successful world creation binds a new simulation clock and clears
the fault latch.

### Native Fallback

Disabling the scheduler prevents AW3 from replacing WorldBox simulation and
snapshot presentation. Any already-owned cycle is first completed or safely
aborted. No scheduler-specific time source, visibility override, or background
mutation remains active in native mode.

## Failure Handling

The runner records the active stage and boundary reason when scheduler-owned
work fails. It joins or cancels background work, cancels pending simulation
time, clears unpublished snapshots and commands, pauses new admissions, and
latches the fault until a world lifecycle reset. It must not retry a failing
stage every render frame.

Native mode remains an emergency fallback. Failure handling must preserve the
original exception as the primary diagnostic rather than replacing it with a
secondary cleanup exception.

## Compatibility And Merge Strategy

Implementation occurs in an isolated worktree based on the current AW3
`master`. The main worktree is dirty with unrelated user work, including
scheduler presentation changes. The implementation commit is merged by path
and reconciled manually where it overlaps:

- `Code/patch/AW_FramePrioritySchedulerPatch.cs`
- `Code/core/performance/AWPresentationInterpolator.cs`

No unrelated modified or deleted file is staged, restored, or rewritten. New
scheduler support files use AW3 namespaces and naming and remain local to the
performance and patch ownership boundaries.

## Verification

The user explicitly prohibited DLL compilation. Verification therefore uses
only source-level evidence:

- PowerShell source guards for required stage order, `AnimationTime`, stage
  bursts, per-domain accounting, starvation behavior, remaining-budget API,
  simulation-time lifecycle, actor/building completion boundaries, snapshot
  publication, native fallback, replica freeze, authority-cycle placement,
  and save/load/clear cleanup;
- static symbol and dependency checks against the current Cultiway source;
- checks that scheduler code does not create unowned worker pools or bypass AW3
  worker allocation;
- checks that native mode does not install active snapshot/presentation
  overrides;
- `git diff --check` and a scoped review of every changed file.

`dotnet build`, `dotnet run`, game DLL generation, and DLL deployment are not
part of this work.

## Deployment

After source verification and merge, deploy only changed source, configuration,
and localization files to the game installation's AW3 source tree. Do not copy
or generate DLLs. Deployment is complete only when deployed file hashes match
the verified repository versions.

## Acceptance Criteria

1. Enabling the AW3 scheduler selects the updated full large scheduler; disabling
   it restores native WorldBox scheduling and presentation.
2. AW3 mirrors Cultiway's current stage burst, `AnimationTime`, simulation-time,
   per-domain budget, starvation, deferred actor/building, and presentation
   snapshot behavior.
3. AW3 authority executes exactly once per completed local simulation tick.
4. Replica clients perform no local authoritative simulation.
5. Pause, save, autosave, load, clear, and failure transitions leave no mutable
   actor/building work running without an owner.
6. AW3 pathfinding and RTS routing retain their allocated worker ownership.
7. Existing AW3 presentation fixes remain present after reconciliation.
8. All source guards and static checks pass, and no DLL is compiled or deployed.
9. Deployed source hashes match the verified repository files.

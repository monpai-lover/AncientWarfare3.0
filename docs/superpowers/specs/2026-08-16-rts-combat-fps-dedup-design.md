# RTS Combat FPS Deduplication Design

## Goal

Reduce repeated CPU work in RTS combat without changing combat decisions,
task transitions, target selection results, military priority, or diagnostic
logging behavior.

## Scope

The change is limited to computations repeated within one controller or actor
P0 execution. No cache may survive into a later logical tick. Existing search
radii, target ordering, hostility checks, engagement thresholds, task chains,
and scheduling budgets remain unchanged.

## Design

### Engagement counting

Do not scan the full army in captain P0 when the captain already has a valid
combat target, because the existing abort predicate is then unconditionally
false. Preserve the full scan when the captain has no valid target. The
controller continues to compute a fresh engagement count whenever it needs the
engaged/live ratios, preserving immediate reactions to deaths and target
changes.

### Member target acquisition

Validate a retained member behavior target once. If it is invalid, run the
existing nearby-unit search. A target returned by that search has already
passed the same validator, so the behavior accepts it without immediately
validating it a second time. Native `attack_target` is not substituted for the
behavior target because doing so could alter the existing nearest-target
selection result.

### Candidate validation

Evaluate actor-level invariants, including member combat ownership and actor
viability, once before iterating search candidates. Candidate-level validation
continues to check target life, hostility, and island compatibility for every
candidate. Public validation behavior remains unchanged.

### Task admission

Do not resolve a personal combat target when task admission is already fixed:
captains and inactive missions reject the member task, active siege members use
the siege task, and members in an already-released field battle use the member
combat task even during a transient target miss. Preserve target validation for
strategic movement, where a newly detected target decides whether combat starts.

## Explicit Non-Goals

- Do not change or rate-limit RTS diagnostic output.
- Do not reduce military P0 frequency or batch size.
- Do not change combat range, attack cadence, target preference, or pathfinding.
- Do not add cross-frame or cross-tick caches.
- Do not change siege, retreat, return-home, or royal-guard behavior.

## Verification

Add rule-level regression tests that fail before implementation and prove:

- a valid retained behavior target is accepted after one validation;
- an invalid retained target still uses the existing fallback search;
- actor-level member eligibility is evaluated outside candidate iteration;
- captain P0 skips roster counting only when its abort result is already fixed
  by a valid captain target.

Then run the tracked rules test entrypoint, continuity and foundation tests,
and a Release build. Source guards must confirm that search radius, ordering,
thresholds, task chains, and military P0 budgets are unchanged.

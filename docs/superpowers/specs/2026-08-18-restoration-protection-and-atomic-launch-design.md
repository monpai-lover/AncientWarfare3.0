# Restoration Protection And Atomic Launch Design

## Scope

Fix the autonomous-restoration launch mismatch that can create a kingdom and
immediately remove it, and give every successfully restored kingdom ten game
years of protection from new external declarations of war.

The three supported restoration routes are:

- autonomous royal restoration;
- hosted restoration after a successful restoration war;
- Guiyi restoration of an extinct kingdom.

The protection is defensive. It does not prevent the restored kingdom from
starting its own wars, and it does not suppress internal rebellions or subject
independence wars.

## Root Cause

Autonomous restoration currently collects initial supporter IDs while the
claimant and seed residents still belong to the former owner. It then creates
the restored kingdom, appoints the claimant as king and city leader, refreshes
the heir, and finally tries to enlist the frozen supporter list.

The enlistment rules correctly reject kings, heirs, city leaders, officials,
and other protected identities. A claimant or future heir that was eligible
during preflight can therefore become ineligible after creation. When the
preflight list only met the minimum exactly, the initial cohort falls below the
required count. The provisional rollback path then returns the seed city to
its former owner and removes the newly restored kingdom.

The fix must align preflight eligibility with post-creation eligibility. The
ten-year protection is an additional survival rule, not a substitute for this
atomic launch fix.

## Atomic Autonomous Launch

### Preflight Candidate Rules

Initial supporter collection must exclude the restoration claimant. The
claimant is guaranteed to become both king and seed-city leader and therefore
cannot be counted as a future uprising soldier.

Preflight must require one additional eligible supporter beyond
`RoyalRestorationRules.MinimumRequiredSupporters(defenders)`. This reserve
covers the single candidate who may become the restored kingdom's heir during
identity recovery.

The inspection remains bounded by the existing seed-resident limits. The fix
must not scan world actors, the whole kingdom, or additional cities.

### Post-Creation Validation

After `RestoreFromCity` returns, the initial supporter IDs must be revalidated
against the restored kingdom and seed city. The post-creation seed validation
must use the count of this revalidated list rather than the stale pre-creation
list count.

`TryStartWithInitialCohort` receives the revalidated IDs and the normal minimum
supporter requirement, not the reserve requirement. A successful launch must
therefore produce the required number of actual uprising soldiers after king,
city-leader, heir, office, army, age, profession, and actor-liveness gates are
applied.

### Commit And Rollback Boundary

The campaign is considered launched only after the actual initial cohort has
been created. History entries and the first restoration war remain after this
commit point.

The existing provisional rollback remains as a defensive recovery path for
real concurrent invalidation or recruitment failure. It must no longer be
triggered solely because the claimant became king or one candidate became
heir.

## Ten-Year Restoration Protection

### Persistent State

Add a kingdom-data field named
`aw_restoration_protection_until_year`, exposed through a `LineageKeys`
constant. A successful restoration writes:

```text
protection_until_year = restoration_year + 10
```

Protection is active while:

```text
current_year < protection_until_year
```

A kingdom restored in year 100 is protected during years 100 through 109 and
can receive new external declarations from year 110 onward. No annual cleanup
or world scan is required. Missing fields in old saves mean no active
protection.

The field is written by the shared `KingdomIdentityContinuityService` success
path so autonomous, hosted, and Guiyi restoration receive identical behavior.
If an autonomous launch is legitimately rolled back, removal of the
provisional kingdom also removes its protection state.

### Wars Blocked

While protection is active, a new war is blocked when the protected restored
kingdom is the declared defender and the war is external.

The gate applies to:

- ordinary AI and player declarations;
- AW3 diplomatic and war-goal declarations;
- system wars that represent an external attack;
- direct vanilla `DiplomacyManager.startWar` and `WarManager.newWar` calls;
- pending declarations that were prepared before restoration but execute
  during the protection period.

The failure reason is `restoration_protection`.

### Wars Allowed

The protection does not block:

- wars started by the restored kingdom;
- the autonomous restoration uprising against the seed city's former owner;
- restoration core-recovery wars;
- vassal independence wars;
- general and fief rebellions;
- succession disputes;
- Jingnan wars;
- loyalist coup-restoration wars;
- Mandate rebellions and other authoritative internal rebellion routes;
- wars that were already active when restoration completed.

Internal-war classification must use a dedicated pure rule informed by the
existing internal war types and the explicit internal-system-war flag. It must
not rely only on alliance, vassal, or current kingdom hierarchy because rebel
kingdoms may not yet have a stable subject relation when the war starts.

## Integration

`RestorationProtectionRules` owns pure year and war-direction decisions.
`RestorationProtectionService` reads kingdom data and supplies the runtime
facts. `WarDecisionService.StartWar` performs the authoritative AW3 check,
including the internal-system-war exemption. `WarDecisionService.ShouldBlockWarStart`
performs the equivalent final check for vanilla entry points patched by
`AW_WarPatch`.

The protection check must run before AW3 allowed-war scopes can bypass normal
vanilla-war isolation. An external system war is not exempt merely because it
was initiated by AW3.

## Diagnostics

Blocked declarations use the existing bounded war-block logging path and the
`restoration_protection` reason. The feature adds no per-frame or annual logs
and no repeated enumeration.

## Verification

Rule and source-guard tests must prove:

- a claimant cannot satisfy the preflight supporter count;
- the preflight reserve covers one future heir;
- post-creation validation uses revalidated supporter IDs;
- a genuine insufficient cohort still fails safely;
- all three restoration routes write the protection deadline through the
  shared restoration success path;
- protection is active in restoration years 0 through 9 and expires in year
  10;
- external incoming declarations are blocked;
- wars started by the restored kingdom are allowed;
- internal rebellion and subject-independence war types are allowed;
- external system wars cannot bypass protection;
- vanilla direct war entry points call the protection gate;
- old saves without the field remain compatible.

Run the targeted rule tests, the full rules suite, and Debug and Release builds.

## Non-Goals

- Do not end or rewrite wars already in progress.
- Do not create bilateral truce rows with every kingdom.
- Do not make the restored kingdom immune to internal politics.
- Do not change the 65 percent restoration campaign completion threshold.
- Do not add world, kingdom, city, or actor scans outside existing bounded
  restoration work.

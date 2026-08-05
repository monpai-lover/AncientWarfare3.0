# Western Surname Inheritance Design

## Goal

Preserve Western family identity after descendants lose political power.
Ordinary descendants must inherit and display a valid surname independently of
noble, ruler, heir, or official status.

## Root Cause

Two existing behaviors combine into the defect:

1. Ordinary Western births use a lightweight path that records parent-child
   edges but skips `InheritFromParents`, so surname and branch identity are not
   inherited.
2. Western display projection deliberately hides the surname when
   `noble == false`, even when the actor still has valid family data.

Political status has therefore become an accidental gate for both persistence
and presentation.

## Inheritance Rule

A Western child enters the complete family-inheritance path whenever either
parent has a valid complete branch with the same naming profile. This decision
does not depend on current office, noble status, or authority.

Selection is deterministic:

1. use the valid paternal branch when present;
2. otherwise use the valid maternal branch;
3. reject branches whose naming profile differs from the child's resolved
   profile, including accidental Xia or Orc inheritance;
4. create a new surname only when neither parent offers a valid branch.

`LINEAGE_STATUS` continues to control noble identity, traits, and succession.
It no longer controls surname inheritance or visibility.

## New Surname Creation

For a true founder with no valid parental surname, choose one of two sources by
a stable 50/50 actor-seeded decision:

- the founder's city name;
- a Western surname stem from a new `western_family_stem` generator backed by
  the existing fantasy-human surname library.

The choice and resolved stem are persisted. `DISPLAY_STEM` is authoritative on
restore; city-derived reconstruction is only a legacy fallback. This prevents
a random surname from changing into a city surname after save/load.

Name assembly removes at most one complete existing surname token before
appending the authoritative surname. It must not deduplicate individual
characters, because legitimate repeated characters can occur in given names.

## Presentation

Western actors with a valid family identity display their given name and
surname whether they are noble or common. Noble state may still affect titles
or other presentation, but not the presence of the surname. Live names, family
tree projections, archived projections, and localized stored names use the same
rule.

## Legacy Repair

Add a versioned, bounded migration for living Western actors. It scans by
stable actor ID and repairs a child only when a valid same-profile parental
branch exists and the child lacks stable family identity. It must not overwrite
an existing different valid branch.

The migration is resumable and idempotent. It updates database identity before
publishing the live actor projection. Dead archives and historical display
records remain unchanged. Actors that change profile or kingdom are rechecked
before each write.

## Failure Handling

Missing parents, malformed branches, unsupported profiles, database failures,
or vanished actors skip the candidate without inventing a cross-profile family.
A failed database write cannot partially update the live actor. Diagnostics are
budgeted and identify actor, parent source, profile, and skip reason.

## Verification

Tests must reverse the assertions that currently preserve the defect and add:

- three generations retaining one surname after all descendants lose power;
- paternal priority and maternal fallback;
- rejection of Xia and Orc branch inheritance;
- deterministic 50/50 source selection and stable generator use;
- save/load retention through `DISPLAY_STEM`;
- commoner display in live, family-tree, archive, and localized projections;
- complete-token idempotence without character-level corruption;
- old-save repair, existing-branch preservation, rollback, bounded resume, and
  repeat-run zero-change behavior;
- full naming, lineage, birth, archive, and rules suites.

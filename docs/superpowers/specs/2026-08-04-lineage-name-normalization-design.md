# Lineage Name Normalization Root Fix Design

## Goal

Stop structured lineage names from producing duplicated surnames such as
`房房雨立`, repair existing high-confidence corrupt records, and remain
compatible with external naming Harmony patches.

## Root Cause

Some capture paths store an already composed full name in `aw_given_name`.
Foreign pseudo-lineage admission trusts a non-empty existing given name, and
archive fallback may copy `Actor.getName()` wholesale. Later,
`LineageDisplayNameRules.Build` unconditionally adds the known family or clan
again. `ApplyDisplayName` then persists the duplicated projection back to the
actor and archive.

## Design

### Canonical Capture

Add a pure given-name normalization rule. Its inputs include the candidate
given name, raw stored name, known family and clan tokens, naming direction,
profile, and provenance. It may remove exactly one complete known prefix or
suffix only when the remainder is non-empty.

Capture prefers `actor.data.name` and AW-owned structured components over
`Actor.getName()`, because the latter may already include another mod's
Harmony projection. Foreign pseudo-lineage admission normalizes a non-empty
existing given instead of trusting it verbatim. Archive fallback also passes
through the same rule.

Historical, Western, and Orc naming profiles keep their existing specialized
projection paths.

### Idempotent Display

`LineageDisplayNameRules.Build` composes complete family/clan tokens
idempotently. If the given value already contains the exact required prefix or
suffix, it does not add another copy. The rule never deduplicates adjacent
characters, so valid names such as `婷婷` remain unchanged and compound
surnames are treated as complete tokens.

### Existing Data Repair

Living actors are repaired lazily at the next authoritative
`ApplyDisplayName`. A dirty `aw_naming_given_name` is updated only when it still
equals the old dirty value. The existing identity refresh path persists the
canonical value.

Dead archives are migrated in bounded, repeatable batches. Persistent data is
changed only when evidence is high-confidence: a known token is present, the
candidate equals the dirty composed form, and the stored/raw full name agrees
with that composition. Ambiguous rows receive correct idempotent display but
are not rewritten.

## External Mod Boundary

- Keep the existing naming collision UID gate.
- Do not modify `aw_native_name` or `aw_chinese_name` unless AW explicitly owns
  the integrated naming record.
- Diagnose `Actor.getName` Harmony owners when structured fields are already
  clean but final output still duplicates.
- Never perform first-character surname stripping.

## Failure Handling

- Empty remainder means no normalization.
- Missing or conflicting family/clan evidence means display-only protection.
- Migration is idempotent and resumes after interruption.
- User changes in `XiaRace.cs`, `name_generators/default/creatures.json`, and
  the spirit-name word list are outside this fix and remain untouched.

## Tests

- Male, integrated, and female suffix composition are idempotent.
- Compound surname tokens deduplicate as a unit.
- Legal repeated given names remain unchanged.
- Dirty foreign pseudo-lineage given values normalize at capture.
- Clean existing given values and Western/Orc profiles do not regress.
- High-confidence migration repairs; ambiguous migration is display-only.
- Archive fallback does not treat a projected `getName()` as canonical given.
- Existing naming collision behavior remains intact.

## Non-Goals

- Reimplementing external naming mods.
- Heuristic Unicode character deduplication.
- Destructive migration of ambiguous historical names.

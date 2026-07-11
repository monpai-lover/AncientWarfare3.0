# AW3 Foreign Xiaized Lineage And Pre-Qin Names Design

## Goal

Preserve the ethnic identity and personal names of non-Xia peoples that adopt
Xia institutions while giving their officials stable AW3 surname/branch data,
Xia-style clan presentation, and continuous descendants. Replace the mixed Xia
kingdom-name pool with a dedicated broad pre-Qin polity library that follows the
Chinese Name mod's generator and word-library pipeline.

## Current Problems

`ForeignPseudoLineageRules` currently splits a personal name at its last space or
punctuation mark. When no delimiter exists it falls back to the complete vanilla
Clan display name, and when no Clan exists it uses the complete personal name as
the branch name. Only a trailing `氏` is removed.

This produces unstable results:

- `John Smith` correctly becomes given name `John`, branch `Smith`.
- `李云` with Clan `曲阜的李家族` can become branch `曲阜的李家族` and display
  `曲阜的李家族李云`.
- a one-token name with no Clan can be used as both branch and given name.
- a second promotion can parse the already-composed display name and add the
  branch prefix again.
- lineage birth handling accepts Xia and human children, but not other civilized
  species, so Xiaized elf/orc/dwarf official families can stop after one
  generation.
- the AW3 branch and the visible vanilla Clan can show different names.

The current `Xia_kingdom` resource uses `中文国名前缀`, which mixes ancient
states, later dynasties, modern regional abbreviations, duplicate entries, and
ordinary characters.

## Foreign Name Resolution

Add one pure resolver that returns stable given, family, and branch names from
structured actor data plus legacy text. Existing structured values always win.

Family/branch source priority is:

1. Existing AW3 `family_name` or `clan_name`.
2. Chinese Name's `chinese_family_name` actor field.
3. A surname token parsed from the personal display name.
4. A normalized vanilla Clan display name.
5. The first useful character of the kingdom name.

Clan normalization trims whitespace, takes the portion after the final Chinese
possessive `的`, and repeatedly removes these terminal labels:

`家族`, `氏族`, `部落`, `家`, `族`, `氏`.

It preserves legitimate compound names such as `司马`, `钟离`, and `淳于`.

Given-name source priority is:

1. Existing `aw_given_name`.
2. The personal display name with the resolved family name removed from its
   start or end.
3. The text before the last recognized delimiter.
4. The untouched display name.

The runtime integration writes only missing name fields. Repeated king/leader
appointments therefore remain idempotent and cannot grow the display name.
Personal given names are never replaced with random Xia names.

## Lineage And Clan Lifecycle

When a kingdom uses Xiaized institutions, king, city leaders, and army leaders
continue to receive an AW3 lineage immediately. Their existing Chinese Name
surname is reused where possible.

Lineage birth eligibility expands from Xia/human-only to every civilized actor
whose parent already has AW3 lineage data. Species, subspecies, portrait,
culture, language, and blood identity are unchanged.

After a foreign official receives a branch, its vanilla Clan is renamed through
the existing Xia pattern using the resolved branch:

- king Clan: kingdom name + branch + `氏`;
- other official Clan: city name + branch + `氏`.

The rename applies only to a valid Clan whose leader belongs to a kingdom using
Xiaized institutions. It does not rename unrelated foreign Clans outside that
institutional system.

## Broad Pre-Qin Kingdom Library

Create `name_generators/lib/先秦诸侯国.txt` and change the Chinese Name generator
resource to:

```json
[
  {
    "id": "Xia_kingdom",
    "templates": [
      { "format": "{先秦诸侯国}", "weight": 1 }
    ]
  }
]
```

The library contains unique names without the suffix `国`. Its scope includes:

- Western Zhou enfeoffed states and royal-domain states;
- Spring and Autumn states and documented minor states;
- Warring States polities and recognized branch states;
- named frontier polities such as `义渠`, `大荔`, `楼烦`, `林胡`, `孤竹`,
  `无终`, `犬戎`, and `山戎`.

It excludes:

- generic ethnonyms such as `东夷`, `西戎`, `北狄`, `百越`, and `群蛮`;
- Xia/Shang/Zhou royal dynasties when they are not a vassal polity name;
- Han-and-later dynasties and states;
- modern province abbreviations;
- duplicate spellings, obvious transcription errors, and entries whose only
  form ends in `国`.

A single code-side canonical list supplies the no-Chinese-Name fallback
generators and `XiaFallbackNameRules`. A focused resource test compares that list
with `先秦诸侯国.txt`, so the optional-mod and fallback configurations cannot
silently diverge.

Existing `XIA_FULL_NAME_APPLIED` behavior remains one-shot. New Xia kingdoms and
kingdoms that newly reach full Xiaization use the new library; already marked
save entities are not repeatedly renamed.

## Performance And Safety

All name parsing is pure string work performed at promotion or lineage birth.
The existing one-time foreign-official integration scan remains the only bulk
operation. No yearly world scan or per-frame naming work is added.

Empty or malformed sources fall through safely. The resolver never emits an
empty branch when a useful kingdom fallback exists, never appends `国`, and does
not overwrite an existing structured surname, branch, or given name.

## Verification

Temporary focused tests under `F:\tmp` must cover:

- delimited western names;
- Chinese no-delimiter names with `chinese_family_name`;
- Clan suffix cleanup for `家族/氏族/部落/家/族/氏`;
- repeated resolution idempotence;
- one-token/no-Clan fallback without duplicated display names;
- civilized non-human descendant lineage eligibility;
- foreign institutional Clan rename eligibility;
- word-library/code-list equality, uniqueness, minimum breadth, no `国` suffix,
  and required representative states;
- exclusion of later dynasties, modern abbreviations, and generic ethnonyms.

The final implementation must pass the focused correctness and court harnesses,
normal net48 build, `DEBUG;TRACE` build, and a Git boundary audit that leaves the
user's intentional test deletions unstaged.

# Western Regnal Chronology Design

Date: 2026-08-01

## Goal

Give civilized non-Xia hereditary monarchies a chronology that follows their
localized personal and title order, while preserving the existing Xia era and
regnal chronology behavior.

The representative Chinese presentation is:

```text
加洛韦王国 艾里斯·罗宾逊伯爵元年
```

## Scope

This change only controls the local ruler chronology used when no committed
formal era is available. It does not change era creation, era persistence,
suzerain chronology selection, reign start recovery, or historical era rows.

The existing formal-era precedence remains:

1. Use the committed local or root-suzerain era when the current chronology
   rules expose one.
2. Otherwise format the current ruler's local regnal chronology.

## Chronology Profiles

The formatting profile is resolved before composing display text:

- biological Xia uses `Xia`;
- `civ_monkey` uses `Xia`;
- a culture carrying the persisted full-entry-into-Xia trait uses `Xia`;
- civilized `human`, `elf`, `dwarf`, and `orc` use `Western` while not fully
  Xiaized;
- invalid or non-civilized kingdoms use no AW3 local regnal chronology.

`orc` keeps its nomadic naming resources but uses the Western chronology word
order. Full Xiaization changes future formatting to Xia without rewriting old
history rows.

## Xia Format

Xia and monkey chronology keeps the existing compact order:

```text
国号 + 爵位前置 + 君主名 + 年次
周伯发元年
```

The ruler component remains the Xia structured given name. Existing formal era
text such as `武德元年` continues to override this fallback.

## Western Format

Western and orc chronology uses:

```text
国家显示名 + 空格 + 完整本地化姓名 + 爵位后置 + 年次
```

The ruler component is the selected localized full personal name, including a
persisted noble family component when present. It must not be reconstructed by
parsing an already-composed display string.

Title suffixes follow the kingdom rank:

| Kingdom rank | Chinese suffix |
| --- | --- |
| Baron | 伯爵 |
| Marquis | 侯爵 |
| Duke | 公爵 |
| King | 国王 |
| Emperor | 皇帝 |

Examples:

```text
加洛韦王国 艾里斯·罗宾逊伯爵元年
布列塔尼公国 阿兰·德·雷恩公爵十二年
猴国王蒙派元年
```

The last example remains Xia-style because monkey is explicitly excluded from
Western profile assignment.

## Republic And Invalid State Handling

Republics never expose local ruler chronology, even when a stale monarch,
reign-start value, title, or era projection remains in memory. Missing ruler,
invalid kingdom, dead ruler, invalid reign year, empty state name, empty ruler
name, or unresolved rank returns an empty local chronology.

## Components

`RegnalChronologyRules` owns pure profile resolution, title localization, and
formatting. It keeps the existing Xia formatter as a compatibility overload and
adds an explicit profile-aware formatter.

`YearNameService.BuildLocalRegnalChronology` gathers runtime facts only:

- republic/hereditary state;
- current reign start and ruler identity;
- biological species, monkey status, and culture entry-into-Xia status;
- kingdom rank;
- Xia structured given name or Western localized full name.

It passes those facts to the pure rule and performs no template generation,
database writes, or culture migration while rendering.

## Performance And Failure Handling

Chronology formatting is O(1), allocation-bounded string composition. It does
not scan actors, cultures, families, or kingdoms and does not call the naming
generator. Missing profile/name/title data falls back to an empty local
chronology rather than throwing or emitting an unbounded warning.

## Verification

Pure-rule tests must prove:

- Xia and monkey preserve the current compact order;
- Western and orc use full name plus postposed localized title;
- every rank maps to the approved title suffix;
- full Xiaization changes Western formatting to Xia;
- republic and invalid inputs return empty text;
- year one uses `元年` and later years use the existing Chinese year formatter;
- a committed formal era still takes precedence over either local format.

A source guard must prove `YearNameService` uses the full localized ruler name
only for the Western profile and does not invoke the naming generator or mutate
identity while formatting.

## Acceptance Criteria

- Independent Western and orc monarchies display the approved localized order.
- Xia and monkey chronology output is unchanged.
- Fully Xiaized former Western cultures switch to Xia order.
- Republics show no monarch chronology.
- Formal era and suzerain-era precedence remains unchanged.
- Save data and existing history records are not rewritten by this feature.

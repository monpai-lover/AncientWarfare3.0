# Historical Master Identity And Vocation Design

## Goal

Correct the identity and military-career behavior of all 84 canonical school masters:

- show a historically conservative surname (姓) and clan branch (氏) in the actor window;
- name each master's original WorldBox clan as `<founder city><氏>氏`;
- prevent civilian masters from being recruited through any vanilla or AW3 military path;
- allow only historically supported masters to enter military service;
- preserve canonical names, teaching, travel, civil office, guest office, and lineage behavior.

The mod is unreleased. This change targets new worlds only and does not migrate old saves.

## Confirmed Decisions

- Use conservative historical identity data. A distinct surname is recorded only when it is sufficiently established.
- An unknown surname is stored as an empty value and rendered as localized `未详`; the literal display text is never persisted or inherited.
- Qin-Han and later figures whose surname and clan designation have merged use the same transmitted value for both fields.
- Li Er uses `李` for both fields because the transmitted record directly identifies Li as his surname designation.
- Canonical actor names remain unchanged, for example `孔丘`, `河上公`, and `乌氏倮`.
- A historical master's WorldBox clan uses the immutable founder city, never the actor's later residence or service city.
- Every canonical master is ineligible for the royal guard, slave-army cadre service, and mass rebel levy.
- Eleven explicitly listed masters may serve in normal armies, border armies, captaincies, and generalships. The other 73 are protected civilian scholars.
- Civilian protection does not prohibit kingship, city leadership, central or local civil office, guest office, teaching, travel, or school leadership.

## Why The Current Behavior Is Wrong

### Actor identity rows

`AW_UnitWindowPatch` currently shows a surname only to a pre-integration noble and shows a clan branch only to a noble or an actor in an integrated kingdom. Historical masters are projected as `LineageStatus.COMMON`, so their valid identity fields are hidden.

### Clan display name

`HistoricalMasterIdentityProjection` and the canonical-master branch of `LineageService.RenameClanByLeader` both force a short name such as `孔氏`. These branches bypass the normal Xia pattern that includes a place name. The original `ClanData.founder_city_name` already preserves the correct immutable creation city.

### Surname data

`HistoricalMasterIdentityRules.Add` currently copies every `ShiName` into `FamilyName`. This produces identities such as `孔姓、孔氏` even when the distinct identity `子姓、孔氏` is known.

### Recruitment bypasses

Vanilla recruitment calls `City.checkCanMakeWarrior` and `City.makeWarrior`, but AW3 also has independent candidate and profession-changing paths in the royal guard, slave army, mandate border army, mandate rebel levy, fief command, and general service. Several paths call `Actor.setProfession(UnitProfession.Warrior)` directly. Existing exclusions recognize vanilla `figure` or `first` traits, not `aw_historical_school_master`.

## Static Identity Model

Extend each canonical identity with an evidence state:

```text
HistoricalMasterFamilyEvidence
  KnownDistinct  -> a documented surname differs from the clan branch
  KnownSame      -> merged or directly documented surname and branch use the same value
  Unknown        -> the surname field is empty and the UI renders a localized unknown label
```

The identity remains data-driven and includes:

```text
CanonicalName
ShiName
GivenName
FamilyName
FamilyEvidence
MilitaryEligibility
```

Validation rules:

- `CanonicalName == ShiName + GivenName` remains mandatory.
- `KnownDistinct` requires non-empty unequal surname and branch values.
- `KnownSame` requires non-empty equal surname and branch values.
- `Unknown` requires an empty surname and a non-empty branch value.
- A literal localized value such as `未详` or `Unknown` is invalid in static or durable identity data.
- All 84 canonical names, IDs, and branch identities remain unique under the existing registry rules.

## Complete 84-Person Identity And Vocation Table

`同氏` below means `KnownSame`. `未详` means an empty stored surname with `Unknown` evidence. `从武` is the explicit military whitelist; `文职保护` rejects military service.

| School | Canonical name | 姓 | 氏 | Evidence | Vocation |
|---|---|---|---|---|---|
| 儒家 | 孔丘 | 子 | 孔 | 明确异姓氏 | 文职保护 |
| 儒家 | 曾参 | 姒 | 曾 | 明确异姓氏 | 文职保护 |
| 儒家 | 孔伋 | 子 | 孔 | 明确异姓氏 | 文职保护 |
| 儒家 | 孟轲 | 姬 | 孟 | 明确异姓氏 | 文职保护 |
| 儒家 | 荀况 | 未详 | 荀 | 保守未详 | 文职保护 |
| 儒家 | 董仲舒 | 董 | 董 | 同氏 | 文职保护 |
| 墨家 | 墨翟 | 未详 | 墨 | 保守未详 | 文职保护 |
| 墨家 | 禽滑釐 | 未详 | 禽 | 保守未详 | 从武 |
| 墨家 | 孟胜 | 未详 | 孟 | 保守未详 | 从武 |
| 墨家 | 相里勤 | 未详 | 相里 | 保守未详 | 文职保护 |
| 墨家 | 邓陵子 | 未详 | 邓陵 | 保守未详 | 文职保护 |
| 墨家 | 田鸠 | 未详 | 田 | 保守未详 | 文职保护 |
| 道家 | 李耳 | 李 | 李 | 传世同称 | 文职保护 |
| 道家 | 列御寇 | 未详 | 列 | 保守未详 | 文职保护 |
| 道家 | 杨朱 | 未详 | 杨 | 保守未详 | 文职保护 |
| 道家 | 庄周 | 未详 | 庄 | 保守未详 | 文职保护 |
| 道家 | 辛钘 | 未详 | 辛 | 保守未详 | 文职保护 |
| 道家 | 河上公 | 未详 | 河上 | 保守未详 | 文职保护 |
| 法家 | 李悝 | 未详 | 李 | 保守未详 | 文职保护 |
| 法家 | 公孙鞅 | 姬 | 公孙 | 明确异姓氏 | 从武 |
| 法家 | 申不害 | 未详 | 申 | 保守未详 | 文职保护 |
| 法家 | 慎到 | 未详 | 慎 | 保守未详 | 文职保护 |
| 法家 | 韩非 | 姬 | 韩 | 明确异姓氏 | 文职保护 |
| 法家 | 李斯 | 未详 | 李 | 保守未详 | 文职保护 |
| 兵家 | 孙武 | 妫 | 孙 | 明确异姓氏 | 从武 |
| 兵家 | 田穰苴 | 妫 | 田 | 明确异姓氏 | 从武 |
| 兵家 | 吴起 | 未详 | 吴 | 保守未详 | 从武 |
| 兵家 | 孙膑 | 妫 | 孙 | 明确异姓氏 | 从武 |
| 兵家 | 尉缭 | 未详 | 尉 | 保守未详 | 从武 |
| 兵家 | 白起 | 未详 | 白 | 保守未详 | 从武 |
| 纵横家 | 王诩 | 未详 | 王 | 保守未详 | 文职保护 |
| 纵横家 | 苏秦 | 未详 | 苏 | 保守未详 | 文职保护 |
| 纵横家 | 张仪 | 未详 | 张 | 保守未详 | 文职保护 |
| 纵横家 | 公孙衍 | 未详 | 公孙 | 保守未详 | 从武 |
| 纵横家 | 范雎 | 未详 | 范 | 保守未详 | 文职保护 |
| 纵横家 | 鲁仲连 | 未详 | 鲁仲 | 保守未详 | 文职保护 |
| 农家 | 许行 | 未详 | 许 | 保守未详 | 文职保护 |
| 农家 | 陈相 | 未详 | 陈 | 保守未详 | 文职保护 |
| 农家 | 陈辛 | 未详 | 陈 | 保守未详 | 文职保护 |
| 农家 | 氾胜之 | 氾 | 氾 | 同氏 | 文职保护 |
| 农家 | 贾思勰 | 贾 | 贾 | 同氏 | 文职保护 |
| 农家 | 王祯 | 王 | 王 | 同氏 | 文职保护 |
| 阴阳家 | 邹衍 | 未详 | 邹 | 保守未详 | 文职保护 |
| 阴阳家 | 邹奭 | 未详 | 邹 | 保守未详 | 文职保护 |
| 阴阳家 | 甘德 | 未详 | 甘 | 保守未详 | 文职保护 |
| 阴阳家 | 石申 | 未详 | 石 | 保守未详 | 文职保护 |
| 阴阳家 | 唐昧 | 未详 | 唐 | 保守未详 | 文职保护 |
| 阴阳家 | 落下闳 | 落下 | 落下 | 同氏 | 文职保护 |
| 名家 | 邓析 | 未详 | 邓 | 保守未详 | 文职保护 |
| 名家 | 尹文 | 未详 | 尹 | 保守未详 | 文职保护 |
| 名家 | 惠施 | 未详 | 惠 | 保守未详 | 文职保护 |
| 名家 | 公孙龙 | 未详 | 公孙 | 保守未详 | 文职保护 |
| 名家 | 宋钘 | 未详 | 宋 | 保守未详 | 文职保护 |
| 名家 | 桓团 | 未详 | 桓 | 保守未详 | 文职保护 |
| 医家 | 秦越人 | 姬 | 秦 | 明确异姓氏 | 文职保护 |
| 医家 | 文挚 | 未详 | 文 | 保守未详 | 文职保护 |
| 医家 | 淳于意 | 淳于 | 淳于 | 同氏 | 文职保护 |
| 医家 | 张机 | 张 | 张 | 同氏 | 文职保护 |
| 医家 | 华佗 | 华 | 华 | 同氏 | 文职保护 |
| 医家 | 葛洪 | 葛 | 葛 | 同氏 | 文职保护 |
| 杂家 | 尸佼 | 未详 | 尸 | 保守未详 | 文职保护 |
| 杂家 | 吕不韦 | 姜 | 吕 | 明确异姓氏 | 文职保护 |
| 杂家 | 刘安 | 刘 | 刘 | 同氏 | 文职保护 |
| 杂家 | 伍被 | 伍 | 伍 | 同氏 | 文职保护 |
| 杂家 | 苏飞 | 苏 | 苏 | 同氏 | 文职保护 |
| 杂家 | 东方朔 | 东方 | 东方 | 同氏 | 文职保护 |
| 商家 | 范蠡 | 未详 | 范 | 保守未详 | 从武 |
| 商家 | 白圭 | 未详 | 白 | 保守未详 | 文职保护 |
| 商家 | 猗顿 | 未详 | 猗 | 保守未详 | 文职保护 |
| 商家 | 乌氏倮 | 未详 | 乌氏 | 保守未详 | 文职保护 |
| 商家 | 卓王孙 | 卓 | 卓 | 同氏 | 文职保护 |
| 商家 | 桑弘羊 | 桑 | 桑 | 同氏 | 文职保护 |
| 工家 | 公输班 | 姬 | 公输 | 明确异姓氏 | 文职保护 |
| 工家 | 欧冶子 | 未详 | 欧冶 | 保守未详 | 文职保护 |
| 工家 | 干将 | 未详 | 干 | 保守未详 | 文职保护 |
| 工家 | 李冰 | 未详 | 李 | 保守未详 | 文职保护 |
| 工家 | 郑国 | 未详 | 郑 | 保守未详 | 文职保护 |
| 工家 | 丁缓 | 丁 | 丁 | 同氏 | 文职保护 |
| 史家 | 左丘明 | 未详 | 左丘 | 保守未详 | 文职保护 |
| 史家 | 司马谈 | 司马 | 司马 | 同氏 | 文职保护 |
| 史家 | 司马迁 | 司马 | 司马 | 同氏 | 文职保护 |
| 史家 | 刘向 | 刘 | 刘 | 同氏 | 文职保护 |
| 史家 | 班固 | 班 | 班 | 同氏 | 文职保护 |
| 史家 | 荀悦 | 荀 | 荀 | 同氏 | 文职保护 |

The military whitelist is therefore exactly:

```text
禽滑釐, 孟胜,
公孙鞅,
孙武, 田穰苴, 吴起, 孙膑, 尉缭, 白起,
公孙衍,
范蠡
```

Disputed or technical military associations do not grant eligibility. In particular, Mo Di and Gongshu Ban remain technical or defensive thinkers rather than ordinary soldiers, Ban Gu remains a literary staff figure, and Tang Mei remains protected because identification with the Chu general Tang Mo is disputed.

## Actor Projection And Persistence

`HistoricalMasterIdentityProjection` writes:

- `GIVEN_NAME` from the canonical given name;
- `CLAN_NAME` from the canonical branch, including compound forms such as `乌氏`;
- `FAMILY_NAME` and `CHINESE_FAMILY_NAME` from the known surname, or an empty string for `Unknown`;
- existing lineage, branch, display-name, school-master, and ability fields unchanged.

`HistoricalMasterLineageCommitIdentity` carries the evidence state. Its validity rules allow an empty surname only for an `Unknown` canonical master. `HistoricalMasterLineagePersistence` and the archive mirror store an empty surname for that case while retaining valid lineage and branch IDs. General Xia lineages keep their existing non-empty surname requirements.

The literal localized unknown label is presentation-only. Descendants of a master with an unknown surname inherit the lineage and branch but do not inherit a fabricated surname.

Canonical `Actor.data.name` and `display_name` remain the registry name. The surname change must not turn `孔丘` into `子丘` or alter any other canonical actor name.

## Original WorldBox Clan Naming

Use one pure formatter:

```text
BuildHistoricalMasterClanName(founderCityName, shiName)
  normalizedShi = remove every trailing 氏 from shiName
  return founderCityName + normalizedShi + 氏
```

Examples:

- `曲阜` + `孔` -> `曲阜孔氏`
- `某城` + `公孙` -> `某城公孙氏`
- `某城` + `乌氏` -> `某城乌氏`, never `某城乌氏氏`

Place resolution order:

1. `ClanData.founder_city_name`;
2. the city named by the committed historical master's `HometownCityId`;
3. no rename and a failed identity projection if neither authoritative source exists.

The current residence, guest-service city, current kingdom, and current king status are never naming inputs for a canonical master's original clan. A later journey or office therefore cannot rename the founder clan.

Both the canonical branch in `LineageService.RenameClanByLeader` and the exact post-creation assertion in `HistoricalMasterIdentityProjection` use the same formatter. The projection runs after `Clan.newClan` and its Harmony postfixes, so it deterministically replaces a Chinese Name template result. The operation is idempotent.

## Actor Window Presentation

`AW_UnitWindowPatch` keeps existing rules for ordinary Xia actors and adds a canonical-master branch:

- always show the identity row;
- always show `姓`;
- render the known surname or localized `未详`;
- make a known surname clickable as before;
- leave an unknown surname non-clickable;
- always show the canonical `氏` and link it to the branch family tree;
- do not require noble status or surname integration.

Add localized keys for the unknown surname value in Simplified Chinese, English, and Traditional Chinese. No hard-coded presentation text is stored in actor data or SQLite.

## Vocation Rule Model

Add a pure, WorldBox-independent rule owner:

```text
HistoricalMasterVocationRules.CanEnter(masterId, context)

contexts:
  OrdinaryWarrior
  NormalArmy
  ArmyCaptain
  BorderArmy
  General
  RoyalGuard
  SlaveArmyCadre
  RebelLevy
```

The matrix is:

| Master category | Ordinary warrior | Normal army | Captain | Border army | General | Royal guard | Slave cadre | Rebel levy |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Protected civilian | No | No | No | No | No | No | No | No |
| Military whitelist | Yes | Yes | Yes | Yes | Yes | No | No | No |

Non-canonical actors are unaffected. An actor carrying a canonical master ID that cannot be resolved fails closed for military entry; identity reconciliation may log the configuration error outside the recruitment hot path.

## Runtime Enforcement Boundaries

Use the central rule at every relevant boundary rather than relying on one trait check:

- `City.checkCanMakeWarrior`: return false for protected masters.
- `City.makeWarrior`: prefix guard prevents the method and its warrior counter increment.
- `Actor.setProfession(Warrior)`: final defense against direct AW3 or third-party promotion.
- army attachment and captain assignment: reject protected masters while always allowing removal from an army.
- `RoyalGuardService`: reject every canonical master before scoring or appointment.
- `SlaveService`: reject every canonical master from slave-army cadre and captain candidates.
- `MandateRebelService`: reject every canonical master from mass mobilization.
- `MandateBorderDefenseService`: admit only the military whitelist.
- `GeneralService`: admit and retain only whitelisted canonical masters.
- `FiefMilitaryService`: relies on the same warrior and general gates.

Candidate filters remain explicit even with final Harmony defenses. This avoids repeatedly selecting an actor that will later be rejected and keeps maintenance work bounded.

## School Travel And Civil Careers

A whitelisted master who becomes a warrior, joins an army, becomes a captain, or becomes a general is considered bound to military service. `HistoricalSchoolTravelService.IsServingOrBound` pauses destination selection and travelling behavior for that period. After the military binding ends, the existing annual school scheduler restores the scholar job and ordinary school activity.

Protected masters and military masters remain eligible for civil office under existing court rules. This feature does not alter:

- kingship or city leadership selection;
- central, local, medical, tutor, or guest-office eligibility;
- school membership and traits;
- teaching, debate, institution, lineage-leader, or biography behavior;
- mortality or canonical name protection.

## Failure Handling And Recovery

- Identity validation fails before projection for contradictory evidence or literal unknown labels.
- A missing authoritative founder city never produces a short or current-city fallback name.
- Repeated actor projection, clan rename, and UI refresh are idempotent.
- A blocked `City.makeWarrior` never increments `warriors_current`.
- Army removal and `stopBeingWarrior` remain allowed even for protected masters.
- Missing master definitions fail closed for military entry without database access or per-frame logging.
- No old-save migration, world scan, or legacy master repair is added.

## Performance

- Canonical identity and vocation lookups are static dictionary lookups.
- Recruitment, army, guard, slave, rebel, border, and general gates perform no SQLite reads.
- Candidate services filter before scoring and sorting.
- No new per-frame world scans are introduced.
- UI reads only the selected actor's existing projected fields and registry definition.

## Verification

### Pure identity tests

- exactly 84 valid identities across 14 schools;
- exact canonical-name reconstruction for every entry;
- all known/unknown evidence invariants;
- exact known identities, including `子/孔/丘` for Kong Qiu;
- unknown surnames remain empty and never equal localized labels;
- suffix normalization for `孔`, `公孙`, and `乌氏`;
- founder-city clan names remain unchanged when residence changes.

### Pure vocation tests

- exactly 11 military-eligible and 73 protected masters;
- the whitelist matches the approved names exactly;
- the full eight-context matrix for every master;
- all canonical masters rejected from royal guard, slave cadre, and rebel levy;
- unresolved canonical IDs fail closed and non-canonical actors remain unaffected.

### Source and integration tests

- actor-window master rows bypass noble/integration visibility gates;
- unknown surname rows are localized and non-clickable;
- historical identity SQLite accepts an empty unknown surname while retaining lineage and branch IDs;
- clan creation ends with the founder-city name even when Chinese Name is present;
- `City.makeWarrior` rejection does not alter warrior counts;
- direct profession, army, captain, guard, slave, rebel, border, general, and fief paths obey the central matrix;
- active military masters pause school travel and resume after service.

### Full regression gate

- historical school rules;
- historical master lineage SQLite;
- historical master Spawn Harmony;
- court/career and guest-office atomic SQLite suites;
- pathfinding, inheritance, correctness, court, and existing military-rule suites;
- locale column/key validation and `git diff --check`;
- Debug and Release full rebuilds with zero warnings and zero errors;
- deployment to the loaded mod directory while preserving `.runtime`.

Live functional acceptance on a new world must confirm Kong Qiu's actor rows, a founder-city clan title, protected-master recruitment rejection, and successful military service for at least one whitelisted master.

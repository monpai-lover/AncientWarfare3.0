# AW3 Expanded Court, Schools, And National Direction Design

## Goal

Upgrade the existing court system from a compact officer list into a wide,
rank-ordered living court. The window must show the king, central officials,
specialists, generals, and local governors as real actors; preserve their office
history in genealogy and biography; add Medical and Yin-Yang specialist offices;
expand the court to fourteen schools; and turn the combined court into a cached,
continuous influence on national livelihood, aggression, and peace strategy.

The feature builds on the existing `CourtService`, `CourtWindow`, lineage archive,
general system, city-leader system, policy AI, war AI, and vassal AI. It does not
create a second set of duplicate actors or a parallel diplomacy engine.

## Wide Rank-Pyramid Court Window

### Window Shell And Navigation

`CourtWindow` remains a separate window. It reuses the proven wide shell and
navigation behavior of `KingdomPolicyWindow`:

- nominal window size `560x360`;
- draggable window frame;
- a fixed top summary area;
- a movable rank canvas below the summary;
- pointer drag pans the canvas through `TreeDragPanHandler`;
- mouse-wheel zoom ranges from `0.25` to `2.0`;
- opening or changing kingdoms resets to a sensible fit-to-content position.

The top summary remains readable while the canvas moves. It shows kingdom name
and flag, government, court level/efficiency, dominant schools, and the three
cached national-direction tendencies. Panning and zooming affect only the rank
canvas.

### Rank Layout

Actors are arranged from top to bottom by official rank, not by school:

1. King at the apex.
2. Three Excellencies and Three Departments.
3. Nine Ministers and Six Ministries.
4. Imperial Physician and Imperial Astrologer.
5. All generals as the military faction.
6. All city leaders/governors as the local-official tier.

Each tier uses as many columns as needed and centers shorter rows under the tier
above, producing a readable pyramid rather than one extremely wide row. The
canvas computes node positions from role rank and stable ordering. Central office
slots stay visible as vacancy placeholders; dynamic general and governor rows
omit dead or invalid actors.

An actor holding multiple roles appears once at the highest-ranked role. The node
subtitle and tooltip list concurrent roles, for example `Crown Prince / Central
Secretariat Director` or `General / Minister of War`. This prevents duplicate
portraits and duplicate influence.

### Actor Nodes

Living actor portraits reuse the same `UiUnitAvatarElement` creation path as
`FamilyTreeNodeView`; the court does not generate static substitute portraits.
Every occupied node includes:

- live portrait;
- kingdom flag;
- actor name colored with the kingdom color;
- a darker kingdom-color border that remains visible behind the portrait;
- primary office and school icon/label;
- a tooltip containing age, ability summary, all offices, school, appointment
  year, and city when applicable;
- click behavior that opens the normal actor window.

The UI builds from cached court records plus existing general and city-leader
collections. Opening the window must not rescan the world, recalculate factions,
or run appointment AI. Dead or stale active database rows are skipped during UI
construction and closed by low-frequency court maintenance.

## Offices And School Assignment

### Specialist Offices

Add two central specialist offices:

- Imperial Physician / `太医令`: candidate scoring strongly favors intelligence
  and stewardship; its school is Medical.
- Imperial Astrologer / `太史令`: candidate scoring strongly favors intelligence
  and diplomacy; its school is Yin-Yang.

Both offices occupy the specialist court tier, participate in court influence,
use normal appointment/dismissal/history records, and may remain vacant when no
eligible actor exists.

### Fourteen Schools

The complete school set is:

1. Confucian / 儒家
2. Legalist / 法家
3. Daoist / 道家
4. Mohist / 墨家
5. Military / 兵家
6. Diplomat / 纵横家
7. Agrarian / 农家
8. Yin-Yang / 阴阳家
9. Logician / 名家
10. Medical / 医家
11. Syncretist / 杂家
12. Merchant / 商家
13. Craftsman / 工家
14. Historian / 史家

School assignment is deterministic but not globally hardcoded by office. It
scores role preference, the actor's relevant stats, existing traits, and a stable
actor-ID tie-break. The following roles have strong constraints:

- Imperial Physician is Medical.
- Imperial Astrologer is Yin-Yang.
- Generals strongly prefer Military.
- Revenue offices prefer Agrarian or Merchant.
- Works offices prefer Mohist or Craftsman.
- Other central and local offices select the best-fitting school from their role
  profile and actor attributes.

Re-running maintenance with unchanged actors and roles must not randomly change
schools.

### Icon Contract

School trait icons use IDs below `ui/Icons/traits/`. Existing icons are reused,
including the user-provided `iconnong.png`. Its registration must use the exact
case-sensitive ID `ui/Icons/traits/iconnong`, replacing the current incorrect
`ui/icons/iconnong` path.

The user-authored assets are:

- `iconnong.png`;
- `iconmingjia.png`;
- `iconzajia.png`;
- `iconshangjia.png`;
- `icongongjia.png`;
- `iconshijia.png`.

Their source dimensions may differ. Court and trait UI renders every school icon
inside a fixed `52x52` display slot, preserves the source aspect ratio, centers
the image, and never assumes that the PNG itself is square. Royal medical care
reuses the existing `ui/Icons/traits/icondanyao` image and does not require a
separate `iconyuyizhaohu.png`.

## Imperial Medical Care

### Eligibility And Effects

Once per world year, a living, active Imperial Physician may treat the current
king and currently registered heir when all three actors still belong to the same
kingdom. The care state provides approximately:

- `+50%` maximum health through the existing health multiplier stat;
- `+15` lifespan through the existing lifespan stat;
- full annual healing through `Actor.restoreHealthPercent`;
- removal of actor traits whose `ActorTrait.can_be_cured` flag is true.

The bonus is represented by one managed care trait/state and cannot stack from
multiple refreshes. It transfers when the heir changes and is removed when the
physician dies, is dismissed, defects, leaves the kingdom, or otherwise becomes
inactive. A new valid physician may restore care at the next maintenance event.

Medical care improves health but does not provide immortality. It does not block
battle death, execution, disaster death, scripted death, or the final extreme-age
death path. The service uses public trait metadata rather than a hardcoded list of
diseases.

### History Noise Control

Routine healing and yearly bonus refreshes do not write biography entries. A
personal biography entry is written only when at least one curable trait was
actually removed. The record identifies the treated king or heir and the acting
Imperial Physician without producing annual history spam.

## Office History, Genealogy, And Archive

Every central officer appointment, reassignment, and dismissal writes personal
biography regardless of whether the actor is noble, famous, or otherwise marked
important. Existing importance gates in `ChronicleEvents` are removed for these
personal office events. National history may retain its stricter importance
filter to avoid flooding the kingdom chronicle.

`CourtOfficer` remains the source of office terms:

- appointment creates a term with office and start year;
- reassignment closes the old term and creates the new term;
- dismissal, death, or invalidation closes the active term;
- each transition synchronizes the actor archive.

The family tree and actor archive expose office information through their social
title resolution:

- a living actor reads the current `COURT_OFFICE_ID` plus concurrent crown-prince,
  general, or governor roles;
- a dead actor retains the last archived office title and color;
- combined live labels use stable precedence, such as `世子 · 中书令` and
  `大将 · 兵部尚书`;
- city leaders continue to display `城市名 太守`;
- kingdom color is preserved in both the live tree and the death archive.

The UI skips an invalid active officer row immediately; maintenance closes that
row and updates the archive rather than leaving a permanently active dead office.

## Continuous National Direction

### Influence Model

Each kingdom maintains three independent continuous tendencies:

- Livelihood: population, agriculture, economy, medicine, construction, and
  recovery.
- Aggression: claims, offensive war, annexation, and forced vassalization.
- Peace: alliances, voluntary vassalization, restraint, and negotiated peace.

The values are not mutually exclusive and do not collapse into one enum. A state
can be economically focused and aggressive at the same time. Values are
normalized and clamped before AI consumption so no court composition can turn a
probability adjustment into an unconditional action.

The king supplies approximately `25%` of total influence. Court officers,
generals, and local governors supply approximately `75%`:

- Three Excellencies and Three Departments have the greatest office weight.
- Nine Ministers and Six Ministries have the next weight.
- specialist offices have a moderate weight.
- generals scale within a bounded range using military merit and rank.
- city leaders/governors provide a small local-administration weight.

An actor is counted once at the weight of the highest role. Concurrent lower
roles provide only a small bounded supplement. The king's school and traits
define the royal vector; stewardship/intelligence lightly adjust livelihood,
warfare lightly adjusts aggression, and diplomacy lightly adjusts peace. Stats
remain modifiers rather than overriding school identity.

### School Direction Vectors

- Confucian: livelihood and peace, with stability emphasis.
- Legalist: aggression, centralization, and forced vassalization.
- Daoist: peace and reduced opportunistic war.
- Mohist: livelihood, peace, engineering, and defense.
- Military: aggression, readiness, and willingness to continue a viable war.
- Diplomat: peace, alliance, voluntary vassalization, and diplomatic leverage.
- Agrarian: livelihood, agriculture, and population recovery.
- Yin-Yang: balanced direction, culture, and policy research.
- Logician: policy/diplomatic research and a small peace tendency.
- Medical: livelihood, health, and population recovery.
- Syncretist: balanced contributions and reduced extreme specialization.
- Merchant: livelihood, economy, trade, and a small peace tendency.
- Craftsman: livelihood, construction, equipment, and engineering research.
- Historian: livelihood, stability, and reduced unjustified war.

### Recalculation And Cache

`CourtService` owns a compact kingdom direction snapshot. It recalculates at
appointment/dismissal, king change, major general change, and low-frequency
yearly maintenance. The yearly path processes each kingdom once and does not sort
all world actors. AI and UI consumers only read the snapshot.

Missing, dead, foreign, or inactive actors are discarded during recomputation.
If no valid court exists, the system returns neutral bounded tendencies rather
than stale values or an exception.

## AI Integration

Direction values are score/probability biases, not permission gates:

- Livelihood increases agrarian, economic, population, medical, and engineering
  policy/research scores and reduces opportunistic offensive wars.
- Aggression increases claim fabrication, ordinary offensive-war selection,
  annexation, and forced-vassalization scores.
- Peace increases alliances and voluntary vassalization, suppresses new
  opportunistic wars, and increases white-peace willingness in long, stalled, or
  losing wars.

`CourtAIRules` and existing policy selection consume livelihood-oriented school
weights. `WarDecisionAI` consumes aggression and peace only after existing legal
and strategic eligibility checks. `VassalAIService` consumes aggression for
forced vassalization and peace for voluntary submission, but neither value can
bypass the new direct-border rule. Existing peace-settlement logic receives the
bounded peace/aggression adjustment after it evaluates duration and military
position.

Mandate wars, defensive wars, and independence wars remain possible regardless
of court direction. Direction never fabricates legality, removes an existing
casus belli requirement, or forces a losing state to continue forever.

## Performance Constraints

- Do not calculate court direction every frame or every AI decision.
- Do not traverse the world when opening or panning the court window.
- Reuse live actor references and cached role records; do not create duplicate
  court actors.
- Batch portrait construction when a very large kingdom has many generals or
  governors, and destroy/reuse old node views when switching kingdoms.
- Do not poll medical care per frame; apply and reconcile it during yearly court
  maintenance and explicit appointment/heir-change events.
- Keep direction modifiers pure and bounded so focused rule tests can run without
  loading the game world.

## Verification

Because repository tests were intentionally removed, implementation uses
temporary TDD harnesses under `F:\tmp` and leaves them out of version control.

Required verification includes:

- role rank produces the specified pyramid order;
- multi-role actors appear once with combined labels and one influence entry;
- empty central offices render vacancies while dead dynamic actors are skipped;
- pan and zoom stay within range and the top summary remains fixed;
- clicking an occupied node opens the actor window;
- school assignment is deterministic and honors fixed/strong role preferences;
- all fourteen trait registrations resolve their exact resource IDs, including
  the corrected Agrarian path and reused medical-care icon, and variable-size
  source PNGs render centered without stretching;
- a valid Imperial Physician treats only the same kingdom's king and current
  heir, does not stack bonuses, and removes only `can_be_cured` traits;
- physician loss and heir replacement remove or transfer care correctly;
- routine healing creates no biography spam and a real cure creates one entry;
- every central appointment/reassignment/dismissal creates a personal office
  history entry and synchronizes the archive;
- live and dead family-tree nodes retain correct office and kingdom color;
- rank weights normalize to approximately 25% king and 75% court/military/local
  influence without duplicate actors;
- livelihood, aggression, and peace produce the intended bounded AI score shifts;
- national direction cannot bypass war legality, Mandate exceptions, or vassal
  adjacency;
- opening and interacting with a large court window causes no world scan and no
  recurring high-cost allocations.

Final verification runs temporary focused rules, `dotnet build
AncientWarfare3.csproj`, both relevant optional Chinese Name configurations if
shared compilation is affected, manual UI smoke checks at default/min/max zoom,
and a git diff/status audit that excludes all user-owned changes.

## Non-Goals

- Do not replace the policy window or merge the court into it.
- Do not add a second actor population for officials or generals.
- Do not implement elections, examinations, hereditary bureaucracy, or a full
  faction civil-war system in this pass.
- Do not let court direction hard-block defense, independence, or Mandate wars.
- Do not write routine annual medical care to biography.
- Do not require a separate royal-medical-care icon.

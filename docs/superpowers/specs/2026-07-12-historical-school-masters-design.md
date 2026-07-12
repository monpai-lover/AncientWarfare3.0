# Historical School Masters And Hundred Schools Design

## Goal

Replace spontaneous school assignment with a historical-person-driven school ecosystem. Eighty-four unique historical masters descend into Xia kingdoms, travel between states, lecture, persuade rulers, recruit real disciples, debate rival schools, establish institutions, and temporarily serve foreign courts without changing nationality.

The system must create visible competition among all fourteen fixed schools while preserving historical causality: no city or ordinary official receives a school without a teacher, lecture, text, institution, conversion event, or explicit historical source.

## Fixed Decisions

- Each historical master descends once per world and never reincarnates.
- Real disciples, later generations, preserved texts, and institutions continue a school after a master's death.
- Masters use hybrid travel: physical land travel, physical boats where possible, and a timed sea-voyage fallback only after prolonged transport failure.
- Descents occur automatically by world stage, not by policy or technology.
- Foreign service lasts a bounded term, normally 8-20 years.
- Masters are mortal with moderate health, lifespan, and travel-safety bonuses.
- Each of fourteen schools has six masters, for eighty-four total.
- Men and women may study, teach, debate, travel, and succeed to leadership of a teaching lineage. Existing male-only central-office rules remain.
- A person belongs to one formal school at a time.
- Travel targets are selected entirely by AI.
- Institutions are part of the first release.
- A city stores multiple institutions but renders only its leading academic landmark on the map.
- On-screen debates receive a physical presentation; off-screen debates use identical background rules.
- Masters and sufficiently reputable direct or later-generation disciples may formally debate.
- Debate loss never directly forces school conversion.
- Historical masters descend only in living Xia kingdoms.

## Historical Registry

The order within each row is the school's descent order. The spellings in this table are canonical and must be used exactly as written, including `田鸠`, `禽滑釐`, `氾胜之`, `宋钘`, and `乌氏倮`; alternate spellings are search/display aliases only.

| School | Six unique historical masters |
|---|---|
| Ru | 孔子, 曾子, 子思, 孟子, 荀子, 董仲舒 |
| Mohist | 墨子, 禽滑釐, 孟胜, 相里勤, 邓陵子, 田鸠 |
| Dao | 老子, 列子, 杨朱, 庄子, 文子, 河上公 |
| Legalist | 李悝, 商鞅, 申不害, 慎到, 韩非, 李斯 |
| Military | 孙武, 司马穰苴, 吴起, 孙膑, 尉缭, 白起 |
| Diplomat | 鬼谷子, 苏秦, 张仪, 公孙衍, 范雎, 鲁仲连 |
| Agrarian | 许行, 陈相, 陈辛, 氾胜之, 贾思勰, 王祯 |
| Yin-Yang | 邹衍, 邹奭, 甘德, 石申, 唐昧, 落下闳 |
| Logician | 邓析, 尹文, 惠施, 公孙龙, 宋钘, 桓团 |
| Medical | 扁鹊, 文挚, 淳于意, 张仲景, 华佗, 葛洪 |
| Syncretist | 尸佼, 吕不韦, 刘安, 伍被, 苏飞, 东方朔 |
| Merchant | 范蠡, 白圭, 猗顿, 乌氏倮, 卓王孙, 桑弘羊 |
| Craftsman | 公输班, 欧冶子, 干将, 李冰, 郑国, 丁缓 |
| Historian | 左丘明, 司马谈, 司马迁, 刘向, 班固, 荀悦 |

Every registry entry defines:

- a stable ASCII ID;
- canonical display name and aliases;
- school ID;
- descent order and wave;
- preferred historical home-state names;
- age, sex, ability profile, and signature debate topics;
- canonical works or institution preferences;
- Xia actor asset and naming metadata.

Historical names are protected from random name generation, Xia naming repair, office-derived surname grants, and Chinese Name integration. A master may establish a normal clan only when the registry explicitly defines that lineage; display identity remains canonical.

## Descent Scheduler

The scheduler starts only after at least one living Xia kingdom owns a living city. If no Xia state exists, all descent clocks pause without consuming a historical entry.

Five waves create an evolving intellectual era. Within each school, roster entry 1 belongs to wave 1, entry 2 to wave 2, entries 3 and 4 to wave 3, entry 5 to wave 4, and entry 6 to wave 5.

1. Founders begin after 10 eligible world years.
2. Early transmitters begin after 35 eligible years.
3. The principal contention wave begins after 70 eligible years.
4. Institutional synthesizers begin after 120 eligible years.
5. Later scholars begin after 180 eligible years.

An eligible year is a year during which at least one living Xia kingdom owns at least one living city. The counter pauses otherwise. At most two masters descend in one eligible year. Within each open wave, the scheduler rotates across schools before giving a school its next slot, so the two-per-year cap cannot starve a school. The first five entries of every school must have descended by 240 eligible years; all sixth entries must have descended by 300 eligible years. An entry remains queued without being consumed whenever no valid Xia home city exists at its scheduling point.

Home selection order:

1. a living Xia kingdom whose current name matches a preferred historical state;
2. an underrepresented Xia kingdom with a suitable capital or developed city;
3. any living Xia kingdom, weighted against repeatedly receiving masters.

The actor joins the chosen city once at descent so vanilla nationality is established correctly. The system then records immutable home identity.

Each descent produces a visible effect, world log, city history entry, personal biography entry, school event, and map notification.

## Identity And Residence

The model separates four concepts:

```text
nationality -> immutable historical home kingdom ID and name
hometown -> original descent city
residence -> current school activity city
service kingdom -> current foreign or domestic court, if appointed
```

Vanilla `Actor.joinCity` and `Actor.setCity` cannot represent foreign residence because they also change kingdom. Historical travellers therefore never call either method merely to travel or reside abroad. `HistoricalAffiliationService` stores immutable nationality and hometown, while `HistoricalResidenceService` stores independent residence and service affiliations. While the original home city and kingdom remain valid, the actor keeps those vanilla pointers. If they are destroyed or become unsafe for engine references, vanilla pointers may be repaired to a safe living container, but this never rewrites historical nationality or presents the actor as naturalized. School, biography, guest, and UI logic use the affiliation services as their source of truth.

Vanilla migration, army recruitment, and city reassignment hooks must not silently naturalize an active historical traveller. Any unavoidable engine-side affiliation drift is detected and repaired on the main thread, with capture, enslavement, and authored naturalization handled only as explicit lifecycle events.

All school-specific systems resolve residence through one API:

- city influence;
- school membership indexes;
- lectures and discipleship;
- debates;
- institutions;
- guest appointment eligibility;
- school UI and map mode;
- biography and history.

The actor physically travels to and remains near the residence city. The city UI labels the relationship as guest residence rather than citizenship.

## Historical Trait And Mortality

All masters receive one common historical-figure trait plus their permanent school trait.

The historical trait grants moderate lifespan, health, disease resistance, and travel safety. It does not grant immortality or immunity to war, accidents, disease, or player actions.

A guest-protection status prevents the residence/service kingdom from treating the master as a hostile national. Protection applies only while the guest relationship is valid. It does not make the actor invulnerable to third-party occupation, disasters, or collateral damage.

Death permanently closes the registry entry, records the cause and location, ends foreign service and travel, transfers lineage leadership where possible, and never schedules the same master again.

## Travel State Machine

```text
AtHome -> ChoosingDestination -> Travelling -> Resident
Resident -> Lecturing | Persuading | Recruiting | Debating | Serving
Serving -> Renewing | Resigning | Dismissed
Resident/Serving -> ChoosingDestination | Retired | Dead
```

Large decisions occur at annual or quarterly intervals, not every frame.

Destination scoring includes:

- city population, development, and capital importance;
- low presence of the traveller's school;
- rival masters who can debate;
- potential disciples;
- ruler receptiveness and suitable open offices;
- active city issues matching the school;
- recent visits and minimum return cooldowns;
- war, occupation, disaster, and transport availability.

Land travel uses the global streaming pathfinder. Cross-sea travel first requests a dock/boat route. After repeated transport failure and a long waiting threshold, a historical master may enter an off-map timed voyage based on distance, then arrive at the destination dock. This exception is limited to historical masters in the first release; ordinary actors and later disciples never receive teleport-like pathfinding fallback. While in timed voyage the master is removed from physical interaction, contributes no city influence, holds no active office, and cannot debate. Arrival is a school lifecycle transition, not a successful global pathfinding request.

## School Membership

Automatic school resolution is removed.

`SchoolMembershipService` becomes the sole authority for formal school identity. A valid membership row includes an explicit source record. `CourtService.EnsurePersonalSchool` projects that authoritative membership onto actor data and traits, or returns `None`; an unbacked legacy school string or trait is cleared rather than accepted. It no longer derives a school from stats, actor ID jitter, parents, city dominance, nationality, residence, office type, or any default value. A schoolless actor may still hold office but contributes no school direction and is displayed as schoolless rather than Confucian.

Valid entry paths are:

- historical descent;
- direct discipleship;
- later-generation discipleship;
- explicit conversion after prolonged exposure;
- study of a preserved work after a lineage becomes extinct;
- an explicitly authored historical event.

School identity persists after dismissal and is not inherited biologically.

One person has one formal school. The membership record is authoritative and the school trait is only its synchronized presentation. Historical masters never convert. Debate never forces conversion. Ordinary members may convert only after multiple years without their own teachers, overwhelming rival exposure, and an explicit recorded conversion event that closes the prior membership before opening the next one.

## Disciples And Lineage

Successful lectures select real residents from the current city. Candidate weights favor ability, interest, and availability but do not create a new actor.

- A master recruits at most one or two direct disciples per year.
- Direct disciples are capped per master.
- Every membership records teacher, school, city, year, and generation.
- Direct and reputable later-generation disciples can teach, travel, debate, and establish institutions.
- Ordinary members contribute local support but do not run long-range travel AI.
- The number of simultaneous non-historical itinerants is capped per school.

On a lineage leader's death, the highest suitable living direct disciple is chosen using reputation, learning, debate history, and follower count. The successor retains their own name and is displayed as a numbered lineage holder, never as a reincarnated master.

If all living members disappear, preserved texts and institutions may later produce a documented rediscovery event. A real local reader becomes the new lineage source; the city is never assigned a school without that source.

## City Influence Model

Each non-zero city-school record exposes five components:

```text
total = tradition + membership + institutions + active presence + momentum
```

- `Tradition` is persistent, grows through founding, repeated teaching, canonical works, and major victories, and decays only after prolonged inactivity.
- `Membership` is computed from real living members: masters, direct disciples, later disciples, and capped ordinary followers.
- `Institutions` comes from valid school facilities and preserved works.
- `Active presence` comes from current resident travellers and school members holding local or court office.
- `Momentum` is signed recent publicity from lectures, persuasion, debates, patronage, expulsion, or scandal and decays quickly toward zero.

Only non-zero city-school rows are stored. Shares and the dominant school are calculated from positive totals. The existing city snapshot and map dirty queues are retained but rebuilt from the new ledger and indexes.

The UI shows each component rather than an unexplained single bar.

## Lectures, Persuasion, And Works

A resident master or qualified disciple periodically chooses one action:

- public lecture;
- private discipleship;
- court persuasion;
- canonical writing;
- institution founding;
- formal debate;
- rest, travel, service, or retirement.

Success depends on actor ability, reputation, school-topic fit, local needs, ruler attitude, rival pressure, and diminishing returns from already dominant influence.

Writing creates an explicit work record tied to author, school, city, and year. Works strengthen institutions and allow later rediscovery; they do not globally add influence without a transmission path.

## Debate System

A city may schedule at most one formal debate per year. Eligible participants are historical masters or sufficiently reputable direct/later disciples from different schools who share the residence city and are off cooldown.

The city selects a topic from real conditions:

- livelihood and famine;
- war and defense;
- aggression and expansion;
- peace and diplomacy;
- order and legal reform;
- commerce;
- technology and institutions;
- medicine and epidemic response where applicable.

The existing `CourtSchoolDirection` values provide school-topic affinity.

Debate score combines normalized intelligence, diplomacy, stewardship where relevant, reputation, debate experience, topic affinity, local support, and a small bounded random term. Margins produce decisive win, narrow win, or draw.

- Wins increase personal reputation and local momentum; major wins add a small amount of tradition.
- Losses reduce momentum and reputation but do not erase tradition or force conversion.
- Draws grant both participants publicity with little share movement.
- Underdog wins have larger momentum rewards.
- Repeated dominance has diminishing returns.

When the city is on screen, participants walk to the leading landmark or city center and receive a visible debate status/effect before resolution. A failed presentation path or bounded presentation timeout skips only the staging animation; it resolves the same pre-seeded debate exactly once. Off-screen debates use the same participants, topic, seed, formula, and result without forcing rendering.

Major debates enter world history, city history, both biographies, and the school event list.

## Foreign Guest Office

Historical masters and qualified itinerant disciples may serve a court whose kingdom differs from `actor.kingdom`.

Guest eligibility requires:

- a living, valid actor;
- current residence in a city of the inviting kingdom;
- no active office in another kingdom;
- no king/slave/madness conflict;
- compliance with the existing male-only central-office rule;
- adequate reputation and office fit.

Appointment stores a service kingdom independently from nationality. The normal term is 8-20 years. During service the actor stops long-range travel, contributes court influence to the service kingdom, and may renew, resign, or be dismissed.

All officer validation, court direction, career records, UI, city influence, and biography use a shared service-affiliation resolver. This prevents annual validation from dismissing a valid foreign guest.

War between home and service kingdoms does not automatically change nationality or force participation. Valid guest protection prevents the host from treating the actor as an enemy. Capture, occupation, court collapse, or city loss may end service and trigger escape, captivity, or death.

## Institutions And Landmark

Institutions never spawn from city stats. A master, lineage holder, or qualifying state action must create them. A state action still requires an existing member, transmitted work, or other explicit school source and therefore cannot create a school from zero.

Examples include academy, Mohist lodge, Dao hall, legal school, military school, clinic, historical archive, and craft workshop.

Each record stores school, city, founder, founding year, level, active custodian, preserved works, and condition.

A city may contain multiple logical institutions. Only the strongest active institution is rendered as the city's academic landmark to avoid building clutter. A change of leading institution updates the landmark while preserving every logical record.

Destroyed, occupied, or unstaffed institutions lose condition and influence. Preserved works may survive even when the landmark changes.

## UI And History

The school browser gains:

- school totals and influence-source breakdown;
- an eighty-four-master gallery with live/dead portraits and current state;
- teacher/disciple lineage trees;
- top cities, kingdoms, institutions, and works;
- recent lectures, journeys, debates, appointments, and deaths.

The dedicated school-city bottom tab gains:

- immediate influence composition;
- source breakdown for every present school;
- resident masters and reputable disciples;
- leading landmark;
- recent and pending debate information.

Actor biography gains descent, home identity, residences, journeys, teachers, disciples, debates, works, foreign offices, retirement, and death.

The map mode continues to color by current school shares and uses the real city selection tab implemented earlier.

## Persistence

New normalized records cover:

- historical master spawn and lifecycle state;
- residence, destination, voyage, and service state;
- school memberships and teacher lineage;
- non-zero city-school influence components;
- institutions and preserved works;
- debates and their deterministic inputs/results.

Actor data stores only hot identity and scheduling keys. SQLite stores durable history and indexes. No old-save migration is required because the mod is unreleased; new-world initialization is authoritative.

## Performance

- descent and major action scheduling is annual/quarterly;
- at most two descents occur per year;
- at most one formal debate occurs per city per year;
- city resident and school membership indexes avoid world scans;
- ordinary followers do not receive travelling AI;
- active itinerants are capped by school;
- city-school snapshots use dirty queues and bounded frame budgets;
- off-screen presentation never activates portraits or forces pathing for visual effect;
- map mode reads cached snapshots rather than recomputing histories.

## Validation

Rule tests prove:

- exactly fourteen schools and eighty-four unique master IDs;
- six masters per school;
- one-time descent, exact roster-to-wave mapping, fair scheduling, and the 240/300-eligible-year guarantees;
- Xia-only home selection and paused queues without Xia states;
- canonical-name protection;
- no stat/parent/city/nationality/office/random/default automatic school assignment and no unbacked legacy membership;
- single-school membership and explicit conversion;
- lineage succession without reincarnation;
- residence/nationality/service separation;
- foreign guest annual validation;
- influence component math, decay, and diminishing returns;
- debate eligibility, cooldown, deterministic resolution, and no forced conversion;
- institution creation requires a real source;
- travel state and timed-voyage restrictions.

Live acceptance requires:

- a fresh world starts with no spontaneous city schools;
- masters descend only after a Xia state exists;
- all schools receive their first five masters by 240 eligible years, all eighty-four masters by 300 eligible years, and no master duplicates;
- masters visibly travel, lecture, recruit, debate, serve foreign courts, resign, and continue travelling;
- nationality remains the home kingdom across every residence and office;
- death is permanent and disciples continue the lineage;
- city/map UI explains every influence source;
- on-screen debates render and off-screen debates produce equivalent records;
- institutions and the leading landmark change through explicit actions;
- long simulations remain bounded without annual frame spikes;
- the combined feature works both with AW3's embedded pathfinder and when AW3 yields to Cultiway.

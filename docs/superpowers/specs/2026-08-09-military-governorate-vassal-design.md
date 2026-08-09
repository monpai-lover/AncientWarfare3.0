# Military Governorate Vassal Design

## Goal

Allow Xia-system and fully Xia-assimilated kingdoms that exceed their direct city limit to release frontier cities as real military-governorate vassal kingdoms. Each governorate is ruled by a general, uses a regional command name such as `天平军`, has no independent diplomacy, and owes complete military service to its suzerain.

## Domain Boundary

A military governorate is not a feudatory. A feudatory remains an internal administrative layer inside its parent kingdom. A military governorate is a real child `Kingdom` with its own cities, population, armies, ruler, succession, and runtime identity, connected to its suzerain through the vassal graph.

The feature is available only to Xia-system and fully Xia-assimilated kingdoms. Western and other civilizations continue to use their own government and vassal systems.

## Data Model

Extend the authoritative `VassalRelation` row with a subject kind:

- `ordinary`
- `military_governorate`
- existing tributary semantics remain separate

Military governorates use zero tribute and a fixed military obligation of 100. Their unique state is persisted in a dedicated `MilitaryGovernorateState` row containing:

- relation, subject kingdom, and suzerain kingdom IDs;
- seat city and command name;
- governor actor and designated successor actor IDs;
- creation year, succession state, active state, end time, and end reason.

The already-migrated `expeditionary_army_id` column remains as an unused
compatibility field. No runtime logic, UI control, or recovery work may depend
on it.

Runtime kingdom data receives a derived subject-kind projection for hot reads. Persistence remains authoritative; runtime projections may be rebuilt after loading.

## Eligibility And Creation

A kingdom may create a military governorate only when `countCities() > getMaxCities()`. The seat must:

- belong directly to the creating kingdom;
- not be its capital;
- not belong to another special administrative unit;
- border a civilized kingdom outside the creator's complete suzerain-vassal network.

The border test reuses the existing external-network frontier semantics. It must not treat the suzerain, sibling subjects, or other kingdoms under the same root suzerain as foreign frontiers.

Players select an eligible frontier city and then select an eligible active general. AI kingdoms use the same rules after remaining over the limit, process bounded city and general candidate sets, and create at most one governorate per kingdom per year.

Creation reuses the transactional shape of `RoyalEnfeoffmentService`:

1. create a real civilization kingdom through the original kingdom API;
2. transfer the seat city;
3. move the selected general, make that actor the engine-level king, and set the capital;
4. create a military-governorate vassal relation;
5. persist governorate state, command name, military contract, and color synchronization;
6. write kingdom, city, and actor chronicle events.

Any failure rolls back the actor, city, kingdom, relation, persisted state, and runtime projections.

## Government, Naming, And Display

The engine-level ruler remains a `king` so native kingdom behavior continues to work. Presentation changes are relationship-driven:

- ruler title: `将军`;
- designated successor title: `留后`;
- kingdom suffix: `军`.

The command name is generated once from the seat region and persisted, for example `天平军`. Later city renames do not silently rewrite the command name. The existing kingdom rename flow may explicitly rename it.

Creation keeps the temporary general-candidate window, which lists a bounded
set of live generals with portrait, military merit, loyalty, ambition, and
current command. Ongoing military-governorate information and actions are
integrated into the existing vassal-management window. No separate military-
governorate management window is added.

Vassal map modes retain the existing hierarchy and add a military-governorate marker and command suffix so same-colored subjects remain distinguishable.

## Color Synchronization

On creation, copy both native primary and secondary kingdom colors from the direct suzerain through the original color APIs. Do not poll colors.

When the suzerain changes color through the original player-facing kingdom-color operation, the same event synchronizes all direct active military governorates. Ordinary vassals and tributaries are unchanged. A governorate cannot retain an independently selected color while subject.

After successful independence, the old synchronized colors are replaced with newly generated independent primary and secondary colors.

## Diplomacy And War Leadership

Military governorates cannot conduct state diplomacy. They cannot independently declare war, make peace, form alliances, create subjects, become tributaries, or send ordinary diplomatic proposals.

An external declaration against a governorate is modeled as a declaration against its root suzerain. The root suzerain becomes the main defender and peace controller; the originally targeted governorate and city remain explicit war-target facts. The governorate joins the defending side.

Whenever the suzerain starts or joins a war, every active military governorate joins with mandatory obligation. This path does not use the ordinary probabilistic vassal-obligation decision.

Peace settlement remains controlled by the suzerain. If settlement transfers governorate cities, its seat and city state are repaired immediately. Losing all cities terminates the governorate relation.

## Succession

Succession uses a dual track:

1. The suzerain may designate a valid general from the governorate or dispatch a valid general from the parent kingdom as `留后`.
2. A valid designated successor inherits immediately.
3. If the office is vacant while the suzerain remains stable, a bounded grace period allows the suzerain to appoint one.
4. If the grace period expires, the suzerain is weak, or central control is broken, the governorate army elects the strongest valid general using military merit, prowess, army support, and local service.

A dispatched parent general moves into the governorate only when succession commits. All previous military career state is closed through existing career services.

## Independence

Military governorates may launch an independence rebellion. A successful rebellion:

- ends the military-governorate relation;
- restores full diplomacy;
- restores ordinary kingdom ruler and heir titles;
- generates independent primary and secondary colors.

A failed rebellion allows the suzerain to replace both the general and designated successor through the normal appointment flow.

## Performance And Recovery

No per-frame or annual world scan is introduced.

- AI creation uses bounded annual kingdom, city, and general work and creates at most one governorate per kingdom per year.
- war participation is triggered by war-start events;
- color synchronization is triggered by the original color-change operation;
- city-state repair is triggered by city transfer;
- succession is triggered by ruler death and bounded deferred recovery;
- load repair uses persisted rows and coalesced bounded work.

Invalid seats, dead officeholders, and broken relations enter repair queues.
Opening the existing vassal-management window performs cached reads and does
not scan all actors.

## Verification

Tests must cover:

- Xia-system eligibility, direct-city over-limit rules, frontier semantics, capital exclusion, and one-per-year AI limits;
- candidate eligibility and bounded general selection;
- successful creation and rollback at every transaction boundary;
- command naming and presentation as `将军`, `留后`, and `军`;
- creation-time and event-driven primary/secondary color synchronization without affecting ordinary subjects;
- blocked diplomacy, mandatory two-way war participation, suzerain war leadership, and suzerain-controlled peace;
- designated and military-elected succession;
- city loss, total territorial loss, rebellion success, and rebellion failure;
- save/load projection recovery and source guards against unbounded scans.

Verification includes focused rule tests, source guards, the complete rules project, and a main project build. Deployment copies source files only and does not deploy a newly built DLL.

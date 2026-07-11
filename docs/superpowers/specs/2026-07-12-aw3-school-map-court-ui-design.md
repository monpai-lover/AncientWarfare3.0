# AW3 School Map Mode And Court UI Design

## Goal

Add a fixed-school list, religion-style detail UI, city school details, and a school
map mode while fixing the court pyramid's default placement and broken relationship
lines. The UI may reuse vanilla religion interaction patterns, but schools remain an
independent AW3 system.

This design targets new worlds only. There is no conversion from `LOCAL_SCHOOL`, old
court layout state, or old UI caches.

## Static School Registry

`CourtSchoolRegistry` defines the fourteen fixed schools. Every definition contains:

- stable ID;
- localized name and doctrine description;
- icon and fixed map color;
- national-direction vector;
- compatible office tags.

The registry has no create, rename, destroy, founder, or random-generation API. It
does not create vanilla Religion or other runtime MetaObjects.

An event-driven `SchoolMembershipIndex` tracks living actors by personal school. It
is updated when a school is assigned or changed and when an actor dies or unloads.
List counts and representative-figure queries use this index rather than enumerating
the world population.

The default list order is:

`Ru`, `Mohist`, `Dao`, `Legalist`, `Military`, `Diplomat`, `Agrarian`,
`YinYang`, `Logician`, `Medical`, `Syncretist`, `Merchant`, `Craftsman`, `Historian`.

## City School Snapshot

A city may contain multiple school influences but has at most one dominant school.
`CitySchoolSnapshot` stores a score for every represented school, total score,
dominant school, and refresh generation.

Only political elites contribute:

- king: 8, capital only;
- heir: 5, actual city only;
- city leader: 5;
- central official: 4, actual city only;
- general: 3, stationed city only;
- local official: 2 to 4 according to rank.

One person holding multiple roles contributes only the highest weight. A bounded
ability modifier changes the contribution by at most 20 percent. Dead, imprisoned,
departed, wrong-kingdom, or otherwise invalid role holders do not contribute. A
person with no school contributes no school score.

Scores are divided by the total to produce city composition. The highest score is
dominant. Ties resolve by highest individual contributor, then office rank, then the
fixed registry order, producing deterministic results. A city with no contribution
has no dominant school and uses neutral gray.

National school totals aggregate city snapshots; they never rescan all actors.

## Snapshot Invalidation And Refresh

Appointment, dismissal, transfer, death, role change, personal-school change, and
king/heir replacement mark only affected cities dirty. Multiple events in one frame
coalesce into one city refresh.

The annual bounded maintenance pass, an open school window, or an active school map
mode may consume dirty work. Work is limited per frame. UI and map mode can display
the last valid snapshot while a dirty city waits in the queue, then update only that
city when refresh completes.

The snapshot builder reads role and court caches. It must not enumerate city
population. When the school window and map mode are closed, there is no per-frame UI
or map color work.

## School Window

The world UI has a dedicated School button. Opening the window enters school map
mode. Closing it restores the map mode that was active before the window opened.

The left list always contains all fourteen schools. Default order is fixed historical
order; controls may switch to total influence or dominant-city count. Each row shows
icon, color, name, adherent count, official count, dominant-city count, and total
influence.

The right pane has two detail states.

### School Detail

The school detail shows doctrine, livelihood/war/aggression/peace/order/commerce/
technology direction, compatible offices, top cities, top kingdoms, and up to five
living representative figures. Representatives are ranked by office, ability, and
political reach. There is no unique school leader.

### City Detail

The city detail shows city, kingdom, dominant school, horizontal composition bars,
and influence sources sorted by actual contribution. Each source identifies king,
heir, leader, central official, local official, or general. Clicking a person opens
the actor window; clicking a school returns to school detail.

An empty city displays "no school influence". No-school is never added as a list row.

School icons in the court, family tree, and biography open the corresponding detail.

## School Map Mode

Overview colors each city zone by its dominant school. No-influence cities use neutral
gray. A fixed legend shows all fourteen schools and supports direct selection.

Selecting a school enters focus mode:

- the selected school's city share controls color intensity;
- other cities are desaturated but retain borders and kingdom readability;
- clicking a city opens its city-school detail.

The hover tooltip is rebuilt from the city currently under the pointer. It shows the
dominant school and the top three shares and must never reuse a previous city's text.
Clicking a kingdom area without a city target must not substitute capital data.

## Court Pyramid Content

The mature court is arranged by actual office rank, top to bottom, rather than by
school. The king is at the apex, the heir follows, and civil officials, military
officials, physicians, and generals occupy their actual rank layers.

The primitive court always shows king, heir, all city leaders, and generals. It does
not fabricate ministers or schools.

Every actor card shows portrait, name, office, country-color frame, and personal
school. An unaligned actor shows localized "No school" and no school icon; it never
uses the generic knowledge icon or a Ru fallback.

## Court Coordinates And Links

Canvas, actor cards, and links use one top-left coordinate system. Layout bounds use
`padding - minX`; no centered offset is applied after node positions are calculated.

Hard acceptance requirements are:

- first open starts at a small left inset, not shifted to the right;
- reopen, kingdom switch, and roster refresh preserve the correct default origin;
- king remains centered over the hierarchy;
- cards at every rank remain within the calculated canvas bounds;
- drag and zoom do not separate cards from their links.

Links are orthogonal hierarchy segments:

1. parent bottom-center to a shared vertical midpoint;
2. a horizontal bus over the child group;
3. one vertical branch to each child top-center.

Links must not cross cards, target empty slots, extend outside the canvas, or use a
coordinate anchor different from actor cards. Card placement and link segment
generation are pure rules and are recalculated after layout, zoom, and roster change.

## Localization And Resource Validation

Provide Simplified Chinese, English, and Traditional Chinese for:

- fourteen school names and descriptions;
- no-school text;
- school window, list, detail, city detail, sorting, counts, directions, and tooltips;
- missing court labels used by primitive and mature layouts.

A resource completeness rule requires every school to have name, description, icon,
color, and direction data. Missing data logs a precise error and uses a neutral
placeholder; it must not expose `name`, an internal ID, or a different school's label.

## Performance And Verification

UI pools list rows, portraits, cards, and link segments. Ordinary refresh updates only
changed counts and the active detail; it does not rebuild every portrait each frame.

Tests must cover snapshot weights, role de-duplication, deterministic ties, dirty-city
coalescing, per-frame budgets, overview/focus colors, current-pointer tooltips, fixed
list sorting, empty-school presentation, and resource completeness.

Pure court-layout tests must cover one node, multiple ranks, uneven child groups,
empty layers, primitive roster, mature roster, initial left inset, reopen, zoom, and
orthogonal segment endpoints. Manual verification must confirm cards and links remain
aligned while dragging, zooming, switching kingdoms, and changing the roster.

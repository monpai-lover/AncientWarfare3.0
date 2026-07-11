# AW3 Historical Schools, Officials, Clans, And State Direction Design

## Goal

Replace office-driven school labels with persistent personal intellectual identities,
make central-office eligibility and official clan creation historically coherent, and
let the composition of a court influence national policy without rewriting a person's
school whenever their office changes.

This design targets new worlds only. The mod has not been released, so no legacy save,
legacy school assignment, legacy clan, or database migration path is required.

## Fixed Schools

AW3 has exactly these fourteen schools:

`Ru`, `Mohist`, `Dao`, `Legalist`, `Military`, `Diplomat`, `Agrarian`,
`YinYang`, `Logician`, `Medical`, `Syncretist`, `Merchant`, `Craftsman`, and
`Historian`.

Schools are static definitions, not randomly generated runtime objects. A person may
also have no school. "No school" is a valid absence of identity and is not a hidden
fifteenth school.

## Personal School Ownership

The persisted personal-school field is the sole source of truth. School traits are
presentation mirrors used by actor UI; offices and city snapshots are consumers.

A school is resolved only at bounded lifecycle events:

- first appointment to a central office;
- promotion to city leader or general;
- becoming king or heir;
- an explicit future conversion or teaching event.

Ordinary residents are never scanned annually. Resolution uses, in order:

1. an already valid personal school;
2. a parental school or explicit teaching source;
3. the current city's school composition;
4. stewardship, diplomacy, warfare, intelligence, and other relevant attributes;
5. a small deterministic jitter used only to break otherwise equal choices.

The resolver may return no school. Appointment compatibility adds only a small score:
physicians prefer Medical candidates, divination or calendar work prefers YinYang,
and military offices prefer Military, but a strong unaligned person remains eligible.
A general contributes to the military faction without automatically becoming Military.

Clearing, changing, or retiring from an office never clears the personal school.
`CourtDirectionService`, candidate selection, read models, actor cards, and traits must
all read the persisted identity and must not fabricate Ru or Military fallbacks.

## Office Eligibility

Central court offices are male-only. The restriction applies at candidate filtering,
appointment validation, and the normal annual central-roster validation pass.

The restriction does not apply to:

- kings or heirs;
- city leaders;
- generals.

A female central official discovered during the ordinary validation pass is dismissed
through the normal office lifecycle. There is no world-wide corrective scan.

## Official Shi And Visible Clan Lifecycle

Appointment to a central office, promotion to city leader, and promotion to general
call one event-driven `EnsureOfficialShiAndClan` operation.

The operation follows these rules:

1. Preserve an existing valid AW3 Shi and visible vanilla Clan.
2. If the person inherited a Shi and the ancestor's visible Clan exists, join that
   Clan instead of founding a duplicate.
3. If the person truly has no Shi, grant a new branch with source `official_grant`,
   create a vanilla Clan, and name the visible Clan after the completed Shi data.
4. Office changes, dismissal, school changes, and retirement do not change the Shi.

When an official receives a Clan after children already exist, synchronize at most 512
descendants whose `LINEAGE_ID` and `SHI_ID` still match the official. Preserve a
descendant that founded a distinct branch. This is a bounded event operation, not a
world scan.

At birth, AW3 first chooses the patrilineal lineage and Shi source, then assigns the
child to the same visible vanilla Clan. Vanilla random maternal/paternal Clan choice
must not diverge from the persisted AW3 Shi. Descendants inherit both the Shi data and
visible Clan consistently.

## Office And Biography History

Every central appointment, city leadership, generalship, transfer, and dismissal uses
one office-history writer. The current office appears in the family tree and actor
biography, and completed tenures remain in the biography after dismissal.

The history record stores actor, kingdom, office, layer, city where applicable,
appointment time, end time, and end reason. UI read models consume this shared record
instead of reconstructing history from current traits.

## Court Direction Model

Each school has a fixed vector over livelihood, war, aggression, peace, order,
commerce, and technology. The current national direction aggregates only valid court
participants:

- king has the highest weight;
- heir and senior central officers follow;
- other central officers and city leaders use office-rank weights;
- generals add military-faction weight;
- one person holding multiple roles contributes once at the highest applicable weight.

An unaligned person adds no school vector, although their office may add a small
administrative baseline. Direction values use smoothing and hysteresis so a death or
single appointment does not make AI policy oscillate every year.

Direction modifies AI weights rather than forcing outcomes:

- livelihood favors development, relief, and stability;
- aggression favors claims and offensive wars;
- peace favors diplomacy, alliances, and settlements;
- commerce and technology affect policy research priorities;
- war favors military preparation without automatically forcing a declaration.

Republics use the same aggregation. Republic UI titles change to head of state and
elder, but the data model and weighting remain identical.

## Performance And Safety

- No annual population scan is allowed.
- School assignment runs only on explicit lifecycle events.
- Central gender validation reuses the bounded court roster.
- Clan descendant synchronization is capped at 512 actors.
- Duplicate event delivery must be idempotent for school, Shi, Clan, and history.
- All caches are cleared when the world unloads.

## Verification

Pure-rule tests must cover:

- a valid personal school surviving appointment, transfer, and dismissal;
- no-school remaining no-school instead of becoming Ru or Military;
- parental, city, attribute, and deterministic tie-break inputs;
- office compatibility changing candidate score without forcing identity;
- male-only central eligibility and gender-neutral city/general eligibility;
- existing Shi reuse, new official grant, visible Clan creation, and idempotence;
- patrilineal child Shi and visible Clan consistency;
- preservation of a descendant's distinct branch;
- role de-duplication and stable direction hysteresis.

Integration verification must cover appointment, dismissal, birth, transfer, death,
biography history, family-tree office display, and AI direction updates without a
world population scan.

# Historical Figure Card Recycle Window Design

Date: 2026-09-04
Status: Approved for implementation planning

## 1. Goal

Move card recycling out of the inventory view into a dedicated recycle window.
The inventory remains responsible for browsing card ownership, viewing details,
and deploying a card. The recycle window is responsible for temporary input
selection, quality filtering, previewing the result, and submitting one atomic
recycle transaction.

The existing recycle rules, collection store, source tracking, and dynasty-crate
weighting remain the source of truth. The UI must not duplicate inventory
mutation logic.

The card catalogue also distinguishes two top-level card roles: Monarch and
Minister. The role controls which broad crate category can draw the card and
which deployment path is available. Existing period crate IDs remain stable so
saved source counts continue to load.

## 2. User-visible behavior

### 2.1 Window layout

Create `HistoricalFigureRecycleWindow` using the existing `AbstractWindow` and
AW3 UI styling conventions. The window contains:

- Left panel: owned cards that are currently eligible for recycling.
- Right panel: fixed input slots, selected quality, required count, next quality,
  source-crate weights, and a result preview.
- Footer: reset/clear, submit recycle, and return to inventory controls.

The left list must use a real scroll container so the full eligible collection
can be browsed. Card dimensions are fixed so changing names or counts cannot
move neighboring cards.

### 2.2 Card crate categories

The crate browser exposes two top-level categories:

- Monarch crates: contain monarch-role cards. Deploying one to a valid target
  city creates a new kingdom using the card's historical kingdom name and makes
  the deployed actor its ruler.
- Minister crates: contain minister-role cards. Deploying one requires a valid
  city with a living civil kingdom, adds the actor to that kingdom's official
  candidate pool, and never creates or renames a kingdom.

The six existing historical-period crate IDs remain the persisted source IDs.
The category is a catalogue/UI filter over those period crates, so old
`ownedCrateCounts` entries and recycle source weights remain compatible.

Minister deployment uses the target city's species and joins that city. It
does not deploy to an unowned tile because an unowned tile has no court. The
actor remains subject to the normal adult, alive, non-king, non-heir, non-slave,
and no-existing-office checks. Historical minister eligibility may satisfy the
normal qualification gate, but it does not bypass those identity and safety
checks. A positive fixed candidate-score bonus makes the minister more likely
to be selected when an office vacancy is evaluated; it is not an immediate
appointment and does not guarantee a specific office.

### 2.3 Quality filter

When the window opens, the left list contains every non-gold card with an owned
count greater than zero.

The first card added to the right panel locks the selected quality. From that
point, the left list contains only cards with the same quality. Cards of other
qualities are hidden until the selection is cleared.

The quality lock remains active while one or more inputs remain selected. Removing
some inputs does not unlock it. Clearing all inputs or pressing reset unlocks the
quality and restores the complete eligible list.

Cards cannot be mixed across qualities. Gold cards are never eligible for
recycling.

### 2.4 Slot and quantity behavior

The UI uses one slot per consumed card. The same card definition may occupy
multiple slots, limited by the owned count. A card cannot be added after the
required count is reached.

The required counts are taken from `HistoricalFigureCardRecycleRules`:

- Blue, purple, and pink: 10 cards for the next quality.
- Red: 5 cards for gold.

The selected card list, slot contents, and counts are refreshed after every
add/remove operation. Removing a slot returns that card to the available count
without changing the quality lock unless it removes the final input.

Recycling remains quality-only: the UI does not mix qualities, and the two
crate categories do not add a second hidden input constraint. Output source
selection continues to use the existing period-crate source weights.

## 3. Components and responsibilities

### 3.1 `HistoricalFigureRecycleWindow`

Owns only view state and transient selection state:

- selected quality, if any;
- selected card counts by card ID;
- ordered slot contents;
- current result/error presentation;
- list and detail refreshes.

It delegates all rule validation and persistence to existing services.

### 3.2 Existing rule and store services

Reuse the following contracts:

- `HistoricalFigureCardRecycleRules.TryCreatePlan` validates input quality,
  input count, and output quality.
- `HistoricalFigureCardCollectionStore.TryRecycle` performs the atomic
  consume-and-add operation and records the output crate source.
- `GetRecycleSourceCounts` and the existing source selection logic provide the
  dynasty-crate weighting.
- `HistoricalFigureCardCatalog` resolves card definitions, portraits, names,
  biographies, and rarity colors.

The old recycle controls in `HistoricalFigureDrawWindow` are removed or changed
to an entry point only. There must be one active recycle state owner.

## 4. Data flow

1. Open the recycle window and read the collection snapshot.
2. Build the eligible left list from owned counts and non-gold rarity.
3. On the first card click, set the quality lock and refresh the left list.
4. On subsequent clicks, reject a different quality and add an allowed card to
   the next free slot when ownership and required count allow it.
5. Recalculate selected count, next quality, source weights, and result preview.
6. On submit, expand ordered slots into card IDs and create a recycle plan.
7. Resolve the output card using the existing weighted dynasty-crate result
   rules.
8. Call `TryRecycle` once with a unique recycle transaction ID.
9. On success, refresh inventory data and show the output portrait, name,
   biography, rarity, and collection/crate source.
10. On failure, keep the selected slots and show a localized error.

No card is consumed during selection. A failed validation or failed persistence
operation leaves both the collection and the current selection unchanged.

## 5. Preview and localization

The right panel displays:

- selected quality and its localized name;
- selected count and required count;
- next quality;
- eligible source dynasties and their relative weights;
- possible output card range when the existing catalog can provide it.

All user-facing text, including empty states, quality names, validation errors,
buttons, and result details, uses the existing AW3 localization mechanism.
Missing optional portraits or audio must fall back without blocking recycling.

## 6. Error handling

The UI shows localized messages for:

- no eligible cards;
- gold card selection;
- mixed qualities;
- insufficient owned quantity;
- incomplete input count;
- missing catalog card or crate;
- duplicate or already-completed recycle transaction;
- collection persistence failure.

The submit button is disabled until the required count is selected. The service
still validates every condition because UI state can become stale while the
window is open.

## 7. Tests and acceptance

Add focused tests before production changes for:

- all eligible qualities visible on initial load;
- first-card selection filtering the left list to the same quality;
- other qualities remaining hidden until reset or complete clear;
- repeated selection of one card being limited by owned count;
- mixed-quality input being rejected;
- slot removal preserving the quality lock while inputs remain;
- blue/purple/pink requiring 10 and red requiring 5;
- gold never being eligible;
- successful recycle consuming inputs and adding one output;
- failed recycle preserving inventory and selection;
- source-crate weighting and output source recording remaining unchanged.

Manual acceptance must verify the list is scrollable, selection updates happen
immediately, reset restores all qualities, and reopening the window starts with
no stale selection.

## 8. Scope exclusions

This change does not alter rarity probabilities, card catalog data, deployment
rules, card portraits, audio assets, or dynasty-crate definitions. It does not
add automatic recycling or multi-submit behavior.

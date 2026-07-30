# Chronicle Text Export Design

## Goal

Add a persistent `Export TXT` action to AW3's shared chronicle window. The
action exports the complete history of the object currently open in the window
to a readable text file inside the active WorldBox save directory.

## Scope

`HistoryListWindow` has three sources. Each source receives the same fixed
toolbar action:

- Person: every biography event for the current actor, regardless of the UI
  category filter.
- Kingdom: every dynasty, reign period, and event for the current kingdom,
  including collapsed sections.
- City: every ownership period and event for the current city, including
  collapsed sections.

Family trees, rosters, court screens, and non-chronicle views are out of
scope. They do not use `HistoryListWindow` and do not represent a chronological
record export.

## User Experience

The button is fixed at the top-right of the history window and uses the
existing AW3 button styling. It remains visible while the list scrolls. The
label is localized as `Export TXT`; its tooltip identifies the active record
type and destination directory.

The action is enabled only when an active save directory has been observed. A
newly generated, unsaved world has no valid destination; its disabled tooltip
instructs the player to save the world first.

After a successful export, the button/tooltip reports the generated file name.
On failure it keeps the window open and reports a concise, actionable reason.

## Output Contract

Exports are written below the active WorldBox save directory:

```text
<save-directory>/aw3_exports/chronicles/
  person_<safe-name>_<actor-id>_<timestamp>.txt
  kingdom_<safe-name>_<kingdom-id>_<timestamp>.txt
  city_<safe-name>_<city-id>_<timestamp>.txt
```

`<safe-name>` removes Windows-invalid path characters. `<timestamp>` is a
local wall-clock value with sufficient precision to keep repeat clicks from
overwriting prior exports.

Each document is UTF-8 with BOM so Chinese text opens correctly in Windows
Notepad. The document begins with the AW3 export title, record type, object
name/ID, and export time. It then preserves the normal chronological ordering:

- Every event writes its captured `YEAR_PREFIX`: the WorldBox year together
  with the state era name/regnal-year snapshot that applied when the event was
  recorded. This is the historical chronicle date, not the machine export time.
- Person records are written as a chronological event list with the event's
  chronicle date on every entry.
- Kingdom records are grouped by dynasty, then reign period, then event; each
  dynasty and reign heading includes its captured start/end chronicle dates.
- City records are grouped by ownership period, then event; each ownership
  period heading includes its captured start/end chronicle dates.

Output uses each record's plain-text snapshot fields. It never writes Unity rich
text tags or requires a live actor, kingdom, or city to still exist.

## Architecture

A small save-directory registry owns the current save location. It observes
both successful saves and world loads through `AW_SavePatch`, and clears its
value for a newly generated world. This avoids coupling the feature to RTS
diagnostic services, which happen to track their own save path for unrelated
artifacts.

A dedicated chronicle exporter accepts only an immutable source kind, context
ID, display metadata, and save directory. It reads the complete data through
`HistoryQuery`, renders the source-specific document, and writes it under the
export directory. The exporter does not reuse `HistoryListWindow`'s filtered,
folded, or partially rendered rows.

The click handler schedules database read, formatting, and file I/O on the
existing historical read background path. Completion returns to the window only
to update UI feedback. The work item contains no Unity objects.

Files are written to a same-directory temporary file and atomically moved to
the final name. A failed export removes its temporary file and leaves earlier
exports intact.

## Consistency And Errors

The exporter obtains a coherent read result from the archive database. If the
archive is unavailable, the save path is invalid, or the background work cannot
be scheduled, it produces a typed failure result rather than throwing into the
UI. A duplicate click while an export is in flight is ignored and the button is
temporarily disabled.

Exporting does not modify chronicle records, world state, saves, or multiplayer
authority. It remains available for a loaded local save on either host or
client; each process writes only to its own active save directory.

## Verification

Source-level and focused behavioral tests must prove:

- each source renders all data even when the person category filter is active
  and sections are collapsed;
- plain-text export strips rich-text tags and preserves chronological order;
- filenames are safe, unique, and inside `aw3_exports/chronicles` below the
  supplied save root;
- the registry is populated by save/load notifications and cleared for a new,
  unsaved world;
- no-save and archive-unavailable requests return a failure result without
  creating a file;
- successful files use UTF-8 BOM and are atomically published.

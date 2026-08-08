# Ancient Warfare 3.0 v1.2.0

## Release Scope

This release combines the current `master` feature set with the validated Cultiway-derived pathfinding and large-step scheduler integration. It is distributed as a source package; no compiled DLL is included.

## New Features

- **Kingdom Atlas / 王国舆图**
  - Added the Kingdom Atlas entry beneath the Kingdom Chronicle button.
  - Generates chronological PNG pages from recorded city ownership changes and archived city-zone geometry.
  - Supports incremental continuation from the last generated event node instead of rebuilding the full history.
  - Supports PNG-first generation with optional GIF export, selectable output resolution, progress percentage, and saved artifacts in the save directory.
  - Atlas pages can be zoomed and dragged. Each event node shows the participating kingdoms' chronicle text and renders only the relevant kingdoms, their borders, colors, and map-mode country names.
  - Vassals use the suzerain color while retaining their own border and country label.

- **Custom titles / 自定义头衔**
  - Added virtual noble-title definitions, title grants, title roster display, localization, and title persistence.
  - Kingdom/person views can display the resolved social, office, heir, military, and virtual titles together.

- **Manual actor renaming / 新人物改名器**
  - Added branch-aware manual actor name editing from the lineage UI.
  - Manual given names and surnames persist across restore and succession transitions.
  - Western-family and Xiaized naming paths now retain their intended surname and given-name rules.

## Performance and Simulation Fixes

- **Cultiway-derived pathfinding**
  - Added path sessions, bounded queues, continuation segments, stale-work rejection, cancellation, traversal snapshots, route caching, and shared worker budgeting.
  - RTS routes now use the shared pathfinder instead of maintaining a separate high-frequency route loop.
  - Added physical dock and water-connectivity support for boat transport, passenger scheduling, and dock route reuse.
  - Added path diagnostics and queue-pressure visibility for runtime diagnosis.

- **Large-step scheduler**
  - Added persistent simulation workers and cooperative actor post-processing.
  - Moved actor tile actions, enemy searches, path movement, smooth movement, and world-maintenance batches behind bounded worker tickets with main-thread commit barriers.
  - Added incremental actor/chunk/zone membership indexes, nearby-status target indexing, enemy-presence caching, deferred path-request batches, and free-tile search reuse.
  - Preserved native presentation paths by default and guarded snapshot presentation behind explicit settings to prevent animation and rendering regressions.
  - Added scheduler lifecycle, shutdown, stale projection, and worker-pool safety checks.

- **Succession and identity repair**
  - Repairs invalid capitals, stale kingdom/city affiliation, royal-guard residue, guest-office state, and delayed accession retries.
  - Includes exponential retry backoff and idempotent accession installation.

## Validation

- `dotnet build AncientWarfare3.csproj --no-restore` passed with 0 errors.
- Accession identity retry rules passed.
- Presentation snapshot performance source guard passed.

## Packaging

- Source-only release package.
- Build outputs, temporary files, logs, databases, worktrees, and local development metadata are excluded.

# Central And Local Court JSON Separation Design

## Goal

Make the custom central court editor and custom local government editor import and export independent JSON templates. A central import must never overwrite local templates, and a local import must never overwrite central offices. Local government cards must display the bilingual office names stored in their template instead of leaking generated localization keys.

## Storage Contract

- Central court files live in `Courtjson/Central/*.json`.
- Local government files live in `Courtjson/Local/*.json`.
- Both file kinds retain the existing validated `CustomCourtTemplate` JSON envelope for backward-compatible validation and atomic storage.
- A central document contains `Name`, `Offices`, and `Edges`; `LocalTemplates` and `ArchivedCrossLayerEdges` are empty.
- A local document contains exactly one `LocalTemplates` entry; central `Offices`, central `Edges`, and archived cross-layer edges are empty.
- Files placed in the wrong directory are rejected instead of being partially interpreted.

## Editor Behavior

The existing import dropdown is context-sensitive. In central mode it lists only `Central` files. In local mode, including the city local-government entry, it lists only `Local` files. Switching editor context immediately refreshes the list and restores that context's independent selection.

Central import replaces only the working template's central ID, revision, name, offices, and edges. It preserves all current local templates and pending city-template rebindings. Local import replaces the local template with the same ID, or appends and selects it when the ID is new and the template limit permits. It preserves every central field and all unrelated local templates.

Save and Export use the active editor context. Their status text reports the actual `Central` or `Local` path.

## Name Resolution

`CustomCourtRuntime.OfficeDisplayName` first checks central offices, then local-template offices. Because local office IDs are validated and rebased as stable IDs, the matching template-owned `CustomCourtLocalizedText` is the source of truth. Built-in localization remains the fallback when no custom definition exists.

## Failure Handling

- Invalid JSON, mixed-context documents, unsafe paths, graph errors, and over-limit local imports leave the working template unchanged.
- Atomic save behavior remains unchanged: write temporary JSON, read and validate it, compare the normalized hash, then replace the destination.
- Empty directories show context-specific localized captions.

## Verification

Rules tests cover path separation, central/local document classification, central merge isolation, local merge isolation, same-ID replacement, new-ID insertion, limit rejection, and local office-name lookup. Source guards cover the context-sensitive window wiring. The full rules executable and production project build must pass before deployment.

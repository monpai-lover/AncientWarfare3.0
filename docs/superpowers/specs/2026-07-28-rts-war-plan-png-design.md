# RTS War Plan Animated GIF Design

## Purpose

Army RTS diagnostics emit a compact visual history of each war/session sequence. The history is retained during play and written only during a normal WorldBox/mod shutdown as one animated GIF89a and, optionally, one compact text manifest per sequence.

## Enable And Lifecycle Contract

`AWPerformanceSettings.ArmyRtsDiagnosticsEnabled` is the sole diagnostics gate. When it is false, snapshot capture, raster work, queueing, and file output stop, and all pending in-memory sequences are discarded. Existing artifacts in save directories are never deleted.

Save/load callbacks only associate a sequence with its normalized save directory. They do not publish frames. World clear closes the current world generation and retains its bounded completed sequences for shutdown. Normal shutdown synchronously encodes retained sequences within the existing finite shutdown budget. A crash may lose an unfinished sequence.

## Main-Thread Capture

All WorldBox and Unity state is read on the authoritative main thread. Capture samples actual `WorldTile.getColor()` values into the projected canvas and derives zone ownership from `tile.zone?.city?.kingdom`. The immutable snapshot also records participant kingdoms, cities and controllers, active fronts, Army missions, route anchors, transport state, recovery state, player orders, and stalled state.

Material fingerprints exclude position-only movement but include terrain/ownership changes and strategic assignments. A five-second per-war revision cadence and newest-pending coalescing prevent request churn.

## Indexed Rendering

The renderer writes directly into an indexed frame using a deterministic 256-entry 3-3-2 RGB global palette. One retained pixel consumes one byte; full RGBA frames are never retained. Real terrain colors form the base, kingdom-zone tint and ownership boundaries remain visible, and city/front/arrow overlays use stable nearest-palette colors.

Arrow semantics remain distinct:

- attack and pursuit: red;
- friendly-city recovery: gold;
- defense, retreat, and redeploy: blue;
- active transport: cyan dashed route with transport marker;
- rally/hold: white;
- stalled assignment: magenta marker.

The projected canvas preserves world aspect ratio and has a configurable default maximum long edge of 768 pixels.

## Bounded Retention

Bounds are named constants and constructor parameters where tests need smaller values:

- maximum 32 frames in one sequence;
- maximum 48 frames across all retained sequences;
- maximum 8 retained sequences;
- default GIF display delay 75 centiseconds.

Identical material fingerprints are suppressed. When a sequence reaches its frame limit, the first and latest frames are preserved and interior history is deterministically decimated before the new latest frame is appended. Global pressure evicts the oldest completed sequence first; the active sequence remains bounded by the same rules. At a 768-pixel square worst case, 48 indexed frames retain about 28 MiB plus small metadata overhead.

## GIF Encoding And Files

The repository-native encoder writes GIF89a with one deterministic global color table, a looping Netscape application extension, a graphics control extension and image descriptor per frame, GIF LZW image data, and the `0x3B` trailer. No external package or DLL is introduced.

At shutdown the writer creates the save's `aw3_rts_plans` directory and writes each GIF and manifest to unique temporary files. After successful flush it atomically replaces the destination files. Failures are isolated and reported once without changing simulation state; partial temporary files never appear as completed GIF artifacts.

## Verification

Focused tests must demonstrate RED before implementation, then prove:

- deterministic indexed palette and real terrain/ownership pixels;
- recovery, redeploy, transport, attack, and stalled colors remain distinct;
- fingerprint deduplication and cadence coalescing;
- per-sequence/global bounds preserve first/latest frames;
- GIF89a signature, loop extension, dimensions, frame count, LZW decodability, and trailer;
- no PNG or per-frame manifest files are created;
- save observation writes nothing before shutdown;
- diagnostics-off discard leaves historical artifacts untouched;
- shutdown writes GIF/manifest atomically and isolates failures;
- Debug and Release builds succeed.

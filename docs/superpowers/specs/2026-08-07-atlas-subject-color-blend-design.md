# Atlas Subject Color Blend Design

## Goal

Keep vassal and tributary territories visually grouped under their root
suzerain while preserving a small amount of each subject's own historical
color.

## Behavior

- Independent kingdoms and root suzerains retain their own color unchanged.
- Every active vassal or tributary uses 80% root-suzerain color and 20% of its
  own historical color.
- Nested subjects resolve the root suzerain first, then blend once with the
  current subject's own color. Parent-subject colors do not accumulate.
- If either color is unavailable, retain the existing fallback behavior.
- Alpha remains the existing display alpha; only RGB channels are blended.
- Borders, country labels, relation history, territory reconstruction, and
  map generation are outside this change.

## Implementation Boundary

Change only `KingdomAtlasRules.BuildDisplayColors` and its focused rules tests.
Use a small pure color-blend helper so rounding is deterministic and testable.
The game deployment receives the changed source file only; no DLL is deployed.

## Verification

- A direct subject receives the expected 80/20 RGB blend.
- A nested subject blends its own color with the root suzerain color exactly
  once.
- A tributary receives the same 80/20 treatment.
- Root and independent kingdom colors are unchanged.
- Missing colors preserve current fallbacks.

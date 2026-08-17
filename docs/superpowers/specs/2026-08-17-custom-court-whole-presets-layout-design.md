# Custom Court Whole Presets And Layout Design

## Goal

Add whole-court built-in presets to the custom court workflow. Selecting a
preset replaces the current editor graph with the complete office set for that
institution. Move both the tool panel and canvas upward by 50 pixels without
changing the window size or horizontal layout.

## Preset Source

Built-in presets are generated from the selected kingdom's existing
`ICourtProfile` and institution definitions. The editor must not maintain a
second static JSON copy of built-in offices.

- Xia profile: Zhou, Han, Tang, and Song institutions.
- Western profile: bureaucratic and feudal-bureaucratic institutions.
- Only institutions compatible with the kingdom's profile are listed.
- The current institution and earlier unlocked stages are enabled.
- Later stages remain visible but disabled with a localized lock message.

Each generated office copies the built-in display name, layer, grade,
preferred school, and military capability. Slots, requirements, effects, and
editor coordinates use the custom court defaults unless the built-in
definition already owns that data.

## Replacement Behavior

The whole-court preset dropdown appears below the court-name input. Selecting
an enabled preset immediately replaces `CustomCourtTemplate.Offices` and
`CustomCourtTemplate.Edges`.

- Existing nodes and links are discarded.
- The player-entered court name is preserved.
- The template ID and revision remain stable.
- The office-name input and current edge selection are cleared.
- Cards are regenerated around the canvas center.
- Built-in definitions do not invent management or appointment-prerequisite
  edges that do not exist in the source model. Runtime court layout continues
  to derive hierarchy from office layer and grade.
- A localized status message identifies the loaded preset.

Selecting a disabled preset leaves the current graph unchanged and shows the
localized unlock requirement.

## Layout

The new dropdown consumes one toolbar row. Existing toolbar controls below it
shift downward by one row while retaining their current order and size.

The final window positioning change is independent of the added control:

- `_toolPanel.anchoredPosition.y` increases from `-4` to `46`.
- `_canvasRect.anchoredPosition.y` increases from `0` to `50`.
- X positions, canvas dimensions, tool-panel dimensions, and window dimensions
  remain unchanged.

## Failure Handling

- Missing kingdom or profile: show an unavailable status and do not mutate the
  graph.
- Empty preset office set: reject the selection and preserve the graph.
- Locked preset: show its lock message and preserve the graph.
- Duplicate office IDs from source data are normalized deterministically before
  replacing the graph.

## Testing

- Rules tests cover profile-compatible preset choices and institution unlock
  ordering.
- Rules tests cover deterministic conversion from office definitions into a
  replacement custom template graph.
- Source guards verify the whole-preset dropdown, direct-replacement callback,
  locked-option handling, and the exact `46`/`50` vertical positions.
- Existing custom court template, effect, and multiplayer tests remain green.
- The net48 release target must build with zero errors.

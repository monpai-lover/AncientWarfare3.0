# Family Tree Entry Back Button Design

## Goal

Add a dedicated entry-return button beside the family tree window's close
button. It returns to the window that opened the family tree, including actor,
lineage list, history, and other supported WorldBox windows.

## Interaction

- Place a 24 x 24 icon button immediately left of the red close button.
- Use the existing right-arrow asset mirrored horizontally as a back arrow.
- Show the localized tooltip `Return to Entry`.
- On click, call the vanilla `WindowHistory.clickBack()` navigation path.
- If no valid history entry exists, retain vanilla fallback behavior, which
  closes the active window.

## Scope

- Change only `FamilyTreeWindow` chrome and localization.
- Keep the existing family-mode `Back to Clan Tree` toolbar button unchanged;
  it navigates inside the family tree feature and has different semantics.
- Do not add a second navigation stack or manually record source window types.

## Verification

- Opening the family tree from an actor window and pressing the new button
  returns to that actor window.
- Opening it from a lineage or history window returns to that window.
- The close button remains clickable and visually unobstructed.
- The button remains aligned after the wide family tree window layout runs.
- Production build and family-tree rule tests pass.

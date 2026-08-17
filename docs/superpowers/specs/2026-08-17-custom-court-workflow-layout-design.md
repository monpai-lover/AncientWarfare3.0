# Custom Court Workflow Layout Design

## Goal

Make the custom court workflow editor visually consistent with the existing court
window. The editor must retain a usable fixed toolbar, a pannable workspace, and
office nodes that look like court actor cards while remaining definition-only
placeholders.

## Layout

- Restore the window to the existing court wide-window proportions.
- Keep the title bar and close control inside the window chrome.
- Reserve the right side for a fixed toolbar. The toolbar is anchored to the
  viewport, never to the pannable workspace.
- Clip the workspace to the central viewport. A large workspace may be panned
  and zoomed without changing the viewport or toolbar bounds.
- Store node positions in workspace coordinates so resizing or reopening the
  window does not rewrite the template layout.

## Vacancy Card

- Add a workflow-only vacancy card component using the visual structure of
  `CourtActorNodeView`.
- Use the court frame, dark panel, avatar area, title, subtitle, and delete
  control style from the existing court UI.
- Render a static empty-slot placeholder in the avatar area; never create or
  bind a real `Actor`.
- Show the custom office name as the title and the localized vacancy label as
  the subtitle.
- Preserve click-to-select, drag-to-move, and edge connection behavior.

## Data Flow

- The card binds only to `CustomCourtOffice`.
- Saving writes office definitions, workspace coordinates, and edges exactly as
  the existing custom template store expects.
- Import remains compatible with existing templates and missing layout values
  use the current default placement behavior.
- The editor never mutates live court appointments while a card is selected or
  moved.

## Verification

- Add source guards for the fixed viewport/toolbar anchoring and vacancy-card
  structure.
- Run the custom-court workflow tests and the complete rules test suite.
- Build the Release assembly and deploy to the local WorldBox Mods directory.
- Verify that toolbar bounds remain inside the viewport and that an empty office
  renders without requiring an Actor or live court data.

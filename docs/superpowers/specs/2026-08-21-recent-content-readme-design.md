# Recent Content README Design

## Goal

Publish a player-facing introduction to the playable work merged from
2026-08-18 through 2026-08-21, then expose it prominently from the repository
README.

## Deliverables

- Add `NEW_CONTENT_2026-08-21.md` at the repository root.
- Add a short latest-content entry near the top of `README.md` linking to the
  new document.
- Push both documents to `master` on GitHub.

## Audience And Tone

The document is written for players. It describes visible gameplay changes,
where players encounter them, and how the systems affect a campaign. Internal
class names, raw commit lists, implementation plans, and source-guard details
are omitted.

## Scope

The source of truth is the repository history dated 2026-08-18 through
2026-08-21. Design-only, test-only, merge, and maintenance commits may support
the review but are not presented as playable features unless corresponding
runtime implementation exists in the same period.

The content is grouped into:

1. Court, harem, and local government
2. Corruption, bandits, and mass uprisings
3. Restoration, Guiyi, and historical figures
4. De jure regions, war goals, and immediate peace
5. War refugees, Xiaization, and cultural drift
6. Lineages, schools, and map modes
7. RTS marching, naval transport, and army return
8. Performance, stability, and important repairs

## Document Shape

The new document contains:

- A literal title and the covered date range
- A short overview of the update
- One section per gameplay group
- Concise bullets explaining both the new behavior and its player-visible
  result
- A final compatibility and testing note that avoids claiming runtime
  verification not supported by repository evidence

The root README receives only a compact summary and link so it remains an
installation and project overview rather than duplicating the full article.

## Accuracy Rules

- Do not describe design documents as shipped features.
- Do not infer gameplay behavior from commit subjects when runtime changes are
  absent.
- Consolidate follow-up fixes into the feature they stabilize.
- Use player terminology instead of implementation terminology.
- Mention known limitations only when they remain visible in the final tree.

## Verification

- Review the four-day commit range and representative diffs for each section.
- Confirm every major statement maps to at least one runtime commit.
- Validate Markdown links and headings.
- Run `git diff --check` before committing.

# Legacy Royal Marriage Language Design

## Goal

Prevent legacy `royal_marriage` history text from mixing languages when the
player changes the UI language. A persisted English entry such as
`A married B` must never render as `A married B缔结婚盟`.

## Root Cause

`WarDisplayLabelRules.NormalizeHistoryContent` currently chooses the missing
suffix from the current UI language. Historical rows do not persist the
language used to construct `content` or `content_rich`, so switching languages
can append a suffix from a different language to the stored sentence.

## Design

Keep the existing database schema and public normalization API. Add the royal
marriage middle phrase to `HistoryLocalizationRules`, then infer the persisted
sentence language from the exact stable middle phrase:

- simplified Chinese: `与`
- traditional Chinese: `與`
- English: ` married `

For a legacy `royal_marriage` entry without a suffix, append the suffix that
belongs to the detected source language, regardless of the current UI
language. English has an intentionally empty suffix, so English text remains
unchanged. If the source language cannot be identified, preserve the text
unchanged instead of guessing.

Normalization is idempotent across language switches. If the text already
ends in any known non-empty royal-marriage suffix, return it unchanged rather
than appending the current or detected language suffix again.

No other history event type or persisted row is changed.

## Tests

Add an isolated rules test slice that compiles the production localization and
display-label rules. Cover these cases:

1. English legacy text stays English when viewed with simplified Chinese UI.
2. Simplified Chinese legacy text receives the simplified suffix even under
   English UI.
3. Traditional Chinese legacy text receives the traditional suffix even under
   simplified Chinese UI.
4. Already normalized simplified or traditional text remains unchanged under
   every UI language.
5. Unknown-format and non-marriage text remain unchanged.

## Compatibility

This is a read-time compatibility repair. It requires no database migration,
does not rewrite save data, and preserves all current correctly localized
history text.

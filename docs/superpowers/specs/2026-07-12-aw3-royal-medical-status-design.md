# AW3 Royal Medical Care Status Design

## Goal

Represent imperial-physician care as a temporary original-style status instead of a
permanent trait, and keep the current king and heir healthy without scanning the royal
family or making them immortal.

This design targets new worlds only. The old `aw_royal_medical_care` trait and its
localization are removed directly; there is no legacy-trait cleanup or save migration.

## Status Definition

Register `aw_royal_medical_care` as a `StatusAsset` through the existing AW3 status
library pattern.

The status has:

- locale ID `status_title_aw_royal_medical_care`;
- description ID `status_description_aw_royal_medical_care`;
- icon `ui/Icons/traits/icondanyao`;
- health multiplier 0.5;
- lifespan and refresh duration 15, which spans the annual treatment interval.

Exact health and duration values remain content constants, not duplicated service
magic numbers.

## Physician Eligibility

The current valid imperial physician is read from the bounded court roster and cached
by kingdom. The physician must be alive, in the same kingdom, and hold the active
physician office. Medical-school identity improves candidate compatibility but is not
mandatory and is never forced by the appointment.

The physician cache is invalidated by appointment, dismissal, death, kingdom change,
and world unload. It never searches the population annually.

## Target Reconciliation

Each kingdom has at most two treatment targets: current king and current heir. The
service reconciles targets after physician change, king change, heir change, target
death, and the normal annual court pass.

For each valid target, the physician refreshes the status duration through
`addStatusEffect`. A target removed from the valid set is ended through
`finishStatusEffect` when the physician is dismissed, dies, changes kingdom, or the
target is replaced.

The same actor serving as both logical entries is treated once. A dead, rekt,
wrong-kingdom, or null target is ignored safely.

## Annual Treatment

The temporary status communicates continuous care and provides passive health
support. The bounded annual treatment action remains responsible for:

- restoring a controlled amount of current health;
- removing only explicitly curable disease traits or statuses;
- recording treatment history only when a material treatment occurred.

Care does not prevent combat death, execution, aging, or incurable conditions and does
not grant immortality.

## Localization And UI

Add Simplified Chinese, English, and Traditional Chinese entries for the status title
and description in the status localization resource. The actor window displays the
effect in the status area, not the permanent-trait area.

The physician's court card remains an office card. Medical personal-school identity,
if any, is displayed separately from the physician office and treatment status.

## Performance And Safety

- Reconciliation touches one cached physician and at most two targets per kingdom.
- No royal-clan, city-population, or world-actor scan is allowed.
- Duplicate reconciliation is idempotent and refreshes rather than stacks the status.
- Status and physician caches clear on world unload.
- Exceptions for one invalid target do not prevent reconciliation of the other target.

## Verification

Tests must cover:

- registration as `StatusAsset` and absence of the old trait registration;
- valid physician applying and refreshing king/heir status;
- dismissal, death, kingdom mismatch, and replacement ending old status;
- king and heir replacement moving treatment to the new targets;
- no physician producing no treatment;
- Medical preference without forced school assignment;
- annual healing, curable-condition removal, and incurable-condition preservation;
- idempotence and bounded target count;
- complete localization in three languages.

Manual verification must confirm the effect appears under statuses with a duration,
does not appear as a permanent trait, and disappears after physician loss or target
replacement.

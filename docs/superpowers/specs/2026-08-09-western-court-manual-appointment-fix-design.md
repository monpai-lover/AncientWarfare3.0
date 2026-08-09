# Western Court Manual Appointment Fix Design

## Problem

Western bureaucratic courts display valid vacant offices such as `west_executive`, but opening the appointment window fails with `该官职已不属于当前官制`.

The office catalog is correct. The failure is caused by inconsistent action gates:

- the vacancy node opens for any vacancy;
- the service permits Western manual appointment only when royal-direct appointment is unlocked;
- that permission failure is returned as `InvalidOffice` and rendered as an institution-membership error.

## Intended Rule

Both `western_bureaucratic` and `western_feudal_bureaucratic` allow the player to manually appoint every office that belongs to the current institution.

Primitive or other institutions still reject offices they do not contain. Automatic vacancy filling, ten-year mayor terms, and cross-city rotation continue to operate after manual appointment.

## Design

Centralize vacancy clickability and commit validation in one pure manual-appointment rule. It receives:

- whether the row is vacant;
- whether the office belongs to the current institution;
- whether the current institution permits manual appointment.

The vacancy card primary click, its management button, candidate-window entry, and service commit all consume this rule. Western bureaucratic and feudal-bureaucratic institutions return manual-appointment permission; office membership remains a separate check.

Add a distinct `AppointmentNotAllowed` result for any future institution that intentionally blocks manual appointment. `InvalidOffice` is reserved for a missing office or one not present in the current institution. Both results receive separate simplified Chinese, English, and traditional Chinese localization.

## Scope

Do not change the Western office catalog, office IDs, default bureaucratic migration, automatic vacancy filling, mayor rotation, career persistence, or government profile selection.

## Verification

Tests cover:

- Western bureaucratic `west_executive`: present and manually appointable;
- Western feudal-bureaucratic offices: present and manually appointable;
- an office absent from the institution: rejected as `InvalidOffice`;
- a deliberately locked institution: rejected as `AppointmentNotAllowed`;
- vacancy primary click and management button use the same rule;
- automatic appointment and ten-year rotation behavior remain unchanged;
- all localization columns contain the new result text.

Run focused court tests, Western court source guards, the complete rules project, and a main project build. Deploy source and localization files only.

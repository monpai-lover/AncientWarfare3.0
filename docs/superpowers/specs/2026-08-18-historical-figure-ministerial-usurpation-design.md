# Historical Figure Ministerial Usurpation Design

## Goal

Allow a historical figure serving as the realm's current premier to enter the
existing ministerial palace-coup path without requiring the `ambitious` trait
or being disqualified by the `content` trait.

## Eligibility Rule

`MinisterialPowerRules.IsAmbitiousUsurper` receives a third fact indicating
whether the candidate is an AW3 historical figure. A candidate has usurpation
intent when either condition is true:

1. The candidate is an AW3 historical figure.
2. The candidate has `ambitious` and does not have `content`.

The runtime recognizes historical figures through the existing
`HistoricalFigureService.TRAIT_FIGURE` and
`HistoricalFigureService.TRAIT_FIRST` markers. It does not add or remove
personality traits from the actor.

## Preserved Coup Gates

Historical status changes only the personality-intent gate. A historical
figure must still satisfy every existing ministerial coup requirement:

- be the realm's current premier and still hold the projected court office;
- serve a monarchy rather than a republic;
- remain outside an active war;
- reach ministerial power 90 before coup preparation can begin;
- reach the existing puppet-ruler threshold at power 95;
- face a weak eligible ruler for three consecutive preparation years;
- respect the realm's twenty-year palace-coup cooldown;
- pass the existing coup success calculation against royal guards, heirs,
  adult royal sons, the wider royal house, and ruler strength.

Historical figures do not receive an immediate coup, guaranteed success, or
any additional ministerial power.

## Scope Boundaries

This change applies only to the ministerial power path in
`MinisterialPowerService`. It does not change general rebellions, claimant
restorations, Mandate wars, succession disputes, feudatory Jingnan wars, or
other accession routes.

Ordinary actors retain the current `ambitious && !content` requirement.
Historical figure spawning, authored names, personality traits, family
identity, and state-name bindings remain unchanged.

## Verification

Pure rule tests cover:

- a historical figure without `ambitious` has usurpation intent;
- a historical figure with `content` still has usurpation intent;
- an ordinary `content` actor remains ineligible;
- an ordinary actor without `ambitious` remains ineligible;
- historical intent cannot bypass power, puppet-ruler, war, monarchy, or
  cooldown gates.

A source guard verifies that `MinisterialPowerService` derives the historical
figure fact from the existing figure traits and passes it into the pure rule.
The full rule suite and Release build must pass before deployment.
